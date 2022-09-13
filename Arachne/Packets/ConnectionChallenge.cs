namespace Arachne.Packets;

internal class ConnectionChallenge : ProtocolPacket
{
    public byte[] Challenge { get; set; }

    public ConnectionChallenge(byte[] challenge) : base(ProtocolPacketType.ConnectionChallenge)
    {
        this.Challenge = challenge;
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        this.Challenge = reader.ReadBytes(length);
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write(this.Challenge.Length);
        writer.Write(this.Challenge);
    }
}