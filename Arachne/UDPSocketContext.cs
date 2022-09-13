using System.Net;
using System.Net.Sockets;

namespace Arachne;

internal class UDPSocketContext : ISocketContext
{
    private Socket _socket;
    public int BoundPort => this._socket == null ? 0 : ((IPEndPoint)this._socket.LocalEndPoint!).Port;

    public UDPSocketContext()
    {
        this._socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
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

        return new ReceiveResult(data, (IPEndPoint)x.RemoteEndPoint);
    }

    public async Task<ReceiveResult> ReceiveAsClient(CancellationToken token)
    {
        var buffer = new byte[2048];

        var segment = new ArraySegment<byte>(buffer);
        var received = await this._socket.ReceiveAsync(segment, SocketFlags.None, token);

        if (received == 0)
        {
            return new ReceiveResult(Array.Empty<byte>(), null);
        }

        var data = new byte[received];
        Array.Copy(buffer, data, received);

        return new ReceiveResult(data, (IPEndPoint)this._socket.RemoteEndPoint!);
    }

    public void SendTo(byte[] data, IPEndPoint remoteEP)
    {
        var length = data.Length;
        this._socket.SendTo(data, 0, length, SocketFlags.None, remoteEP);
    }

    public void SendAsClient(byte[] data)
    {
        var length = data.Length;
        this._socket.Send(data, 0, length, SocketFlags.None);
    }
}