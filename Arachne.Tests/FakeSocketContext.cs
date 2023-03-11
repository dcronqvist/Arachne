using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Arachne.Tests;

internal class FakeNetwork
{
    // data, from, to
    internal ThreadSafe<Queue<(byte[], IPEndPoint, IPEndPoint)>> inNetwork = new(new());

    internal float _lossRate;
    private int _delay;

    Random rng;

    public FakeNetwork(float packetLoss, int latency)
    {
        _lossRate = packetLoss;
        _delay = latency;
        rng = new();
    }

    internal void Send(byte[] data, IPEndPoint from, IPEndPoint to)
    {
        if (rng.NextDouble() < (_lossRate + 0.0001f))
        {
            // Loss rate is halved every time a packet is lost to prevent tests from being impossible...
            _lossRate /= 2;
            return; // Packet loss
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(this._delay);

            this.inNetwork.LockedAction(q =>
            {
                q!.Enqueue((data, from, to));
            });
        });
    }

    internal async Task<(byte[], IPEndPoint)> ReceiveAsync(IPEndPoint receiver, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var x = this.inNetwork.LockedAction<(byte[], IPEndPoint, IPEndPoint)?>(q =>
            {
                if (q!.Count > 0)
                {
                    var peek = q.Peek();
                    if (peek.Item3.Equals(receiver))
                    {
                        q.Dequeue();
                        return peek;
                    }
                }

                return null;
            });

            if (x is not null)
            {
                var data = x.Value.Item1;
                var from = x.Value.Item2;

                return (data, from);
            }

            await Task.Delay(1);
        }

        throw new OperationCanceledException();
    }
}

internal class FakeSocketContext : ISocketContext
{
    private FakeNetwork _network;
    private IPEndPoint? _connectedTo;
    private IPEndPoint? _local;

    public int BoundPort => 0;

    public FakeSocketContext(FakeNetwork network)
    {
        this._network = network;
    }

    public void Bind(IPEndPoint endPoint)
    {
        // Do nothing
        this._local = endPoint;
    }

    public void Close()
    {

    }

    public void Connect(IPEndPoint remote)
    {
        // Get random port
        this._local = new IPEndPoint(IPAddress.Any, 0);
        this._connectedTo = remote;
    }

    public async Task<ReceiveResult> ReceiveAsClient(CancellationToken token)
    {
        var x = await this._network.ReceiveAsync(this._local, token);
        return new ReceiveResult(x.Item1, x.Item2);
    }

    public async Task<ReceiveResult> ReceiveAsync(CancellationToken token)
    {
        var x = await this._network.ReceiveAsync(this._local, token);
        return new ReceiveResult(x.Item1, x.Item2);
    }

    public void SendAsClient(byte[] data)
    {
        this._network.Send(data, this._local!, this._connectedTo!);
    }

    public void SendTo(byte[] data, IPEndPoint remoteEP)
    {
        this._network.Send(data, this._local!, (IPEndPoint)remoteEP);
    }
}