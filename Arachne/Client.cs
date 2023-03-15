using System.Diagnostics;
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
    private string _host;
    private int _port;

    private BufferBlock<byte[]> _sendQueue;
    private Task? _sendTask;
    private bool _readyToSend = false;
    private Task? _receiveTask;
    private bool _readyToReceive = false;
    private CancellationTokenSource _cts;
    private IAuthenticator _authenticator;
    private DeliveryService<ProtocolPacket> _deliveryService = new();
    private ThreadSafe<MovingAverage> _pingAverage = new(new(TimeSpan.FromSeconds(1)));
    private ThreadSafe<Dictionary<ulong, DateTime>> _pingTimes = new(new());

    // Connection stuff
    private ulong _nextSequenceNumber = 1;

    // Reliability stuff
    private ThreadSafe<ReliabilityManager> _reliabilityManager = new(new());

    public event EventHandler<ReceivedDataClientEventArgs>? ReceivedData;
    public event EventHandler? DisconnectedByServer;
    public event EventHandler<ulong>? ServerAckedPacket;
    public event EventHandler<ulong>? ResentPacket;

    public Client(uint protocolID, IAuthenticator authenticator) : this(protocolID, authenticator, new UDPSocketContext(5))
    { }

    public Client(uint protocolID, IAuthenticator auth, ISocketContext context)
    {
        this._protocolID = protocolID;
        this._socket = context;
        this._cts = new CancellationTokenSource();
        this._sendQueue = new BufferBlock<byte[]>();
        this._authenticator = auth;

        this._reliabilityManager.LockedAction(rm =>
        {
            rm!.SequenceNumberAcked += (s, e) =>
            {
                this.ServerAckedPacket?.Invoke(this, e);
            };
        });
    }

    public ISocketContext GetSocketContext()
    {
        return this._socket;
    }

    private ulong GetNextSequenceNumber()
    {
        return this._nextSequenceNumber++;
    }

    public async Task<(Constant, ulong)> ConnectAsync(string address, int port, GetChallengeResponse respondToChallenge, int timeout = 5000)
    {
        this._lastPacketSent = DateTime.Now;
        var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(timeout);
        var token = tokenSource.Token;
        var loopToken = this._cts.Token;
        this._host = address;
        this._port = port;

        this._socket.Connect(new IPEndPoint(IPAddress.Parse(address), port));

        _ = Task.Run(() => this.ReliabilityLoopAsync(loopToken));
        this._receiveTask = Task.Run(() => this.ReceiveLoopAsync(loopToken));

        // Send connect request
        var connectRequest = new ConnectionRequest(this._protocolID, 0).SetChannelType(ChannelType.Reliable);
        this.SendPacket(connectRequest);

        var response = await this.ReceivePacketAsync<ProtocolPacket>(timeout);

        if (response is null)
        {
            return (Constant.NO_RESPONSE, 0);
        }

        ConnectionResponse? connectResponse = null;

        if (response.PacketType == ProtocolPacketType.ConnectionChallenge)
        {
            var challenge = (ConnectionChallenge)response;
            var challResp = await respondToChallenge(challenge.Challenge);
            var challRespPacket = new ConnectionChallengeResponse(challResp).SetChannelType(ChannelType.Reliable);
            this.SendPacket(challRespPacket);
            connectResponse = await this.ReceivePacketAsync<ConnectionResponse>(timeout);
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

        this._sendTask = Task.Run(() => this.SendLoopAsync(loopToken));
        _ = Task.Run(() => this.KeepAliveLoopAsync(loopToken));
        _ = Task.Run(() => this.PingLoopAsync(loopToken));

        while (!this._readyToSend || !this._readyToReceive)
        {
            await Task.Delay(100);
        }

        return (Constant.SUCCESS, connectResponse.ClientID);
    }

    private async Task PingLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var seq = this.SendPacket(new Ping());
            this._pingTimes.LockedAction(pt => pt!.Add(seq, now));

            await Task.Delay(300);
        }
    }

    private async Task ReliabilityLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var resendTime = TimeSpan.FromMilliseconds(1000);
            this._reliabilityManager.LockedAction(rm =>
            {
                var packsToResend = rm!.GetSentPacketsOlderThan(resendTime);

                foreach (var p in packsToResend)
                {
                    this.SendPacketRaw(p);
                    rm.UpdateSentTimeForPacket(p);
                    this.ResentPacket?.Invoke(this, p.SequenceNumber);
                }

            });

            await Task.Delay(50);
        }
    }

    private async Task KeepAliveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.Now;
            if (now - this._lastPacketSent > TimeSpan.FromMilliseconds(500))
            {
                this.SendPacket(new ConnectionKeepAlive().SetChannelType(ChannelType.Unreliable));
            }
            await Task.Delay(50);
        }
    }

    // private async Task<ReceiveResult> ReceiveAsync(CancellationToken token)
    // {
    //     return await this._socket.ReceiveAsClient(token);
    // }

    private async Task<T?> ReceivePacketAsync<T>(int timeout) where T : ProtocolPacket
    {
        var packet = await this._deliveryService.AwaitDeliveryAsync(timeout);

        if (packet is null) return null;

        return (T)packet;
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

    private void ProcessPacket(ProtocolPacket packet)
    {
        this.ReceivePacket(packet);

        if (this._deliveryService.TryDeliverToWaiter(packet))
        {
            return;
        }

        if (packet.PacketType == ProtocolPacketType.ApplicationData)
        {
            var appData = packet as ApplicationData;
            this.ReceivedData?.Invoke(this, new ReceivedDataClientEventArgs(appData!.Data, this._connectedTo!));
        }
    }

    private void ReceivePacket(ProtocolPacket packet)
    {
        if (packet.PacketType == ProtocolPacketType.ConnectionTermination)
        {
            // Server wants to terminate the connection. Acknowledge
            var ack = new ConnectionTerminationAck().SetChannelType(ChannelType.Reliable);
            this.SendPacket(ack);
            // TODO: Close client.
            this.DisconnectedByServer?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (packet.PacketType == ProtocolPacketType.Pong)
        {
            var seqPong = (packet as Pong)!.PongSeq;
            var ping = this._pingTimes.LockedAction(pt =>
            {
                if (pt!.TryGetValue(seqPong, out var sentTime))
                {
                    var now = DateTime.Now;
                    return (now - sentTime).Milliseconds;
                }
                return 0;
            });

            this._pingAverage.LockedAction(pa =>
            {
                pa!.Add((uint)ping);
            });
        }

        this._reliabilityManager.LockedAction(rm =>
        {
            rm!.AddReceivedPacket(packet);
        });
    }

    internal ulong SendPacket(ProtocolPacket packet)
    {
        this._lastPacketSent = DateTime.Now;

        packet.SetSequenceNumber(this.GetNextSequenceNumber());
        packet.SetAckSequenceNumbers(this._reliabilityManager.LockedAction(rm => rm!.GetNextAcksToSend())!);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        packet.Serialize(bw);
        var bytes = ms.ToArray();

        if (packet.Channel.IsReliable())
        {
            // Reliability stuff
            this._reliabilityManager.LockedAction(rm =>
            {
                rm!.AddSentPacket(packet);
            });
        }

        this.Send(bytes);
        return packet.SequenceNumber;
    }

    internal void SendPacketRaw(ProtocolPacket packet)
    {
        this._lastPacketSent = DateTime.Now;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        packet.Serialize(bw);
        var bytes = ms.ToArray();

        if (packet.Channel.IsReliable())
        {
            // Reliability stuff
            this._reliabilityManager.LockedAction(rm =>
            {
                rm!.AddSentPacket(packet);
            });
        }

        this.Send(bytes);
    }

    // PUBLIC API

    public uint GetPing()
    {
        return this._pingAverage.LockedAction(p => p!.GetAverage());
    }

    public ulong SendToServer(byte[] data, ChannelType channel)
    {
        var packet = new ApplicationData(data).SetChannelType(channel);
        return this.SendPacket(packet);
    }

    public async Task<byte[]> ReceiveNextFromServerAsync(int timeout)
    {
        var packet = await this._deliveryService.AwaitDeliveryAsync(timeout);

        if (packet is ApplicationData appData)
        {
            return appData.Data;
        }

        return Array.Empty<byte>();
    }

    public void Disconnect()
    {
        var packet = new ConnectionTermination("Disconnect").SetChannelType(ChannelType.Reliable);
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
        catch (Exception)
        {
            return default!;
        }
    }

    public static async Task<T> RequestServerInfoAsync<T>(string host, int port, int timeout = 2000) where T : ISerializable
    {
        return await RequestServerInfoAsync<T>(new UDPSocketContext(5), host, port, timeout);
    }

    public static async Task<T> RequestServerInfoAsync<T>(ISocketContext context, string host, int port, int timeout = 2000) where T : ISerializable
    {
        var serverInfoRequest = new ServerInfoRequest();
        var endPoint = new IPEndPoint(IPAddress.Parse(host), port);
        return await SendAndReceivePacketAsync<T>(context, endPoint, serverInfoRequest, timeout);
    }
}