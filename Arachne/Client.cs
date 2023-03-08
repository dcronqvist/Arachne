using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks.Dataflow;
using Arachne.Packets;

namespace Arachne;

public class ReceivedDataClientEventArgs : EventArgs
{
    public byte[] Data { get; private set; }
    public IPEndPoint Sender { get; private set; }

    public ReceivedDataClientEventArgs(byte[] data, IPEndPoint sender)
    {
        Data = data;
        Sender = sender;
    }
}

public sealed class Client
{
    public delegate Task<byte[]> GetChallengeResponse(byte[] challenge);

    private ISocketContext _socket;
    private IPEndPoint? _connectedTo;
    private uint _protocolID;
    private DateTime _lastPacketSent;

    private BufferBlock<byte[]> _sendQueue;
    private Task? _sendTask;
    private bool _readyToSend = false;
    private Task? _receiveTask;
    private bool _readyToReceive = false;
    private CancellationTokenSource _cts;
    private IAuthenticator _authenticator;

    // Connection stuff
    private ulong _nextSequenceNumber = 1;
    private ulong _lastReceivedSequenceNumber = 0;
    private Dictionary<ulong, (DateTime, ProtocolPacket)> _unackedPackets;

    public event EventHandler<ReceivedDataClientEventArgs>? ReceivedData;
    public event EventHandler? DisconnectedByServer;

    public Client(uint protocolID, IAuthenticator authenticator) : this(protocolID, authenticator, new UDPSocketContext())
    { }

    public Client(uint protocolID, IAuthenticator auth, ISocketContext context)
    {
        this._protocolID = protocolID;
        this._socket = context;
        this._cts = new CancellationTokenSource();
        this._sendQueue = new BufferBlock<byte[]>();
        this._unackedPackets = new();
        this._authenticator = auth;
    }

    private ulong GetNextSequenceNumber()
    {
        return this._nextSequenceNumber++;
    }

    private List<(ProtocolPacket, IPEndPoint)> GetUnackedPacketsOlderThan(TimeSpan timeout)
    {
        var now = DateTime.Now;
        var packets = new List<(ProtocolPacket, IPEndPoint)>();
        foreach (var (seq, (time, packet)) in this._unackedPackets)
        {
            if (now - time > timeout)
            {
                packets.Add((packet, this._connectedTo!));
            }
        }
        return packets;
    }

    private async Task ReliabilityLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var timeBeforeResend = TimeSpan.FromMilliseconds(500);

            var toResend = new List<(ProtocolPacket, IPEndPoint)>();

            var unacked = this.GetUnackedPacketsOlderThan(timeBeforeResend);
            foreach (var (p, d) in unacked)
            {
                this.ResendReliablePacket(p, d);
            }

