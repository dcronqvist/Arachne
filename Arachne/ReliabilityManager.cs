using Arachne.Packets;

namespace Arachne;

internal class ReliabilityManager
{
    private List<(DateTime, ProtocolPacket)> _sentPacketsAwaitingAck = new();
    private PriorityQueue<ProtocolPacket, ulong> _receivedPacketsAwaitingAck = new();

    public ReliabilityManager() { }

    // SENDING PACKETS AND RECEIVING ACKS FOR THEM

    public void AddSentPacket(ProtocolPacket packet)
    {
        this._sentPacketsAwaitingAck.Add((DateTime.Now, packet));
    }

    public void AddReceivedPacket(ProtocolPacket packet)
    {
        var receivedAcks = packet.AckSequenceNumbers;

        foreach (var receivedAck in receivedAcks)
        {
            this._sentPacketsAwaitingAck.RemoveAll(x => x.Item2.SequenceNumber == receivedAck);
        }

        if (packet.Channel.IsReliable())
        {
            this._receivedPacketsAwaitingAck.Enqueue(packet, packet.SequenceNumber);

            while (this._receivedPacketsAwaitingAck.Count > 32)
            {
                this._receivedPacketsAwaitingAck.Dequeue(); // Only keep the last 32 packets
            }
        }
    }

    public List<ProtocolPacket> GetSentPacketsOlderThan(TimeSpan timeSpan)
    {
        var packets = new List<ProtocolPacket>();
        foreach (var (time, packet) in this._sentPacketsAwaitingAck)
        {
            if (DateTime.Now - time > timeSpan)
            {
                packets.Add(packet);
            }
        }

        return packets;
    }

    public void UpdateSentTimeForPacket(ProtocolPacket packet)
    {
        var index = this._sentPacketsAwaitingAck.FindIndex(x => x.Item2.SequenceNumber == packet.SequenceNumber);
        if (index != -1)
        {
            this._sentPacketsAwaitingAck[index] = (DateTime.Now, packet);
        }
    }

    public ulong[] GetNextAcksToSend()
    {
        var acks = new List<ulong>();
        foreach (var (packet, seq) in this._receivedPacketsAwaitingAck.UnorderedItems.OrderBy(x => x.Item2))
        {
            acks.Add(packet.SequenceNumber);
        }

        return acks.ToArray();
    }
}