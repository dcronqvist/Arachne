using Arachne.Packets;

namespace Arachne;

internal class ConnectionTerminationAck : ProtocolPacket
{
    public ConnectionTerminationAck() : base(ProtocolPacketType.ConnectionTerminationAck)
    {
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {

    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {

    }
}