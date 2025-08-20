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
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspListenSocket(tcplistener);

        testObj.Start();
        Assert.That((tcplistener.LocalEndpoint as IPEndPoint)?.Port, Is.GreaterThan(0));
        testObj.Stop();
        testObj.Start();
        testObj.Stop();

        // should success without execption
    }

    [Test]
    [CancelAfter(1000)]

    public async Task SimpleAccept(CancellationToken cancellationToken)
    {
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspListenSocket(tcplistener);

        testObj.Start();
        var listenEndpoint = tcplistener.LocalEndpoint as IPEndPoint;
        Debug.Assert(listenEndpoint != null);


        var acceptTask = testObj.AcceptAsync(cancellationToken);
        var client = new TcpClient();
        client.Connect(listenEndpoint);

        var result = await acceptTask;

        Assert.That(acceptTask.Result, Is.AssignableTo<RtspTcpTransport>());
    }

    [Test()]
    [CancelAfter(1000)]
    public void AccpetWithoutStart(CancellationToken cancellationToken)
    {
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspListenSocket(tcplistener);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await testObj.AcceptAsync(cancellationToken));
    }
}