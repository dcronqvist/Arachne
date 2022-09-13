using Arachne.Packets;

namespace Arachne;

internal class ApplicationData : ProtocolPacket
{
    public byte[] Data { get; private set; }

    public ApplicationData(byte[] data) : base(ProtocolPacketType.ApplicationData)
    {
        this.Data = data;
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        this.Data = reader.ReadBytes(length);
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write(this.Data.Length);
        writer.Write(this.Data);
    }
}