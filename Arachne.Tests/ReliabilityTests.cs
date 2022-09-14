using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Arachne.Packets;
using Xunit;

namespace Arachne.Tests;

public class ReliabilityTests
{
    [Fact]
    public async Task Test1()
    {
        var fakeNet = new FakeNetwork(0.0f, 100);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(1, "127.0.0.1", 8888, 0, IAuthenticator.NoAuth, socketContextServer);
        var client = new Client(0, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var code = await client.ConnectAsync(25, "127.0.0.1", 8888, IAuthenticator.NoAuthResponse);
        Assert.Equal(Constant.SUCCESS, code);
    }

    [Fact]
    public async Task Test2()
    {
        var fakeNet = new FakeNetwork(0.0f, 1);
        var socketContextServer = new FakeSocketContext(fakeNet);
        var socketContextClient = new FakeSocketContext(fakeNet);

        var server = new Server(1, "127.0.0.1", 8888, 0, new PasswordAuth("goodpass"), socketContextServer);
        var client = new Client(0, IAuthenticator.NoAuth, socketContextClient);

        await server.StartAsync();
        var code = await client.ConnectAsync(25, "127.0.0.1", 8888, PasswordAuth.Response("goodpass"));
        Assert.Equal(Constant.SUCCESS, code);
    }
}