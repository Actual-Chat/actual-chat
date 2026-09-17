using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ActualChat.OAuth.Module;

namespace ActualChat.Mcp.IntegrationTests;

public sealed class SigningCertificateTest
{
    [Fact]
    public void LoadCertificateShouldReturnNullWithoutConfiguration()
        => OAuthModule.LoadCertificate(new OAuthSettings()).Should().BeNull();

    [Fact]
    public void LoadCertificateShouldLoadFromPemFiles()
    {
        // arrange
        var (certificate, rsa) = NewSelfSignedCertificate();
        using var _ = certificate;
        using var __ = rsa;
        var dir = Directory.CreateTempSubdirectory();
        try {
            var certPath = Path.Combine(dir.FullName, "tls.crt");
            var keyPath = Path.Combine(dir.FullName, "tls.key");
            File.WriteAllText(certPath, certificate.ExportCertificatePem());
            File.WriteAllText(keyPath, rsa.ExportRSAPrivateKeyPem());

            // act
            using var loaded = OAuthModule.LoadCertificate(new OAuthSettings {
                SigningCertificatePath = certPath,
                SigningKeyPath = keyPath,
            });

            // assert
            loaded.Should().NotBeNull();
            loaded!.HasPrivateKey.Should().BeTrue();
            loaded.Thumbprint.Should().Be(certificate.Thumbprint);
        }
        finally {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadCertificateShouldThrowOnMissingPemFiles()
    {
        // arrange
        var dir = Directory.CreateTempSubdirectory();
        var settings = new OAuthSettings {
            SigningCertificatePath = Path.Combine(dir.FullName, "tls.crt"),
            SigningKeyPath = Path.Combine(dir.FullName, "tls.key"),
        };
        try {
            // act
            var load = () => OAuthModule.LoadCertificate(settings);

            // assert
            load.Should().Throw<Exception>();
        }
        finally {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadCertificateShouldLoadFromBase64Pkcs12()
    {
        // arrange
        var (certificate, rsa) = NewSelfSignedCertificate();
        using var _ = certificate;
        using var __ = rsa;
        var base64 = Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12));

        // act
        using var loaded = OAuthModule.LoadCertificate(new OAuthSettings { SigningCertificateBase64 = base64 });

        // assert
        loaded.Should().NotBeNull();
        loaded!.HasPrivateKey.Should().BeTrue();
        loaded.Thumbprint.Should().Be(certificate.Thumbprint);
    }

    // Private methods

    private static (X509Certificate2 Certificate, RSA Rsa) NewSelfSignedCertificate()
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=oauth-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return (certificate, rsa);
    }
}
