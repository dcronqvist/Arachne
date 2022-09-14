using System.Net;
using System.Threading.Tasks.Dataflow;
using Arachne.Packets;

namespace Arachne;

public class ReceivedDataServerEventArgs : EventArgs
{
    public byte[] Data { get; private set; }
    public RemoteConnection From { get; private set; }

    public ReceivedDataServerEventArgs(byte[] data, RemoteConnection from)
    {
        this.Data = data;
        this.From = from;
    }
}

public class ConnectionEventArgs : EventArgs
{
    public RemoteConnection Connection { get; private set; }

    public ConnectionEventArgs(RemoteConnection connection)
    {
        Connection = connection;
    }
}

public sealed class Server
{
    private ISocketContext _listener;
    private IPEndPoint _listenEndPoint;
    internal uint _protocolID;

    private int _maxConnections;

    private CancellationTokenSource _cts;

    private BufferBlock<(byte[], IPEndPoint)> _sendQueue;
    private Task? _sendTask;
    private bool _readyToSend = false;
    private Task? _receiveTask;
    private bool _readyToReceive = false;

    internal IAuthenticator? _authenticator;
    private ThreadSafe<Dictionary<IPEndPoint, RemoteConnection>> _connections;

    // EVENTS
    public event EventHandler<ReceivedDataServerEventArgs>? ReceivedData;
    public event EventHandler<ConnectionEventArgs>? ConnectionRequested;
    public event EventHandler<ConnectionEventArgs>? ConnectionFailedAuth;
    public event EventHandler<ConnectionEventArgs>? ConnectionEstablished;
    public event EventHandler<ConnectionEventArgs>? ConnectionTerminated;
    public event EventHandler<ConnectionEventArgs>? ClientDisconnected;

    public Server(int maxConns, string address, int port, uint protocolID, IAuthenticator authenticator) : this(maxConns, address, port, protocolID, authenticator, new UDPSocketContext())
    { }

    public Server(int maxConns, string address, int port, uint protocolID, IAuthenticator authenticator, ISocketContext context)
    {
        this._maxConnections = maxConns;
        this._listenEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
        this._protocolID = protocolID;
        this._listener = context;
        this._cts = new CancellationTokenSource();
        this._sendQueue = new BufferBlock<(byte[], IPEndPoint)>();
        this._authenticator = authenticator;
        this._connections = new(new());
    }

    internal void TriggerConnRequestedEvent(RemoteConnection rc)
    {
        ConnectionRequested?.Invoke(this, new ConnectionEventArgs(rc));
    }

    internal void TriggerConnFailedAuthEvent(RemoteConnection rc)
    {
        ConnectionFailedAuth?.Invoke(this, new ConnectionEventArgs(rc));
    }

    internal void TriggerConnEstablishedEvent(RemoteConnection rc)
    {
        ConnectionEstablished?.Invoke(this, new ConnectionEventArgs(rc));
    }

    internal void TriggerClientDisconnectEvent(RemoteConnection rc)
    {
        this.ClientDisconnected?.Invoke(this, new ConnectionEventArgs(rc));
    }

    public async Task StartAsync()
    {
        this._listener.Bind(this._listenEndPoint);
        var token = this._cts.Token;

        this._receiveTask = Task.Run(() => this.ReceiveLoopAsync(token));
        this._sendTask = Task.Run(() => this.SendLoopAsync(token));
        _ = Task.Run(() => this.ReliabilityLoopAsync(token));
        _ = Task.Run(() => this.CheckTimedOutClientsLoopAsync(token));

        while (!this._readyToSend || !this._readyToReceive)
        {
            await Task.Delay(100);
        }
    }

    private async Task CheckTimedOutClientsLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(1000);
            var connsTimedout = new List<RemoteConnection>();

            this._connections.LockedAction(c =>
            {
                foreach (var conn in c!.Values)
                {
                    if (conn.HasTimedOut(TimeSpan.FromSeconds(10)))
                    {
                        connsTimedout.Add(conn);
                    }
                }
            });

