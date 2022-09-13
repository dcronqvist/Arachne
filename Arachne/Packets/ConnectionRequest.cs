namespace Arachne.Packets;

internal class ConnectionRequest : ProtocolPacket
{
    public uint ProtocolID { get; set; }
    public uint ProtocolVersion { get; set; }
    public ulong ClientID { get; set; }

    public ConnectionRequest(uint protocolID, uint protocolVersion, ulong clientID) : base(ProtocolPacketType.ConnectionRequest)
    {
        this.ProtocolID = protocolID;
        this.ProtocolVersion = protocolVersion;
        this.ClientID = clientID;
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        this.ProtocolID = reader.ReadUInt32();
        this.ProtocolVersion = reader.ReadUInt32();
        this.ClientID = reader.ReadUInt64();
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write(this.ProtocolID);
        writer.Write(this.ProtocolVersion);
        writer.Write(this.ClientID);
    }
}