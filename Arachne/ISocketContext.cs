using System.Net;

namespace Arachne;

public struct ReceiveResult
{
    public byte[] Data { get; private set; }
    public int Length { get; private set; }
    public IPEndPoint Sender { get; private set; }

    public ReceiveResult(byte[] data, IPEndPoint sender)
    {
        Data = data;
        Length = data.Length;
        Sender = sender;
    }
}

public interface ISocketContext
{
    int BoundPort { get; }
    void Close();

    void Bind(IPEndPoint endPoint);
    void SendTo(byte[] data, IPEndPoint remoteEP);

    void Connect(IPEndPoint remote);
    void SendAsClient(byte[] data);
    Task<ReceiveResult> ReceiveAsync(CancellationToken token);
    Task<ReceiveResult> ReceiveAsClient(CancellationToken token);

    uint GetSentBytesPerSecond();
    uint GetReceivedBytesPerSecond();
}