            foreach (var conn in connsTimedout)
            {
                this.DisconnectClient(conn);
            }
        }
    }

    public async Task StopAsync()
    {
        this._cts.Cancel();

        while (this._readyToReceive || this._readyToSend)
        {
            await Task.Delay(100);
        }

        this._listener.Close();
    }

    private async Task ReliabilityLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var timeBeforeResend = TimeSpan.FromMilliseconds(500);

            var toResend = new List<(ProtocolPacket, IPEndPoint)>();

            var connections = this._connections.LockedAction(c => c is null ? new List<RemoteConnection>() : c.Values.ToList())!;

            foreach (var connection in connections)
            {
                var unacked = connection.GetUnackedPacketsOlderThan(timeBeforeResend);
                foreach (var (p, d) in unacked)
                {
                    this.ResendReliablePacket(p, d);
                }
            }

            await Task.Delay(100);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                this._readyToReceive = true;
                var result = await this._listener.ReceiveAsync(token);
                _ = Task.Run(() =>
                {
                    this.OnReceive(result.Data, result.Sender);
                });
            }
        }
        catch (Exception)
        {
            // stop
            this._readyToReceive = false;
            return;
        }
    }

    internal ulong GetClientID(IPEndPoint endpoint)
    {
        return this._connections.LockedAction(c => c[endpoint].ClientID);
    }

    private void SendTo(byte[] data, IPEndPoint endPoint)
    {
        this._sendQueue.Post((data, endPoint));
    }

    private async Task SendLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                this._readyToSend = true;
                var (data, endPoint) = await this._sendQueue.ReceiveAsync(token);
                this._listener.SendTo(data, endPoint);
            }
        }
        catch (Exception)
        {
            // stop
            this._readyToSend = false;
            return;
        }
    }

    protected void OnReceive(byte[] data, IPEndPoint sender)
    {
        // Handle received data
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        var packet = ProtocolPacket.Deserialize(reader);
        _ = this.ProcessPacket(packet, sender);
    }

    private async Task ProcessPacket(ProtocolPacket packet, IPEndPoint sender)
    {
        var connection = this.GetConnectionForEndPoint(sender);

        if (packet.Channel == ChannelType.ReliableOrdered || packet.Channel == ChannelType.UnreliableOrdered)
        {
            if (connection.FollowsOrder(packet.SequenceNumber))
            {
                connection.SetLastReceivedSequenceNumber(packet.SequenceNumber);
            }
            else
            {
                return; // Packet is out of order, ignore it
            }
        }

        if (packet.Channel == ChannelType.ReliableUnordered || packet.Channel == ChannelType.ReliableOrdered)
        {
            connection.RegisterReliableSequenceNumberAcked(packet.GetAckedSequenceNumbers());
        }

        if (packet.PacketType == ProtocolPacketType.ApplicationData)
        {
            var appData = (ApplicationData)packet;
            this.ReceivedData?.Invoke(this, new ReceivedDataServerEventArgs(appData.Data, connection));
        }
        else
        {
            await connection.ReceiveProtocolPacket(packet);
        }
    }

    private RemoteConnection GetConnectionForEndPoint(IPEndPoint endPoint)
    {
        var connection = this._connections.LockedAction(c => c!.TryGetValue(endPoint, out var conn) ? conn : null);

        if (connection is null)
        {
            connection = new RemoteConnection(this, endPoint);
            this._connections.LockedAction(c => c!.Add(endPoint, connection));
        }

        return connection;
    }

    // SENDING METHODS

    internal void SendPacketTo(ProtocolPacket packet, IPEndPoint endPoint)
    {
        var connection = this.GetConnectionForEndPoint(endPoint);
        packet.SetSequenceNumber(connection.GetNextSequenceNumber());

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        packet.Serialize(bw);
        var bytes = ms.ToArray();

        if (packet.Channel == ChannelType.UnreliableUnordered)
        {
            this.SendPacketToUnreliablyUnordered(bytes, endPoint);
        }
        else if (packet.Channel == ChannelType.UnreliableOrdered)
        {
            this.SendPacketToUnreliablyOrdered(bytes, endPoint);
        }
        else if (packet.Channel == ChannelType.ReliableUnordered)
        {
            this.SendPacketToReliablyUnordered(bytes, endPoint, packet);
        }
        else if (packet.Channel == ChannelType.ReliableOrdered)
        {
            this.SendPacketToReliablyOrdered(bytes, endPoint, packet);
        }
    }

    private void SendPacketToUnreliablyUnordered(byte[] data, IPEndPoint endPoint)
    {
        this.SendTo(data, endPoint);
    }

    private void SendPacketToUnreliablyOrdered(byte[] data, IPEndPoint endPoint)
    {
        this.SendTo(data, endPoint);
    }

    private void SendPacketToReliablyUnordered(byte[] data, IPEndPoint endPoint, ProtocolPacket packet)
    {
        var connection = this.GetConnectionForEndPoint(endPoint);
        connection.RegisterReliablePacketSend(packet);

        this.SendTo(data, endPoint);
    }

    private void SendPacketToReliablyOrdered(byte[] data, IPEndPoint endPoint, ProtocolPacket packet)
    {
        var connection = this.GetConnectionForEndPoint(endPoint);
        connection.RegisterReliablePacketSend(packet);

        this.SendTo(data, endPoint);
    }

    private void ResendReliablePacket(ProtocolPacket packet, IPEndPoint endPoint)
    {
        var connection = this.GetConnectionForEndPoint(endPoint);
        connection.RegisterResentReliablePacket(packet.SequenceNumber);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        packet.Serialize(bw);
        var bytes = ms.ToArray();

        this.SendTo(bytes, endPoint);
    }

    internal void RemoveConnection(RemoteConnection conn)
    {
        this._connections.LockedAction(c => c!.Remove(conn.RemoteEndPoint));
    }

    // PUBLIC API

    public void DisconnectClient(RemoteConnection conn)
    {
        conn.MoveNext(ConnectionTransition.CTSent);
        var termination = new ConnectionTermination("Server terminated connection").SetChannel(ChannelType.ReliableOrdered);
        this.SendPacketTo(termination, conn.RemoteEndPoint);

        this.RemoveConnection(conn);
        this.ConnectionTerminated?.Invoke(this, new ConnectionEventArgs(conn));
    }

    public RemoteConnection[] GetAllClients()
    {
        return this._connections.LockedAction(c => c!.Values!.ToArray())!;
    }

    public RemoteConnection GetClientConnection(ulong clientID)
    {
        return this._connections.LockedAction(c => c!.Values!.FirstOrDefault(conn => conn.ClientID == clientID))!;
    }

    public void SendToClient(byte[] data, RemoteConnection connection, ChannelType channel)
    {
        var packet = new ApplicationData(data).SetChannel(channel);
        this.SendPacketTo(packet, connection.RemoteEndPoint);
    }
}