namespace Rtsp.Tests;

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using Rtsp.Utils.Tests;

#if NETFRAMEWORK
using TcpListener = Rtsp.Utils.Tests.TcpListener;
#endif

public class RtspHttpsTransportTests
{
    private const string Request = """
                                   OPTIONS rtsp://localhost/stream RTSP/1.0
                                   CSeq: 1


                                   """;
    
    [Test]
    [CancelAfter(10 * 1000)]
    public async Task AnHttpsTunnelRunsHttpOverTls(CancellationToken token)
    {
        // HTTP inside TLS, on both connections, from their first byte - not TLS carried inside
        // a plain HTTP tunnel, which an HTTPS server cannot read
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
    
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<TunnelData> serving = ServeTunnel(listener, token);

        var errors = SslPolicyErrors.None;
        using var transport = new RTSPHttpsTransport(new Uri($"https://localhost:{port}/stream"), new NetworkCredential(),
            (_, _, _, policyErrors) => { errors |= policyErrors; return true; });
        var request = Encoding.ASCII.GetBytes(Request);
        var stream = transport.GetStream();
        await stream.WriteAsync(request, token);
        await stream.FlushAsync(token);
        
        TunnelData tunnel = await serving;
        Assert.That(tunnel.GetData, Does.StartWith("GET /stream HTTP/1.0"));
        Assert.That(tunnel.PostData, Does.StartWith("POST /stream HTTP/1.0"));
        Assert.That(tunnel.BodyData, Is.EqualTo(Request));
        Assert.That(errors & SslPolicyErrors.RemoteCertificateNameMismatch, Is.EqualTo(SslPolicyErrors.None));
    }
    
    private sealed record TunnelData(string GetData = "", string PostData = "", string BodyData = "");

    
    /// <summary>
    /// The server end of an RTSP over HTTP tunnel: the GET it answers, then the POST and the
    /// one request it carries.
    /// </summary>
    private static async Task<TunnelData> ServeTunnel(TcpListener listener, CancellationToken token)
    {
        using var certificate =  CertificateUtility.CreateCertificate();
        
        // handle timeout
        token.Register(listener.Stop);

        using var getClient = await listener.AcceptTcpClientAsync();
        using var get = new SslStream(getClient.GetStream(), false);
        await get.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false);

        var getData = await ReadHeadAsync(get, token);
        var ok = "HTTP/1.0 200 OK\r\nContent-Type: application/x-rtsp-tunnelled\r\n\r\n"u8;
        await get.WriteAsync(ok.ToArray(), token);
        await get.FlushAsync(token);

        using var postClient = await listener.AcceptTcpClientAsync();
        using var post = new SslStream(postClient.GetStream(), false);
        await post.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false);

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