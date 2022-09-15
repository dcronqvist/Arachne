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
        return (id, ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes(password));
    }
}

public class ConnectTests
{
    [Fact]
    public async Task Test1()
    {
        var server = new Server(10, "127.0.0.1", 8888, 5, IAuthenticator.NoAuth);
        var client = new Client(5, IAuthenticator.NoAuth);

        await server.StartAsync();
        var code = await client.ConnectAsync(25, "127.0.0.1", 8888, IAuthenticator.NoAuthResponse);
        Assert.Equal(Constant.SUCCESS, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test2()
    {
        var server = new Server(10, "127.0.0.1", 8889, 5, new NeverAuth());
        var client = new Client(5, IAuthenticator.NoAuth);

        await server.StartAsync();
        var code = await client.ConnectAsync(25, "127.0.0.1", 8889, IAuthenticator.NoAuthResponse);
        Assert.Equal(Constant.FAILURE_INVALID_AUTHENTICATION, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test3()
    {
        var server = new Server(10, "127.0.0.1", 8890, 5, new PasswordAuth("goodpassword"));
        var client = new Client(5, IAuthenticator.NoAuth);

        await server.StartAsync();
        var code = await client.ConnectAsync(25, "127.0.0.1", 8890, (id, ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("goodpassword")));
        Assert.Equal(Constant.SUCCESS, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test4()
    {
        var server = new Server(10, "127.0.0.1", 8891, 5, new PasswordAuth("goodpassword"));
        var client = new Client(5, IAuthenticator.NoAuth);

        await server.StartAsync();
        var code = await client.ConnectAsync(25, "127.0.0.1", 8891, (id, ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("thewrongpassword")));
        Assert.Equal(Constant.FAILURE_INVALID_AUTHENTICATION, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test5()
    {
        var server = new Server(10, "127.0.0.1", 8892, 5, IAuthenticator.NoAuth);
        var client = new Client(0, IAuthenticator.NoAuth);

        await server.StartAsync();
        var code = await client.ConnectAsync(25, "127.0.0.1", 8892, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.FAILURE_UNSUPPORTED_PROTOCOL_VERSION, code);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test6()
    {
        var server = new Server(10, "127.0.0.1", 8893, 5, IAuthenticator.NoAuth);
        var client = new Client(5, IAuthenticator.NoAuth);

        bool clientDisc = false;

        client.DisconnectedByServer += (s, e) => clientDisc = true;

        await server.StartAsync();
        var code = await client.ConnectAsync(25, "127.0.0.1", 8893, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.SUCCESS, code);

        var connection = server.GetClientConnection(25);
        Assert.NotNull(connection);

        server.DisconnectClient(connection);
        Assert.Equal(ConnectionState.Disconnected, connection.CurrentState);

        await Task.Delay(1000);
        Assert.True(clientDisc);
    }

    [Fact]
    public async Task Test7()
    {
        var server = new Server(10, "127.0.0.1", 8894, 5, IAuthenticator.NoAuth);
        var client = new Client(5, IAuthenticator.NoAuth);

        bool serverNotified = false;

        server.ClientDisconnected += (s, e) => serverNotified = true;

        await server.StartAsync();
        var code = await client.ConnectAsync(25, "127.0.0.1", 8894, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.SUCCESS, code);

        client.Disconnect();

        await Task.Delay(1000);

        Assert.True(serverNotified);
    }

    [Fact]
    public async Task Test8()
    {
        var server = new Server(10, "127.0.0.1", 8895, 5, IAuthenticator.NoAuth);
        var client = new Client(5, IAuthenticator.NoAuth);

        //await server.StartAsync(); // Don't start the server
        var code = await client.ConnectAsync(25, "127.0.0.1", 8895, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.NO_RESPONSE, code);
    }

    [Fact]
    public async Task Test9()
    {
        var server = new Server(10, "127.0.0.1", 8896, 5, IAuthenticator.NoAuth);
        var client = new Client(5, IAuthenticator.NoAuth);

        server.ConnectionEstablished += (s, e) => { Console.WriteLine($"Server: Connection Established to {e.Connection.RemoteEndPoint}"); };
        server.ConnectionTerminated += (s, e) => { Console.WriteLine($"{e.Connection.RemoteEndPoint} terminated"); };

        await server.StartAsync(); // Don't start the server
        var code = await client.ConnectAsync(25, "127.0.0.1", 8896, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.Equal(Constant.SUCCESS, code);

        await Task.Delay(14000);

        Assert.NotNull(server.GetClientConnection(25));
    }
}