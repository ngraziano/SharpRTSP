namespace Rtsp.Tests;

using NUnit.Framework;
using Rtsp;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

[TestFixture()]
public class RtspListenSocketTests
{


    [Test()]
    public void StartStopTest()
    {
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspListenSocket(tcpListener);

        testObj.Start();
        Assert.That((tcpListener.LocalEndpoint as IPEndPoint)?.Port, Is.GreaterThan(0));
        testObj.Stop();
        testObj.Start();
        testObj.Stop();

        // should success without exception
    }

    [Test]
    [CancelAfter(1000)]

    public async Task SimpleAccept(CancellationToken cancellationToken)
    {
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspListenSocket(tcpListener);

        testObj.Start();
        var listenEndpoint = tcpListener.LocalEndpoint as IPEndPoint;
        Debug.Assert(listenEndpoint != null);


        var acceptTask = testObj.AcceptAsync(cancellationToken);
        var client = new TcpClient();
        client.Connect(listenEndpoint);

        var result = await acceptTask;

        Assert.That(acceptTask.Result, Is.AssignableTo<RtspTcpTransport>());
    }

    [Test()]
    [CancelAfter(1000)]
    public async Task AcceptWithoutStart(CancellationToken cancellationToken)
    {
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspListenSocket(tcpListener);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await testObj.AcceptAsync(cancellationToken));
    }
}