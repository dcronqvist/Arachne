namespace Arachne.Packets;

internal class ServerInfoResponse : ProtocolPacket
{
    public byte[] Info { get; set; }

    public ServerInfoResponse(byte[] info) : base(ProtocolPacketType.ServerInfoResponse)
    {
        this.Info = info;
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        var len = reader.ReadInt32();
        this.Info = reader.ReadBytes(len);
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write(this.Info.Length);
        writer.Write(this.Info);
    }
}