using System.Net;
using System.Net.Sockets;

namespace Arachne;

internal class MovingAverage
{
    public TimeSpan TimeToSave { get; }
    private Queue<(DateTime, uint)> _values;

    internal MovingAverage(TimeSpan timeToSave)
    {
        this.TimeToSave = timeToSave;
        this._values = new();
    }

    internal void Add(uint value)
    {
        var now = DateTime.Now;

        while (this._values.Count > 0 && DateTime.Now - this._values.Peek().Item1 > this.TimeToSave)
            this._values.Dequeue();
        this._values.Enqueue((now, value));
    }

    internal uint GetAverage()
    {
        if (this._values.Count == 0)
        {
            return 0;
        }

        var sum = 0u;
        foreach (var (time, value) in this._values)
        {
            sum += value;
        }

        return (uint)(sum / this._values.Count);
    }
}

internal class UDPSocketContext : ISocketContext
{
    private Socket _socket;
    public int BoundPort => this._socket == null ? 0 : ((IPEndPoint)this._socket.LocalEndPoint!).Port;
    private ThreadSafe<MovingAverage> _sentBytesPerSecond;
    private ThreadSafe<MovingAverage> _receivedBytesPerSecond;

    public UDPSocketContext(int movingAverageLength)
    {
        this._socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        this._sentBytesPerSecond = new(new MovingAverage(TimeSpan.FromSeconds(1)));
        this._receivedBytesPerSecond = new(new MovingAverage(TimeSpan.FromSeconds(1)));
    }

    public void Bind(IPEndPoint endPoint)
    {
        this._socket.Bind(endPoint);
    }

    public void Connect(IPEndPoint remote)
    {
        this._socket.Connect(remote);
    }

    public void Close()
    {
        this._socket.Close();
    }

    public async Task<ReceiveResult> ReceiveAsync(CancellationToken token)
    {
        var buffer = new byte[2048];
        EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        var x = await this._socket.ReceiveFromAsync(buffer, SocketFlags.None, sender, token);
        var data = new byte[x.ReceivedBytes];
        Array.Copy(buffer, data, x.ReceivedBytes);

        this._receivedBytesPerSecond.LockedAction(q => q!.Add((uint)x.ReceivedBytes));
        return new ReceiveResult(data, (IPEndPoint)x.RemoteEndPoint);
    }

    public async Task<ReceiveResult> ReceiveAsClient(CancellationToken token)
    {
        var buffer = new byte[2048];

        var segment = new ArraySegment<byte>(buffer);
        var received = await this._socket.ReceiveAsync(segment, SocketFlags.None, token);

        if (received == 0)
        {
            return new ReceiveResult(Array.Empty<byte>(), null!);
        }

        var data = new byte[received];
        Array.Copy(buffer, data, received);

        this._receivedBytesPerSecond.LockedAction(q => q!.Add((uint)received));
        return new ReceiveResult(data, (IPEndPoint)this._socket.RemoteEndPoint!);
    }

    public void SendTo(byte[] data, IPEndPoint remoteEP)
    {
        var length = data.Length;
        this._sentBytesPerSecond.LockedAction(q => q!.Add((uint)length));
        this._socket.SendTo(data, 0, length, SocketFlags.None, remoteEP);
    }

    public void SendAsClient(byte[] data)
    {
        var length = data.Length;
        this._sentBytesPerSecond.LockedAction(q => q!.Add((uint)length));
        this._socket.Send(data, 0, length, SocketFlags.None);
    }

    public uint GetSentBytesPerSecond()
    {
        return this._sentBytesPerSecond.LockedAction(q => q!.GetAverage());
    }

    public uint GetReceivedBytesPerSecond()
    {
        return this._receivedBytesPerSecond.LockedAction(q => q!.GetAverage());
    }
}