            await Task.Delay(100);
        }
    }

    public async Task<(Constant, ulong)> ConnectAsync(string address, int port, GetChallengeResponse respondToChallenge, int timeout = 5000)
    {
        this._lastPacketSent = DateTime.Now;
        var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(timeout);
        var token = tokenSource.Token;

        this._socket.Connect(new IPEndPoint(IPAddress.Parse(address), port));

        // Send connect request
        var connectRequest = new ConnectionRequest(this._protocolID, 0).SetChannelType(ChannelType.ReliableOrdered);
        this.SendPacket(connectRequest);

        var response = await this.ReceivePacketAsync<ProtocolPacket>(token);

        if (response is null)
        {
            return (Constant.NO_RESPONSE, 0);
        }

        ConnectionResponse? connectResponse = null;

        if (response.PacketType == ProtocolPacketType.ConnectionChallenge)
        {
            var challenge = (ConnectionChallenge)response;
            var challResp = await respondToChallenge(challenge.Challenge);
            var challRespPacket = new ConnectionChallengeResponse(challResp).SetChannelType(ChannelType.ReliableOrdered);
            this.SendPacket(challRespPacket);
            connectResponse = await this.ReceivePacketAsync<ConnectionResponse>(token);
        }
        else
        {
            connectResponse = (ConnectionResponse)response;
        }


        if (connectResponse is null)
        {
            return (Constant.NO_RESPONSE, 0);
        }

        if (!connectResponse.IsSuccess())
        {
            return (connectResponse.Code, connectResponse.ClientID);
        }

        this._connectedTo = new IPEndPoint(IPAddress.Parse(address), port);

        // -------------------------

        var loopToken = this._cts.Token;
        this._receiveTask = Task.Run(() => this.ReceiveLoopAsync(loopToken));
        this._sendTask = Task.Run(() => this.SendLoopAsync(loopToken));
        _ = Task.Run(() => this.ReliabilityLoopAsync(loopToken));
        _ = Task.Run(() => this.KeepAliveLoopAsync(loopToken));


        while (!this._readyToSend || !this._readyToReceive)
        {
            await Task.Delay(100);
        }

        return (Constant.SUCCESS, connectResponse.ClientID);
    }

    private async Task KeepAliveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.Now;
            if (now - this._lastPacketSent > TimeSpan.FromMilliseconds(500))
            {
                this.SendPacket(new ConnectionKeepAlive().SetChannelType(ChannelType.UnreliableUnordered));
            }
            await Task.Delay(50);
        }
    }

    private async Task<ReceiveResult> ReceiveAsync(CancellationToken token)
    {
        return await this._socket.ReceiveAsClient(token);
    }

    private async Task<T> ReceivePacketAsync<T>(CancellationToken token) where T : ProtocolPacket
    {
        try
        {
            var result = await this.ReceiveAsync(token);

            using var ms = new MemoryStream(result.Data);
            using var br = new BinaryReader(ms);

            var packet = ProtocolPacket.Deserialize(br);

            return packet as T;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            this._readyToReceive = true;
            var result = await this._socket.ReceiveAsClient(token);
            _ = Task.Run(() =>
            {
                this.OnReceive(result.Data);
            });
        }

        this._readyToReceive = false;
    }

    private void Send(byte[] data)
    {
        if (this._readyToSend)
        {
            this._sendQueue.Post(data);
        }
        else
        {
            // If the sending thread is not running, just send it synchronously
            this._socket.SendAsClient(data);
            this._lastPacketSent = DateTime.Now;
        }
    }

    private async Task SendLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            this._readyToSend = true;
            var data = await this._sendQueue.ReceiveAsync(token);
            this._socket.SendAsClient(data);
            this._lastPacketSent = DateTime.Now;
        }

        this._readyToSend = false;
    }

    private void OnReceive(byte[] data)
    {
        // Handle received data
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        var packet = ProtocolPacket.Deserialize(reader);
        this.ProcessPacket(packet);
    }

    private bool FollowsOrder(ulong sequenceNumber)
    {
        return sequenceNumber > this._lastReceivedSequenceNumber;
    }

    private void SetLastReceivedSequenceNumber(ulong sequenceNumber)
    {
        this._lastReceivedSequenceNumber = sequenceNumber;
    }

    private void ProcessPacket(ProtocolPacket packet)
    {
        if (packet.Channel == ChannelType.ReliableOrdered || packet.Channel == ChannelType.UnreliableOrdered)
        {
            if (this.FollowsOrder(packet.SequenceNumber))
            {
                this.SetLastReceivedSequenceNumber(packet.SequenceNumber);
            }
            else
            {
                return; // Packet is out of order, ignore it
            }
        }

        if (packet.Channel == ChannelType.ReliableUnordered || packet.Channel == ChannelType.ReliableOrdered)
        {
            this.RegisterReliableSequenceNumberAcked(packet.GetAckedSequenceNumbers());
        }

        this.ReceivePacket(packet);
    }

    private void ReceivePacket(ProtocolPacket packet)
    {
        // TODO: Handle packet

        if (packet.PacketType == ProtocolPacketType.ConnectionTermination)
        {
            // Server wants to terminate the connection. Acknowledge
            var ack = new ConnectionTerminationAck().SetChannelType(ChannelType.ReliableOrdered);
            this.SendPacket(ack);
            // TODO: Close client.
            this.DisconnectedByServer?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (packet.PacketType == ProtocolPacketType.ApplicationData)
        {
            var appData = packet as ApplicationData;
            this.ReceivedData?.Invoke(this, new ReceivedDataClientEventArgs(appData!.Data, this._connectedTo!));
        }
    }

    internal void SendPacket(ProtocolPacket packet)
    {
        this._lastPacketSent = DateTime.Now;

        packet.SetSequenceNumber(this.GetNextSequenceNumber());

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        packet.Serialize(bw);
        var bytes = ms.ToArray();

        if (packet.Channel == ChannelType.UnreliableUnordered)
        {
            this.SendPacketToUnreliablyUnordered(bytes);
        }
        else if (packet.Channel == ChannelType.UnreliableOrdered)
        {
            this.SendPacketToUnreliablyOrdered(bytes);
        }
        else if (packet.Channel == ChannelType.ReliableUnordered)
        {
            this.SendPacketToReliablyUnordered(bytes, packet);
        }
        else if (packet.Channel == ChannelType.ReliableOrdered)
        {
            this.SendPacketToReliablyOrdered(bytes, packet);
        }
    }

    private void SendPacketToUnreliablyUnordered(byte[] data)
    {
        this.Send(data);
    }

    private void SendPacketToUnreliablyOrdered(byte[] data)
    {
        this.Send(data);
    }

    private void SendPacketToReliablyUnordered(byte[] data, ProtocolPacket packet)
    {
        this.RegisterReliablePacketSend(packet);

        this.Send(data);
    }

    private void SendPacketToReliablyOrdered(byte[] data, ProtocolPacket packet)
    {
        this.RegisterReliablePacketSend(packet);

        this.Send(data);
    }

    private void ResendReliablePacket(ProtocolPacket packet)
    {
        this.RegisterResentReliablePacket(packet.SequenceNumber);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        packet.Serialize(bw);
        var bytes = ms.ToArray();

        this.Send(bytes);
    }

    // ACK STUFF

    private void RegisterReliablePacketSend(ProtocolPacket packet)
    {
        this._unackedPackets.Add(packet.SequenceNumber, (DateTime.Now, packet));
    }

    private void RegisterResentReliablePacket(ulong sequenceNumber)
    {
        if (this._unackedPackets.TryGetValue(sequenceNumber, out var packet))
        {
            this._unackedPackets[sequenceNumber] = (DateTime.Now, packet.Item2);
        }
    }

    private void RegisterReliableSequenceNumberAcked(params ulong[] sequenceNumbers)
    {
        foreach (var sequenceNumber in sequenceNumbers)
        {
            if (this._unackedPackets.ContainsKey(sequenceNumber))
            {
                this._unackedPackets.Remove(sequenceNumber);
            }
        }
    }

    private void ResendReliablePacket(ProtocolPacket packet, IPEndPoint endPoint)
    {
        this.RegisterResentReliablePacket(packet.SequenceNumber);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        packet.Serialize(bw);
        var bytes = ms.ToArray();

        this.Send(bytes);
    }

    // PUBLIC API

    public void SendToServer(byte[] data, ChannelType channel)
    {
        var packet = new ApplicationData(data).SetChannelType(channel);
        this.SendPacket(packet);
    }

    public void Disconnect()
    {
        var packet = new ConnectionTermination("Disconnect").SetChannelType(ChannelType.ReliableOrdered);
        this._readyToSend = false;
        this.SendPacket(packet);
        this._socket.Close();

        this._cts.Cancel();
    }

    private static async Task<T> SendAndReceivePacketAsync<T>(ISocketContext context, IPEndPoint endPoint, ProtocolPacket packet, int timeout = 2000) where T : ISerializable
    {
        try
        {
            var tokenSource = new CancellationTokenSource(timeout);
            var token = tokenSource.Token;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            packet.Serialize(bw);
            var bytes = ms.ToArray();

            context.Connect(endPoint);
            context.SendTo(bytes, endPoint);
            var result = await context.ReceiveAsClient(token);

            using var ms2 = new MemoryStream(result.Data);
            using var br = new BinaryReader(ms2);

            var responsePacket = ProtocolPacket.Deserialize(br);
            ServerInfoResponse response = (ServerInfoResponse)responsePacket;

            var data = response.Info;

            using var ms3 = new MemoryStream(data);
            using var br2 = new BinaryReader(ms3);

            return (T)T.Deserialize(br2);
        }
        catch (Exception ex)
        {
            return default!;
        }
    }

    public static async Task<T> RequestServerInfoAsync<T>(string host, int port, int timeout = 2000) where T : ISerializable
    {
        return await RequestServerInfoAsync<T>(new UDPSocketContext(), host, port, timeout);
    }

    public static async Task<T> RequestServerInfoAsync<T>(ISocketContext context, string host, int port, int timeout = 2000) where T : ISerializable
    {
        var serverInfoRequest = new ServerInfoRequest();
        var endPoint = new IPEndPoint(IPAddress.Parse(host), port);
        return await SendAndReceivePacketAsync<T>(context, endPoint, serverInfoRequest, timeout);
    }
}