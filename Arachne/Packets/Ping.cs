namespace Arachne.Packets;

internal class Ping : ProtocolPacket
{
    public Ping() : base(ProtocolPacketType.Ping)
    {
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {

    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {

    }
}

internal class Pong : ProtocolPacket
{
    public ulong PongSeq { get; set; }

    public Pong() : base(ProtocolPacketType.Pong)
    {
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        this.PongSeq = reader.ReadUInt64();
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write(this.PongSeq);
    }
}