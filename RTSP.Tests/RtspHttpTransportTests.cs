namespace Rtsp.Tests;

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

#if NETFRAMEWORK
using TcpListener = Rtsp.Utils.Tests.TcpListener;
#endif

public class RtspHttpTransportTests
{
    private const string Request = """
                                   OPTIONS rtsp://localhost/stream RTSP/1.0
                                   CSeq: 1


                                   """;

    [Test]
    [CancelAfter(10 * 1000)]
    public async Task AnHttpTunnelReachesAnIPv4Server(CancellationToken token)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<TunnelData> serving = ServeTunnel(listener, token);

        using var transport =
            new RtspHttpTransport(new Uri($"http://127.0.0.1:{port}/stream"), new NetworkCredential());
        var request = Encoding.ASCII.GetBytes(Request);
        var stream = transport.GetStream();
        await stream.WriteAsync(request, token);
        await stream.FlushAsync(token);

        TunnelData tunnelData = await serving;
        Assert.That(tunnelData.GetData, Does.StartWith("GET /stream HTTP/1.0"));
        Assert.That(tunnelData.PostData, Does.StartWith("POST /stream HTTP/1.0"));
        Assert.That(tunnelData.BodyData, Is.EqualTo(Request));
    }

    [Test]
    [CancelAfter(10 * 1000)]
    public async Task AnHttpTunnelReachesAnIPv6Server(CancellationToken token)
    {
        using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<TunnelData> serving = ServeTunnel(listener, token);

        using var transport = new RtspHttpTransport(new Uri($"http://[::1]:{port}/stream"), new NetworkCredential());
        var request = Encoding.ASCII.GetBytes(Request);
        var stream = transport.GetStream();
        await stream.WriteAsync(request, token);
        await stream.FlushAsync(token);

        TunnelData tunnelData = await serving;
        Assert.That(tunnelData.GetData, Does.StartWith("GET /stream HTTP/1.0"));
        Assert.That(tunnelData.PostData, Does.StartWith("POST /stream HTTP/1.0"));
        Assert.That(tunnelData.BodyData, Is.EqualTo(Request));
    }


    private sealed record TunnelData(string GetData = "", string PostData = "", string BodyData = "");

    /// <summary>
    /// The server end of an RTSP over HTTP tunnel: the GET it answers, then the POST and the
    /// one request it carries.
    /// </summary>
    private static async Task<TunnelData> ServeTunnel(TcpListener listener, CancellationToken token)
    {
        // handle timeout
        token.Register(listener.Stop);

        using var getClient = await listener.AcceptTcpClientAsync();
        using var get = getClient.GetStream();
        var getData = await ReadHeadAsync(get, token);
        var ok = "HTTP/1.0 200 OK\r\nContent-Type: application/x-rtsp-tunnelled\r\n\r\n"u8;
        await get.WriteAsync(ok.ToArray(), token);
        await get.FlushAsync(token);

        using var postClient = await listener.AcceptTcpClientAsync();
        using var post = postClient.GetStream();
        var postData = await ReadHeadAsync(post, token);

        int length = Convert.ToBase64String(Encoding.ASCII.GetBytes(Request)).Length;
        byte[] body = new byte[length];
        for (var read = 0; read < length;)
        {
            int count = await post.ReadAsync(body.AsMemory(read, length - read), token);
            if (count == 0)
                throw new EndOfStreamException();
            read += count;
        }

        var bodyData = Encoding.ASCII.GetString(Convert.FromBase64String(Encoding.ASCII.GetString(body)));
        return new (getData, postData, bodyData);
    }

    private static async Task<string> ReadHeadAsync(Stream stream, CancellationToken token)
    {
        var head = new StringBuilder();
        var b = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var nbRead = await stream.ReadAsync(b, token);
            if (nbRead == 0)
                throw new EndOfStreamException(head.ToString());
            head.Append((char)b[0]);
        }

        return head.ToString();
    }
}