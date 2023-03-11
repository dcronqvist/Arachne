using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Arachne.Tests;

public class NeverAuth : IAuthenticator
{
    public Task<bool> AuthenticateAsync(ulong clientID, byte[] challenge, byte[] response)
    {
        return Task.FromResult(false);
    }

    public Task<byte[]> GetChallengeForClientAsync(ulong clientID)
    {
        return Task.FromResult(new byte[0]);
    }
}

public class PasswordAuth : IAuthenticator
{
    public string Password { get; set; }

    public PasswordAuth(string pass)
    {
        this.Password = pass;
    }

    public Task<bool> AuthenticateAsync(ulong clientID, byte[] challenge, byte[] response)
    {
        byte[] stringBytes = System.Text.Encoding.UTF8.GetBytes(this.Password);
        return Task.FromResult(StructuralComparisons.StructuralEqualityComparer.Equals(stringBytes, response));
    }

    public Task<byte[]> GetChallengeForClientAsync(ulong clientID)
    {
        return Task.FromResult(new byte[0]);
    }

    public static Client.GetChallengeResponse Response(string password)
    {
        return (ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes(password));
    }
}

public class ConnectTests
{
    private readonly ITestOutputHelper output;

    public ConnectTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public async Task Test1()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8888, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8888, IAuthenticator.NoAuthResponse);
        await Task.Delay(500);
        Assert.Equal(Constant.SUCCESS, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test2()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8889, 5, new NeverAuth(), socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8889, IAuthenticator.NoAuthResponse);
        await Task.Delay(500);

        Assert.Equal(Constant.FAILURE_INVALID_AUTHENTICATION, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test3()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8890, 5, new PasswordAuth("goodpassword"), socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8890, (ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("goodpassword")));
        await Task.Delay(500);

        Assert.Equal(Constant.SUCCESS, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test4()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8891, 5, new PasswordAuth("goodpassword"), socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8891, (ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("thewrongpassword")));
        await Task.Delay(500);

        Assert.Equal(Constant.FAILURE_INVALID_AUTHENTICATION, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test5()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8892, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(0, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8892, IAuthenticator.NoAuthResponse, timeout: 2000);
        await Task.Delay(500);

        Assert.Equal(Constant.FAILURE_UNSUPPORTED_PROTOCOL_VERSION, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test6()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8893, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        bool clientDisc = false;

        client.DisconnectedByServer += (s, e) => clientDisc = true;

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8893, IAuthenticator.NoAuthResponse, timeout: 2000);
        await Task.Delay(500);

        Assert.Equal(Constant.SUCCESS, code);

        var connection = server.GetClientConnection(id);
        Assert.NotNull(connection);

        server.DisconnectClient(connection);
        Assert.Equal(ConnectionState.Disconnected, connection.CurrentState);

        await Task.Delay(1000);
        Assert.True(clientDisc);
    }

    [Fact]
    public async Task Test7()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8894, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        bool serverNotified = false;

        server.ClientDisconnected += (s, e) => serverNotified = true;

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8894, IAuthenticator.NoAuthResponse, timeout: 2000);
        await Task.Delay(500);

        Assert.Equal(Constant.SUCCESS, code);

        client.Disconnect();

        await Task.Delay(1000);

        Assert.True(serverNotified);
    }

    [Fact]
    public async Task Test8()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8895, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        //await server.StartAsync(); // Don't start the server
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8895, IAuthenticator.NoAuthResponse, timeout: 2000);
        await Task.Delay(500);

        Assert.Equal(Constant.NO_RESPONSE, code);
    }

    [Fact]
    public async Task Test9()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);
        // Here server will have a different protocol version than the client, but it will support the client's version still.

        var server = new Server(10, "127.0.0.1", 8897, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default, 3, 4); // So server still supports protocol version 3 and 4
        var client = new Client(3, IAuthenticator.NoAuth, socketContextClient); // Client is using protocol version 3

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8897, IAuthenticator.NoAuthResponse, timeout: 2000);
        await Task.Delay(500);

        Assert.Equal(Constant.SUCCESS, code);

        await Task.Delay(1000);

        Assert.NotNull(server.GetClientConnection(id));

        await server.StopAsync();
    }

    [Fact]
    public async Task Test10()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);
        // Here server will have a different protocol version than the client.

        var server = new Server(10, "127.0.0.1", 8898, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default, 4); // So server still supports protocol version 4
        var client = new Client(3, IAuthenticator.NoAuth, socketContextClient); // Client is using protocol version 3, so it should fail.

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8898, IAuthenticator.NoAuthResponse, timeout: 2000);
        await Task.Delay(500);

        Assert.Equal(Constant.FAILURE_UNSUPPORTED_PROTOCOL_VERSION, code);

        await Task.Delay(1000);

        await server.StopAsync();
    }

    [Fact]
    public async Task Test11()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8899, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default, 4);

        await server.StartAsync();
        var serverInfo = await Client.RequestServerInfoAsync<DefaultServerInfo>(socketContextClient, "127.0.0.1", 8899);

        Assert.NotNull(serverInfo);
        Assert.Equal(5u, serverInfo.ProtocolID);
        Assert.Collection(serverInfo.SupportedProtocols, p => Assert.Equal(4u, p));
        Assert.Equal(0u, serverInfo.ConnectedClients);
    }

    [Fact]
    public async Task Test12()
    {
        var fakeNet = new FakeNetwork(0, 0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8899, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default, 4);

        //await server.StartAsync(); // No server running.

        // Server info should return null if no server is running.
        var serverInfo = await Client.RequestServerInfoAsync<DefaultServerInfo>(socketContextClient, "127.0.0.1", 8899);

        Assert.Null(serverInfo);
    }

    [Theory]
    [InlineData(2, 0.0f, 100)]
    [InlineData(5, 0.0f, 100)]
    [InlineData(20, 0.5f, 20)]
    [InlineData(50, 0.4f, 20)]
    public async Task Test13(int amount, float packetLoss, int latency)
    {
        var fakeNet = new FakeNetwork(0, 0, latency); // 0% packet loss
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8899, 4, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default, 4);
        var client = new Client(4, IAuthenticator.NoAuth, socketContextClient);

        var valuesToSend = Enumerable.Range(0, amount).ToHashSet();
        ThreadSafe<HashSet<int>> received = new(new());

        server.ReceivedData += (sender, e) =>
        {
            var data = e.Data;
            received.LockedAction(r => r.Add(BitConverter.ToInt32(data)));
            server.SendToClient(new byte[1] { 11 }, e.From, Packets.ChannelType.Default); // This will make sure that the client will receive acks.
            output.WriteLine($"Server received data {BitConverter.ToInt32(data)}");
        };

        client.ServerAckedPacket += (sender, e) =>
        {
            output.WriteLine($"Server acked packet {e}");
        };

        client.ResentPacket += (sender, e) =>
        {
            output.WriteLine($"Client resent packet {e}");
        };

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8899, IAuthenticator.NoAuthResponse, timeout: 10000000);
        Assert.Equal(Constant.SUCCESS, code);

        await Task.Delay(1000);
        fakeNet._lossRate = packetLoss; // Set packet loss after connection is established

        foreach (var val in valuesToSend)
        {
            var seq = client.SendToServer(BitConverter.GetBytes(val), Packets.ChannelType.Reliable);
            output.WriteLine($"Client sent data {val} with seq {seq}");
            await Task.Delay(latency);
        }

        await Task.Delay(10000);

        Assert.Equal(amount, received.Value.Count);
        // Assert that all received values are in the valuesToSend set.
        Assert.True(received.Value.All(v => valuesToSend.Contains(v)), "Received values are not in the valuesToSend set.");
        // And the other way around.
        Assert.True(valuesToSend.All(v => received.Value.Contains(v)), "ValuesToSend values are not in the received set.");
    }
}

class TestPacket : ISerializable
{
    public int Value { get; set; }

    public static object Deserialize(BinaryReader reader)
    {
        return new TestPacket { Value = reader.ReadInt32() };
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Value);
    }
}