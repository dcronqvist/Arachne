namespace Arachne.Packets;

internal class ConnectionResponse : ProtocolPacket
{
    public Constant Code { get; set; }

    public ConnectionResponse(Constant code) : base(ProtocolPacketType.ConnectionResponse)
    {
        this.Code = code;
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        this.Code = (Constant)reader.ReadUInt32();
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write((uint)this.Code);
    }

    public bool IsSuccess()
    {
        return this.Code == Constant.SUCCESS;
    }
}