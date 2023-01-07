namespace Arachne.Packets;

internal class ServerInfoRequest : ProtocolPacket
{
    public ServerInfoRequest() : base(ProtocolPacketType.ServerInfoRequest)
    {
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {

    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {

    }
}