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
        var success = await client.ConnectAsync(25, "127.0.0.1", 8888, IAuthenticator.NoAuthResponse);
        Assert.True(success);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test2()
    {
        var server = new Server(10, "127.0.0.1", 8889, 5, new NeverAuth());
        var client = new Client(5, IAuthenticator.NoAuth);

        await server.StartAsync();
        var success = await client.ConnectAsync(25, "127.0.0.1", 8889, IAuthenticator.NoAuthResponse);
        Assert.False(success);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test3()
    {
        var server = new Server(10, "127.0.0.1", 8890, 5, new PasswordAuth("goodpassword"));
        var client = new Client(5, IAuthenticator.NoAuth);

        await server.StartAsync();
        var success = await client.ConnectAsync(25, "127.0.0.1", 8890, (id, ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("goodpassword")));
        Assert.True(success);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test4()
    {
        var server = new Server(10, "127.0.0.1", 8891, 5, new PasswordAuth("goodpassword"));
        var client = new Client(5, IAuthenticator.NoAuth);

        await server.StartAsync();
        var success = await client.ConnectAsync(25, "127.0.0.1", 8891, (id, ch) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("thewrongpassword")));
        Assert.False(success);
        await server.StopAsync();
    }

    [Fact]
    public async Task Test5()
    {
        var server = new Server(10, "127.0.0.1", 8892, 5, IAuthenticator.NoAuth);
        var client = new Client(0, IAuthenticator.NoAuth);

        await server.StartAsync();
        var success = await client.ConnectAsync(25, "127.0.0.1", 8892, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.False(success);
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
        var success = await client.ConnectAsync(25, "127.0.0.1", 8893, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.True(success);

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
        var success = await client.ConnectAsync(25, "127.0.0.1", 8894, IAuthenticator.NoAuthResponse, timeout: 2000);
        Assert.True(success);

        client.Disconnect();

        await Task.Delay(1000);

        Assert.True(serverNotified);
    }
}