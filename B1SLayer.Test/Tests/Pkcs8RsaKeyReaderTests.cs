using System.Security.Cryptography;
using System.Text;

namespace B1SLayer.Test;

/// <summary>
///     The reader is only used on netstandard2.0, where <see cref="RSA.ImportPkcs8PrivateKey" /> doesn't exist,
///     but it is compiled everywhere so it can be checked against that method here.
/// </summary>
public class Pkcs8RsaKeyReaderTests
{
    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(3072)]
    [InlineData(4096)]
    public void Read_MatchesTheFrameworkImplementation(int keySize)
    {
        using var rsa = RSA.Create(keySize);
        var expected = rsa.ExportParameters(true);

        var actual = Pkcs8RsaKeyReader.Read(rsa.ExportPkcs8PrivateKey());

        Assert.Equal(expected.Modulus, actual.Modulus);
        Assert.Equal(expected.Exponent, actual.Exponent);
        Assert.Equal(expected.D, actual.D);
        Assert.Equal(expected.P, actual.P);
        Assert.Equal(expected.Q, actual.Q);
        Assert.Equal(expected.DP, actual.DP);
        Assert.Equal(expected.DQ, actual.DQ);
        Assert.Equal(expected.InverseQ, actual.InverseQ);
    }

    [Fact]
    public void Read_ManyKeys_ProducesKeysThatSignVerifiably()
    {
        // Values with leading zeros are dropped to a shorter DER encoding, which about 1 in 256
        // components hits, so several keys are used for the padding to get a realistic exercise
        var data = Encoding.ASCII.GetBytes("header.payload");

        for (var i = 0; i < 40; i++)
        {
            using var original = RSA.Create(1024);
            using var imported = RSA.Create();
            imported.ImportParameters(Pkcs8RsaKeyReader.Read(original.ExportPkcs8PrivateKey()));

            var signature = imported.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            Assert.True(original.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
    }

    [Fact]
    public void Read_NonRsaKey_IsRejected()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var exception = Assert.Throws<CryptographicException>(() => Pkcs8RsaKeyReader.Read(ecdsa.ExportPkcs8PrivateKey()));

        Assert.Contains("not an RSA key", exception.Message);
    }

    [Fact]
    public void Read_EncryptedKey_IsRejected()
    {
        using var rsa = RSA.Create(1024);
        var encrypted = rsa.ExportEncryptedPkcs8PrivateKey("password", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000));

        Assert.Throws<CryptographicException>(() => Pkcs8RsaKeyReader.Read(encrypted));
    }

    [Fact]
    public void Read_TruncatedOrCorruptedInput_NeverReadsOutOfBounds()
    {
        using var rsa = RSA.Create(1024);
        var der = rsa.ExportPkcs8PrivateKey();

        for (var length = 0; length < der.Length; length++)
        {
            var truncated = der.AsSpan(0, length).ToArray();
            Assert.Throws<CryptographicException>(() => Pkcs8RsaKeyReader.Read(truncated));
        }

        // Any corruption may either be rejected or produce parameters, but must never surface anything else
        for (var i = 0; i < der.Length; i += 3)
        {
            var corrupted = (byte[])der.Clone();
            corrupted[i] ^= 0xFF;

            try
            {
                Pkcs8RsaKeyReader.Read(corrupted);
            }
            catch (CryptographicException)
            {
            }
        }
    }

    [Fact]
    public void Read_Rubbish_IsRejected()
    {
        Assert.Throws<CryptographicException>(() => Pkcs8RsaKeyReader.Read([]));
        Assert.Throws<CryptographicException>(() => Pkcs8RsaKeyReader.Read([0x30]));
        Assert.Throws<CryptographicException>(() => Pkcs8RsaKeyReader.Read([0x30, 0x84, 0xFF, 0xFF, 0xFF, 0xFF]));
        Assert.Throws<CryptographicException>(() => Pkcs8RsaKeyReader.Read([0x30, 0x80, 0x00, 0x00]));
        Assert.Throws<CryptographicException>(() => Pkcs8RsaKeyReader.Read(Encoding.ASCII.GetBytes("-----BEGIN PRIVATE KEY-----")));
    }
}
