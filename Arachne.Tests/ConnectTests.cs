using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

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
    [Fact]
    public async Task Test1()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8888, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8888, IAuthenticator.NoAuthResponse);
        Assert.Equal(Constant.SUCCESS, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test2()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8889, 5, new NeverAuth(), socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8889, IAuthenticator.NoAuthResponse);
        Assert.Equal(Constant.FAILURE_INVALID_AUTHENTICATION, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test3()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8890, 5, new PasswordAuth("goodpassword"), socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8890, (ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("goodpassword")));
        Assert.Equal(Constant.SUCCESS, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test4()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8891, 5, new PasswordAuth("goodpassword"), socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8891, (ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("thewrongpassword")));
        Assert.Equal(Constant.FAILURE_INVALID_AUTHENTICATION, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test5()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8892, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(0, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8892, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.FAILURE_UNSUPPORTED_PROTOCOL_VERSION, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test6()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8893, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        bool clientDisc = false;

        client.DisconnectedByServer += (s, e) => clientDisc = true;

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8893, IAuthenticator.NoAuthResponse, timeout: 2000);
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
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8894, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        bool serverNotified = false;

        server.ClientDisconnected += (s, e) => serverNotified = true;

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8894, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.SUCCESS, code);

        client.Disconnect();

        await Task.Delay(1000);

        Assert.True(serverNotified);
    }

    [Fact]
    public async Task Test8()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8895, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default);
        var client = new Client(5, IAuthenticator.NoAuth, socketContextClient);

        //await server.StartAsync(); // Don't start the server
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8895, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.NO_RESPONSE, code);
    }

    [Fact]
    public async Task Test9()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);
        // Here server will have a different protocol version than the client, but it will support the client's version still.

        var server = new Server(10, "127.0.0.1", 8897, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default, 3, 4); // So server still supports protocol version 3 and 4
        var client = new Client(3, IAuthenticator.NoAuth, socketContextClient); // Client is using protocol version 3

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8897, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.SUCCESS, code);

        await Task.Delay(1000);

        Assert.NotNull(server.GetClientConnection(0));

        await server.StopAsync();
    }

    [Fact]
    public async Task Test10()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);
        // Here server will have a different protocol version than the client.

        var server = new Server(10, "127.0.0.1", 8898, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default, 4); // So server still supports protocol version 4
        var client = new Client(3, IAuthenticator.NoAuth, socketContextClient); // Client is using protocol version 3, so it should fail.

        await server.StartAsync();
        var (code, id) = await client.ConnectAsync("127.0.0.1", 8898, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.FAILURE_UNSUPPORTED_PROTOCOL_VERSION, code);

        await Task.Delay(1000);

        await server.StopAsync();
    }

    [Fact]
    public async Task Test11()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
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
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(10, "127.0.0.1", 8899, 5, IAuthenticator.NoAuth, socketContextServer, IServerInfoProvider.Default, 4);

        //await server.StartAsync(); // No server running.

        // Server info should return null if no server is running.
        var serverInfo = await Client.RequestServerInfoAsync<DefaultServerInfo>(socketContextClient, "127.0.0.1", 8899);

        Assert.Null(serverInfo);
    }
}