namespace Arachne;

public interface IServerInfoProvider
{
    public ISerializable GetServerInfo(Server server);

    public static IServerInfoProvider Default => new DefaultServerInfoProvider();
}

public class DefaultServerInfo : ISerializable
{
    public uint ProtocolID { get; set; }
    public uint[] SupportedProtocols { get; set; }
    public uint ConnectedClients { get; set; }

    public DefaultServerInfo(uint connectedClients, uint protocolID, uint[] supportedProtocols)
    {
        this.ConnectedClients = connectedClients;
        this.ProtocolID = protocolID;
        this.SupportedProtocols = supportedProtocols;
    }

    public static object Deserialize(BinaryReader reader)
    {
        var connected = reader.ReadUInt32();
        var protocolID = reader.ReadUInt32();
        var supportedProtocols = new uint[reader.ReadInt32()];
        for (int i = 0; i < supportedProtocols.Length; i++)
        {
            supportedProtocols[i] = reader.ReadUInt32();
        }

        return new DefaultServerInfo(connected, protocolID, supportedProtocols);
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(this.ConnectedClients);
        writer.Write(this.ProtocolID);
        writer.Write(this.SupportedProtocols.Length);
        foreach (var protocol in this.SupportedProtocols)
        {
            writer.Write(protocol);
        }
    }
}

public class DefaultServerInfoProvider : IServerInfoProvider
{
    public ISerializable GetServerInfo(Server server)
    {
        return new DefaultServerInfo((uint)server.GetAllClients().Length, server._protocolID, server._supportedClientProtocolIDs);
    }
}