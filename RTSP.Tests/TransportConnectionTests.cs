using NUnit.Framework;
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Rtsp.Tests
{
    /// <summary>
    /// How the client transports connect: to IPv6 servers, and over TLS where it is asked for.
    /// </summary>
    [TestFixture]
    public class TransportConnectionTests
    {
        private const string Request = "OPTIONS rtsp://localhost/stream RTSP/1.0\r\nCSeq: 1\r\n\r\n";

        [Test]
        public void ATcpTransportReconnectsToAnIPv6Server()
        {
            var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var connection = new TcpClient(AddressFamily.InterNetworkV6);
                connection.Connect(IPAddress.IPv6Loopback, port);
                using var first = listener.AcceptTcpClient();

                var transport = new RtspTcpTransport(connection);
                transport.Close();
                transport.Reconnect();
                using var second = listener.AcceptTcpClient();

                Assert.That(transport.Connected, Is.True);
                transport.Close();
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void ATcpTransportReconnectsToAnIPv4ServerReachedOverADualModeSocket()
        {
            // the remote end point of a dual mode socket is the IPv4 address mapped into IPv6
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var connection = new TcpClient(AddressFamily.InterNetworkV6);
                connection.Client.DualMode = true;
                connection.Connect(IPAddress.Loopback, port);
                using var first = listener.AcceptTcpClient();

                var transport = new RtspTcpTransport(connection);
                transport.Close();
                transport.Reconnect();
                using var second = listener.AcceptTcpClient();

                Assert.That(transport.Connected, Is.True);
                transport.Close();
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void AnHttpTunnelReachesAnIPv6Server()
        {
            var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<Tunnel> serving = Task.Run(() => ServeTunnel(listener, client => client.GetStream()));

                using var transport = new RtspHttpTransport(new Uri($"http://[::1]:{port}/stream"), new NetworkCredential());
                SendRequest(transport.GetStream());

                Tunnel tunnel = Wait(serving);
                Assert.That(tunnel.Get, Does.StartWith("GET /stream HTTP/1.0"));
                Assert.That(tunnel.Post, Does.StartWith("POST /stream HTTP/1.0"));
                Assert.That(tunnel.Body, Is.EqualTo(Request));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void AnHttpsTunnelRunsHttpOverTls()
        {
            // HTTP inside TLS, on both connections, from their first byte - not TLS carried inside
            // a plain HTTP tunnel, which an HTTPS server cannot read
            using X509Certificate2 certificate = CreateCertificate();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<Tunnel> serving = Task.Run(() => ServeTunnel(listener, client =>
                {
                    var tls = new SslStream(client.GetStream(), false);
                    tls.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, false);
                    return tls;
                }));

                SslPolicyErrors errors = SslPolicyErrors.None;
                using var transport = new RTSPHttpsTransport(new Uri($"https://localhost:{port}/stream"), new NetworkCredential(),
                    (sender, cert, chain, policyErrors) => { errors |= policyErrors; return true; });
                SendRequest(transport.GetStream());

                Tunnel tunnel = Wait(serving);
                Assert.That(tunnel.Get, Does.StartWith("GET /stream HTTP/1.0"));
                Assert.That(tunnel.Post, Does.StartWith("POST /stream HTTP/1.0"));
                Assert.That(tunnel.Body, Is.EqualTo(Request));
                Assert.That(errors & SslPolicyErrors.RemoteCertificateNameMismatch, Is.EqualTo(SslPolicyErrors.None));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void AnRtspsConnectionChecksTheCertificateAgainstTheHostName()
        {
            using X509Certificate2 certificate = CreateCertificate();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task serving = Task.Run(() =>
                {
                    using var client = listener.AcceptTcpClient();
                    using var tls = new SslStream(client.GetStream(), false);
                    tls.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, false);
                });

                SslPolicyErrors errors = SslPolicyErrors.None;
                var transport = new RtspTcpTlsTransport(new Uri($"rtsps://localhost:{port}/stream"),
                    (sender, cert, chain, policyErrors) => { errors |= policyErrors; return true; });
                transport.GetStream();
                Wait(serving);

                // the certificate names localhost: checked against the address, it does not match
                Assert.That(errors & SslPolicyErrors.RemoteCertificateNameMismatch, Is.EqualTo(SslPolicyErrors.None));
                transport.Close();
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void AnRtspsConnectionMadeElsewhereChecksTheCertificateAgainstTheHostNameGiven()
        {
            using X509Certificate2 certificate = CreateCertificate();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task serving = Task.Run(() =>
                {
                    using var client = listener.AcceptTcpClient();
                    using var tls = new SslStream(client.GetStream(), false);
                    tls.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, false);
                });

                SslPolicyErrors errors = SslPolicyErrors.None;
                var transport = new RtspTcpTlsTransport(new TcpClient("localhost", port),
                    (sender, cert, chain, policyErrors) => { errors |= policyErrors; return true; })
                {
                    TargetHost = "localhost",
                };
                transport.GetStream();
                Wait(serving);

                Assert.That(errors & SslPolicyErrors.RemoteCertificateNameMismatch, Is.EqualTo(SslPolicyErrors.None));
                transport.Close();
            }
            finally
            {
                listener.Stop();
            }
        }

        private sealed class Tunnel
        {
            public string Get = "";
            public string Post = "";
            public string Body = "";
        }

        /// <summary>
        /// The server end of an RTSP over HTTP tunnel: the GET it answers, then the POST and the
        /// one request it carries.
        /// </summary>
        private static Tunnel ServeTunnel(TcpListener listener, Func<TcpClient, Stream> open)
        {
            var tunnel = new Tunnel();

            using var getClient = listener.AcceptTcpClient();
            using Stream get = open(getClient);
            tunnel.Get = ReadHead(get);
            byte[] ok = Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nContent-Type: application/x-rtsp-tunnelled\r\n\r\n");
            get.Write(ok, 0, ok.Length);
            get.Flush();

            using var postClient = listener.AcceptTcpClient();
            using Stream post = open(postClient);
            tunnel.Post = ReadHead(post);

            int length = Convert.ToBase64String(Encoding.ASCII.GetBytes(Request)).Length;
            byte[] body = new byte[length];
            for (int read = 0; read < length;)
            {
                int count = post.Read(body, read, length - read);
                if (count == 0)
                    throw new EndOfStreamException();
                read += count;
            }
            tunnel.Body = Encoding.ASCII.GetString(Convert.FromBase64String(Encoding.ASCII.GetString(body)));
            return tunnel;
        }

        private static void SendRequest(Stream stream)
        {
            byte[] request = Encoding.ASCII.GetBytes(Request);
            stream.Write(request, 0, request.Length);
            stream.Flush();
        }

        private static string ReadHead(Stream stream)
        {
            var head = new StringBuilder();
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new EndOfStreamException(head.ToString());
                head.Append((char)b);
            }
            return head.ToString();
        }

        private static T Wait<T>(Task<T> task)
        {
            Assert.That(task.Wait(TimeSpan.FromSeconds(10)), Is.True, "the server did not finish");
            return task.Result;
        }

        private static void Wait(Task task)
        {
            Assert.That(task.Wait(TimeSpan.FromSeconds(10)), Is.True, "the server did not finish");
        }

        /// <summary>A self signed certificate for localhost, with its key, as a TLS server needs it.</summary>
        private static X509Certificate2 CreateCertificate()
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            request.CertificateExtensions.Add(names.Build());

            using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            // through PKCS#12, as Windows only serves TLS with a key it can find again
            byte[] pfx = created.Export(X509ContentType.Pfx);
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(pfx, null);
#else
            return new X509Certificate2(pfx);
#endif
        }
    }
}
