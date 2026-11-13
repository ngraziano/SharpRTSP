namespace Rtsp.Utils.Tests;

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

internal static class CertificateUtility
{
    /// <summary>A self-signed certificate for localhost, with its key, as a TLS server needs it.</summary>
    public static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());

        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // through PKCS#12, as Windows only serves TLS with a key it can find again
        var pfx = created.Export(X509ContentType.Pfx);
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(pfx, null);
#else
        return new X509Certificate2(pfx);
#endif
    }
}