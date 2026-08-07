using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Jotdex.Server.Hosting;

/// <summary>Create or load a long-lived self-signed PFX under the app data config folder.</summary>
public static class SelfSignedHttpsCertificate
{
    public const string FileName = "jotdex-self-signed.pfx";

    public static X509Certificate2 GetOrCreate(string dataRoot)
    {
        var dir = Path.Combine(dataRoot, "config");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FileName);

        if (File.Exists(path))
        {
            try
            {
                return X509CertificateLoader.LoadPkcs12FromFile(path, null);
            }
            catch
            {
                // Regenerate if corrupt
            }
        }

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=Jotdex Local",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")], // serverAuth
                critical: false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        try
        {
            var host = Dns.GetHostName();
            if (!string.IsNullOrWhiteSpace(host))
                san.AddDnsName(host);
        }
        catch
        {
            /* ignore */
        }
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        req.CertificateExtensions.Add(san.Build());

        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        var pfx = cert.Export(X509ContentType.Pfx);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllBytes(tmp, pfx);
        File.Move(tmp, path, overwrite: true);

        return X509CertificateLoader.LoadPkcs12(pfx, null);
    }
}
