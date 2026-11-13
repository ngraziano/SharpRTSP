namespace Rtsp.Tests;

using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Rtsp.Utils.Tests;

#if NETFRAMEWORK
using TcpListener = Rtsp.Utils.Tests.TcpListener;
#endif

using NUnit.Framework;



[TestFixture]
public class RtspTcpTlsTransportTests
{
    private readonly TimeSpan _timeoutServer = TimeSpan.FromSeconds(10);
    
    [Test]
    public void AnRtspsConnectionChecksTheCertificateAgainstTheHostName()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(() =>
        {
            using var certificate = CertificateUtility.CreateCertificate();
            using var client = listener.AcceptTcpClient();
            using var tls = new SslStream(client.GetStream(), false);
            tls.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, false);
        });

        var errors = SslPolicyErrors.None;
        using var transport = new RtspTcpTlsTransport(new Uri($"rtsps://localhost:{port}/stream"),
            (_, _, _, policyErrors) => { errors |= policyErrors; return true; });
        transport.GetStream();
        
        Assert.That(serving.Wait(_timeoutServer), Is.True, "the server did not finish");
        // the certificate names localhost: checked against the address, it does not match
        Assert.That(errors & SslPolicyErrors.RemoteCertificateNameMismatch, Is.EqualTo(SslPolicyErrors.None));
    }
    
    [Test]
    public void AnRtspsConnectionMadeElsewhereChecksTheCertificateAgainstTheHostNameGiven()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(() =>
        {
            using var certificate = CertificateUtility.CreateCertificate();
            using var client = listener.AcceptTcpClient();
            using var tls = new SslStream(client.GetStream(), false);
            tls.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, false);
        });

        var errors = SslPolicyErrors.None;
        using var transport = new RtspTcpTlsTransport(new TcpClient("localhost", port),
            (_, _, _, policyErrors) => { errors |= policyErrors; return true; })
        {
            TargetHost = "localhost",
        };
        transport.GetStream();
        
        Assert.That(serving.Wait(_timeoutServer), Is.True, "the server did not finish");
        Assert.That(errors & SslPolicyErrors.RemoteCertificateNameMismatch, Is.EqualTo(SslPolicyErrors.None));
    }
}