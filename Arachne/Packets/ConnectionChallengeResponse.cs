namespace Arachne.Packets;

internal class ConnectionChallengeResponse : ProtocolPacket
{
    public byte[] Response { get; set; }

    public ConnectionChallengeResponse(byte[] response) : base(ProtocolPacketType.ConnectionChallengeResponse)
    {
        this.Response = response;
    }

    public override void DeserializeProtocolPacket(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        this.Response = reader.ReadBytes(length);
    }

    public override void SerializeProtocolPacket(BinaryWriter writer)
    {
        writer.Write(this.Response.Length);
        writer.Write(this.Response);
    }
}