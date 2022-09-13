namespace Arachne.Packets;

internal class ConnectionResponse : ProtocolPacket
{
    public byte Success { get; set; }
    public string? Reason { get; set; }

    public ConnectionResponse(byte success, string? reason) : base(ProtocolPacketType.ConnectionResponse)
    {
        this.Success = success;
        this.Reason = reason;
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        this.Success = reader.ReadByte();
        if (this.Success == 0)
        {
            this.Reason = reader.ReadString();
        }
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write(this.Success);
        if (this.Success == 0)
        {
            writer.Write(this.Reason!);
        }
    }

    public bool IsSuccess()
    {
        return this.Success == 1;
    }
}