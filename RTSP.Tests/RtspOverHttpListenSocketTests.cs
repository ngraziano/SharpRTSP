namespace Rtsp.Tests;

using NUnit.Framework;
using Rtsp;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[TestFixture()]
public class RtspOverHttpListenSocketTests
{
    private readonly Random random = new();
    private string GenerateCookie()
    {
        var cookieLength = random.Next(2, 30);
        var sb = new StringBuilder();
        for (int i = 0; i < cookieLength; i++) {
            sb.Append((char)random.Next(33, 127));
        }
        return sb.ToString();
    }

    private static byte[] GetRequest(string sessionCookie) => Encoding.UTF8.GetBytes(
            $"""
            GET /sw.mov HTTP/1.0
            User-Agent: QTS (qtver=4.1;cpu=PPC;os=Mac 8.6)
            x-sessioncookie: {sessionCookie}
            Accept: application/x-rtsp-tunnelled
            Pragma: no-cache
            Cache-Control: no-cache
            
            
            """);

    private static byte[] PostRequest(string sessionCookie) => Encoding.UTF8.GetBytes(
        $"""
        POST /sw.mov HTTP/1.0
        User-Agent: QTS (qtver=4.1;cpu=PPC;os=Mac 8.6)
        x-sessioncookie: {sessionCookie}
        Content-Type: application/x-rtsp-tunnelled
        
        
        """);

    [Test()]
    public void StartStopTest()
    {
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspOverHttpListenSocket(tcplistener);

        testObj.Start();
        Assert.That((tcplistener.LocalEndpoint as IPEndPoint)?.Port, Is.GreaterThan(0));
        testObj.Stop();
        testObj.Start();
        testObj.Stop();

        // should success without execption
    }

    [Test]
    [CancelAfter(1000)]

    public async Task SimpleAcceptAysnc(CancellationToken cancellationToken)
    {
        var sessionCookie = GenerateCookie();
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspOverHttpListenSocket(tcplistener);

        testObj.Start();
        var listenEndpoint = tcplistener.LocalEndpoint as IPEndPoint;
        Debug.Assert(listenEndpoint != null);


        var acceptTask = testObj.AcceptAsync(cancellationToken);
        
        var getclient = new TcpClient();
        getclient.Connect(listenEndpoint);
        getclient.GetStream().Write(GetRequest(sessionCookie));

        var postclient = new TcpClient();
        postclient.Connect(listenEndpoint);
        postclient.GetStream().Write(PostRequest(sessionCookie));

        var result = await acceptTask;

        Assert.That(result, Is.AssignableTo<RtspHttpServerTransport>());
    }

    [Test]
    [CancelAfter(1000)]

    public async Task PostBeforeGetAcceptAysnc(CancellationToken cancellationToken)
    {
        var sessionCookie = GenerateCookie();
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspOverHttpListenSocket(tcplistener);

        testObj.Start();
        var listenEndpoint = tcplistener.LocalEndpoint as IPEndPoint;
        Debug.Assert(listenEndpoint != null);


        var acceptTask = testObj.AcceptAsync(cancellationToken);
        
        var postclient = new TcpClient();
        postclient.Connect(listenEndpoint);
        postclient.GetStream().Write(PostRequest(sessionCookie));

        var getclient = new TcpClient();
        getclient.Connect(listenEndpoint);
        getclient.GetStream().Write(GetRequest(sessionCookie));

        var result = await acceptTask;

        Assert.That(result, Is.AssignableTo<RtspHttpServerTransport>());
    }


    [Test()]
    [CancelAfter(1000)]
    public void AccpetWithoutStart(CancellationToken cancellationToken)
    {
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspOverHttpListenSocket(tcplistener);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await testObj.AcceptAsync(cancellationToken));
    }
}