using Arachne.Packets;

namespace Arachne;

internal class ConnectionTermination : ProtocolPacket
{
    public string Reason { get; set; }

    public ConnectionTermination(string reason) : base(ProtocolPacketType.ConnectionTermination)
    {
        this.Reason = reason;
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        this.Reason = reader.ReadString();
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write(this.Reason);
    }
}