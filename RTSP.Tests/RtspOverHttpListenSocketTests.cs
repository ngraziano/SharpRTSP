namespace Rtsp.Tests;

using NUnit.Framework;
using Rtsp;
using System;
using System.Diagnostics;
using System.IO;
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
        for (int i = 0; i < cookieLength; i++)
        {
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


    private const string getWithoutSession =
            $"""
            GET /sw.mov HTTP/1.0
            User-Agent: QTS (qtver=4.1;cpu=PPC;os=Mac 8.6)
            Accept: application/x-rtsp-tunnelled
            Pragma: no-cache
            Cache-Control: no-cache
            
            
            """;

    private const string getWithoutAccept =
            $"""
            GET /sw.mov HTTP/1.0
            User-Agent: QTS (qtver=4.1;cpu=PPC;os=Mac 8.6)
            x-sessioncookie: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            Pragma: no-cache
            Cache-Control: no-cache
            
            
            """;

    private const string rtspMessage =
            $"""
            OPTION * RTSP/1.0
            User-Agent: QTS (qtver=4.1;cpu=PPC;os=Mac 8.6)
            x-sessioncookie: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            
            
            """;

    [Test()]
    public void StartStopTest()
    {
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspOverHttpListenSocket(tcplistener);

        testObj.Start();
        Assert.That((tcplistener.LocalEndpoint as IPEndPoint)?.Port, Is.GreaterThan(0));
        testObj.Stop();
        testObj.Start();
        testObj.Start();
        testObj.Stop();
        testObj.Stop();

        // should success without execption
    }


    [Test]
    [CancelAfter(1000)]

    public async Task InvalidDataAcceptAysnc(CancellationToken cancellationToken)
    {
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspOverHttpListenSocket(tcplistener);

        testObj.Start();
        var listenEndpoint = tcplistener.LocalEndpoint as IPEndPoint;
        Debug.Assert(listenEndpoint != null);

        using var client = new TcpClient();
        client.Connect(listenEndpoint);

        var data = new byte[2048];
        random.NextBytes(data);

        await client.GetStream().WriteAsync(data, cancellationToken);
        await client.GetStream().FlushAsync(cancellationToken);


        // Invalid client get disconnected
        Assert.ThrowsAsync<IOException>(async () => _ = await client.GetStream().ReadAsync(data, cancellationToken));

        testObj.Stop();

        Assert.That(client.Connected, Is.False);
    }


    [Test]
    [TestCase(getWithoutSession)]
    [TestCase(getWithoutAccept)]
    [TestCase(rtspMessage)]
    [CancelAfter(1000)]

    public async Task IncompleteDataAcceptAysnc(string dataIn, CancellationToken cancellationToken)
    {
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspOverHttpListenSocket(tcplistener);

        testObj.Start();
        var listenEndpoint = tcplistener.LocalEndpoint as IPEndPoint;
        Debug.Assert(listenEndpoint != null);

        using var client = new TcpClient();
        client.Connect(listenEndpoint);

        await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(dataIn), cancellationToken);
        await client.GetStream().FlushAsync(cancellationToken);


        // Invalid client get disconnected after initial response
        var data = new byte[2048];
        bool connectionWasClosed = false;
        while (client.Connected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if(await client.GetStream().ReadAsync(data, cancellationToken) == 0)
                {
                    connectionWasClosed = true;
                    break;
                }
            }
            catch (IOException)
            {
                connectionWasClosed = true;
                // disconnection is normal
                break;
            }
        }

        testObj.Stop();

        Assert.That(connectionWasClosed, Is.True);
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

        using var getclient = new TcpClient();
        getclient.Connect(listenEndpoint);
        getclient.GetStream().Write(GetRequest(sessionCookie));

        using var postclient = new TcpClient();
        postclient.Connect(listenEndpoint);
        postclient.GetStream().Write(PostRequest(sessionCookie));

        var result = await acceptTask;

        testObj.Stop();

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

        using var postclient = new TcpClient();
        postclient.Connect(listenEndpoint);
        postclient.GetStream().Write(PostRequest(sessionCookie));

        using var getclient = new TcpClient();
        getclient.Connect(listenEndpoint);
        getclient.GetStream().Write(GetRequest(sessionCookie));

        var result = await acceptTask;

        testObj.Stop();

        Assert.That(result, Is.AssignableTo<RtspHttpServerTransport>());
    }


    [Test()]
    [CancelAfter(1000)]
    public void AcceptWithoutStart(CancellationToken cancellationToken)
    {
        var tcplistener = new TcpListener(IPAddress.Loopback, 0);
        var testObj = new RtspOverHttpListenSocket(tcplistener);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await testObj.AcceptAsync(cancellationToken));
    }
}