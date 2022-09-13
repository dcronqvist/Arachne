namespace Arachne.Packets;

internal enum ProtocolPacketType : byte
{
    ConnectionRequest = 0,
    ConnectionChallenge = 1,
    ConnectionChallengeResponse = 2,
    ConnectionResponse = 3,
    ConnectionKeepAlive = 4,
    ApplicationData = 5,
    ConnectionTermination = 6,
    ConnectionTerminationAck = 7
}

public enum ChannelType : byte
{
    ReliableOrdered = 0,
    ReliableUnordered = 1,
    UnreliableOrdered = 2,
    UnreliableUnordered = 3
}

internal abstract class ProtocolPacket
{
    public ProtocolPacketType PacketType { get; set; }
    public ChannelType Channel { get; set; }
    public ulong SequenceNumber { get; set; }
    public ulong SequenceAck { get; set; }
    public uint AckBits { get; set; } // All sequence numbers up to 32 before SequenceAck are acknowledged

    public ProtocolPacket(ProtocolPacketType packetType)
    {
        this.PacketType = packetType;
    }

    public ulong[] GetAckedSequenceNumbers()
    {
        var acked = new List<ulong>();
        for (int i = 0; i < 32; i++)
        {
            if ((this.AckBits & (1 << i)) != 0)
            {
                acked.Add(this.SequenceAck - (uint)(i + 1));
            }
        }

        return acked.ToArray();
    }

    public ProtocolPacket SetChannel(ChannelType channel)
    {
        this.Channel = channel;
        return this;
    }

    internal ProtocolPacket SetSequenceNumber(ulong sequenceNumber)
    {
        this.SequenceNumber = sequenceNumber;
        return this;
    }

    internal ProtocolPacket SetLatestSequenceAck(ulong sequenceAck)
    {
        this.SequenceAck = sequenceAck;
        return this;
    }

    internal ProtocolPacket SetAckBits(uint ackBits)
    {
        this.AckBits = ackBits;
        return this;
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write((byte)this.PacketType);
        writer.Write((byte)this.Channel);
        writer.Write(this.SequenceNumber);
        writer.Write(this.SequenceAck);
        writer.Write(this.AckBits);
        this.SerializeProtocolPacket(writer);
    }

    public static ProtocolPacket Deserialize(BinaryReader reader)
    {
        ProtocolPacketType ppt = (ProtocolPacketType)reader.ReadByte();
        ChannelType ppc = (ChannelType)reader.ReadByte();
        ulong seq = reader.ReadUInt64();
        ulong seqAck = reader.ReadUInt64();
        uint ackBits = reader.ReadUInt32();
        var packet = CreatePacketOfType(ppt);
        packet.SetChannel(ppc);
        packet.SetSequenceNumber(seq);
        packet.SetLatestSequenceAck(seqAck);
        packet.SetAckBits(ackBits);
        packet.DeserializeProtocolPacket(reader);
        return packet;
    }

    private static ProtocolPacket CreatePacketOfType(ProtocolPacketType ppt)
    {
        switch (ppt)
        {
            case ProtocolPacketType.ConnectionRequest:
                return new ConnectionRequest(0, 0, 0);
            case ProtocolPacketType.ConnectionChallenge:
                return new ConnectionChallenge(new byte[0]);
            case ProtocolPacketType.ConnectionChallengeResponse:
                return new ConnectionChallengeResponse(new byte[0]);
            case ProtocolPacketType.ConnectionResponse:
                return new ConnectionResponse(0, null);
            // case ProtocolPacketType.ConnectionKeepAlive:
            //     return new ConnectionKeepAlive();
            case ProtocolPacketType.ApplicationData:
                return new ApplicationData(new byte[0]);
            case ProtocolPacketType.ConnectionTermination:
                return new ConnectionTermination("");
            case ProtocolPacketType.ConnectionTerminationAck:
                return new ConnectionTerminationAck();
                // default:
                //     throw new Exception("Invalid packet type");
        }

        throw new Exception("Invalid packet type");
    }

    public abstract void SerializeProtocolPacket(BinaryWriter writer);
    public abstract void DeserializeProtocolPacket(BinaryReader reader);
}