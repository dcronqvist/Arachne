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

    Ping = 0b1010,
    Pong = 0b1011
}

[Flags]
public enum ChannelType : byte
{
    Unreliable = 0b00000000,
    Reliable = 0b00010000
}

public static class ChannelTypeExtensions
{
    public static bool IsReliable(this ChannelType channelType)
    {
        return channelType.HasFlag(ChannelType.Reliable);
    }
}

internal abstract class ProtocolPacket
{
    public byte PacketTypeAndChannel { get; set; }
    public ProtocolPacketType PacketType => (ProtocolPacketType)(this.PacketTypeAndChannel & 0b1111);
    public ChannelType Channel => (ChannelType)(this.PacketTypeAndChannel & 0b11110000);
    public ulong SequenceNumber { get; set; }
    public ulong[] AckSequenceNumbers { get; set; } = new ulong[0];

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

    internal ProtocolPacket SetSequenceNumber(ulong sequenceNumber)
    {
        this.SequenceNumber = sequenceNumber;
        return this;
    }

    internal ProtocolPacket SetAckSequenceNumbers(ulong[] ackSequenceNumbers)
    {
        this.AckSequenceNumbers = ackSequenceNumbers;
        return this;
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(this.PacketTypeAndChannel);
        writer.Write(this.SequenceNumber);

        var firstAckSequenceNumber = this.AckSequenceNumbers.LastOrDefault();
        writer.Write(firstAckSequenceNumber);

        uint ackBits = 0;
        for (uint i = 0; i < 32; i++)
        {
            var ackNum = firstAckSequenceNumber - i - 1;
            if (AckSequenceNumbers.Contains(ackNum))
            {
                ackBits |= 1u << (int)i;
            }
        }
        writer.Write(ackBits);

        // writer.Write(this.AckSequenceNumbers.Length);
        // foreach (var ackSequenceNumber in this.AckSequenceNumbers)
        // {
        //     writer.Write(ackSequenceNumber);
        // }
        this.SerializeProtocolPacket(writer);
    }

    public static ProtocolPacket Deserialize(BinaryReader reader)
    {
        byte packetTypeAndChannel = reader.ReadByte();
        ProtocolPacketType ppt = (ProtocolPacketType)(packetTypeAndChannel & 0b00001111);
        ChannelType ppc = (ChannelType)(packetTypeAndChannel & 0b11110000);
        ulong seq = reader.ReadUInt64();
        var packet = CreatePacketOfType(ppt);
        packet.SetChannelType(ppc);
        packet.SetSequenceNumber(seq);

        ulong firstSeqack = reader.ReadUInt64();
        int ackBits = reader.ReadInt32();

        var listOfAcks = new List<ulong>();

        listOfAcks.Add(firstSeqack);
        for (uint i = 0; i < 32; i++)
        {
            if ((ackBits & (1u << (int)i)) != 0)
            {
                listOfAcks.Add(firstSeqack - i - 1);
            }
        }
        packet.SetAckSequenceNumbers(listOfAcks.ToArray());

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
            case ProtocolPacketType.Ping:
                return new Ping();
            case ProtocolPacketType.Pong:
                return new Pong();
                // default:
                //     throw new Exception("Invalid packet type");
        }

        throw new Exception("Invalid packet type");
    }

    public abstract void SerializeProtocolPacket(BinaryWriter writer);
    public abstract void DeserializeProtocolPacket(BinaryReader reader);
}