namespace Arachne.Packets;

internal class ConnectionKeepAlive : ProtocolPacket
{
    public ConnectionKeepAlive() : base(ProtocolPacketType.ConnectionKeepAlive)
    {
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
    }
}