namespace Arachne.Packets;

internal enum ProtocolPacketType : byte
{
    ConnectionRequest = 0b0000,
    ConnectionChallenge = 0b0001,
    ConnectionChallengeResponse = 0b0010,
    ConnectionResponse = 0b0011,
    ConnectionKeepAlive = 0b0100,
    ApplicationData = 0b0101,
    ConnectionTermination = 0b0110,
    ConnectionTerminationAck = 0b0111,

    ServerInfoRequest = 0b1000,
    ServerInfoResponse = 0b1001,
}

public enum ChannelType : byte
{
    ReliableOrdered = 0b00 << 4,
    ReliableUnordered = 0b01 << 4,
    UnreliableOrdered = 0b10 << 4,
    UnreliableUnordered = 0b11 << 4
}

internal abstract class ProtocolPacket
{
    public byte PacketTypeAndChannel { get; set; }
    public ProtocolPacketType PacketType => (ProtocolPacketType)(this.PacketTypeAndChannel & 0b1111);
    public ChannelType Channel => (ChannelType)(this.PacketTypeAndChannel & 0b11110000);
    public ulong SequenceNumber { get; set; }
    public ulong SequenceAck { get; set; }
    public uint AckBits { get; set; } // All sequence numbers up to 32 before SequenceAck are acknowledged

    public ProtocolPacket(ProtocolPacketType packetType)
    {
        this.SetPacketType(packetType);
    }

    public ProtocolPacket SetPacketType(ProtocolPacketType packetType)
    {
        this.PacketTypeAndChannel = (byte)((this.PacketTypeAndChannel & 0b11110000) | (byte)packetType);
        return this;
    }

    public ProtocolPacket SetChannelType(ChannelType channelType)
    {
        this.PacketTypeAndChannel = (byte)((this.PacketTypeAndChannel & 0b00001111) | (byte)channelType);
        return this;
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
        writer.Write(this.PacketTypeAndChannel);
        writer.Write(this.SequenceNumber);
        writer.Write(this.SequenceAck);
        writer.Write(this.AckBits);
        this.SerializeProtocolPacket(writer);
    }

    public static ProtocolPacket Deserialize(BinaryReader reader)
    {
        byte packetTypeAndChannel = reader.ReadByte();
        ProtocolPacketType ppt = (ProtocolPacketType)(packetTypeAndChannel & 0b00001111);
        ChannelType ppc = (ChannelType)(packetTypeAndChannel & 0b11110000);
        ulong seq = reader.ReadUInt64();
        ulong seqAck = reader.ReadUInt64();
        uint ackBits = reader.ReadUInt32();
        var packet = CreatePacketOfType(ppt);
        packet.SetChannelType(ppc);
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
                return new ConnectionRequest(0, 0);
            case ProtocolPacketType.ConnectionChallenge:
                return new ConnectionChallenge(new byte[0]);
            case ProtocolPacketType.ConnectionChallengeResponse:
                return new ConnectionChallengeResponse(new byte[0]);
            case ProtocolPacketType.ConnectionResponse:
                return new ConnectionResponse(Constant.SUCCESS, 0);
            case ProtocolPacketType.ConnectionKeepAlive:
                return new ConnectionKeepAlive();
            case ProtocolPacketType.ApplicationData:
                return new ApplicationData(new byte[0]);
            case ProtocolPacketType.ConnectionTermination:
                return new ConnectionTermination("");
            case ProtocolPacketType.ConnectionTerminationAck:
                return new ConnectionTerminationAck();
            case ProtocolPacketType.ServerInfoRequest:
                return new ServerInfoRequest();
            case ProtocolPacketType.ServerInfoResponse:
                return new ServerInfoResponse(new byte[0]);
                // default:
                //     throw new Exception("Invalid packet type");
        }

        throw new Exception("Invalid packet type");
    }

    public abstract void SerializeProtocolPacket(BinaryWriter writer);
    public abstract void DeserializeProtocolPacket(BinaryReader reader);
}