namespace Arachne.Packets;

internal class ConnectionResponse : ProtocolPacket
{
    public Constant Code { get; set; }
    public ulong ClientID { get; set; }

    public ConnectionResponse(Constant code, ulong clientID) : base(ProtocolPacketType.ConnectionResponse)
    {
        this.Code = code;
        this.ClientID = clientID;
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        this.Code = (Constant)reader.ReadUInt32();
        this.ClientID = reader.ReadUInt64();
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write((uint)this.Code);
        writer.Write(this.ClientID);
    }

    public bool IsSuccess()
    {
        return this.Code == Constant.SUCCESS;
    }
}