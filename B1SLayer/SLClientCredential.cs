using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

/// <summary>
///     The credential a confidential client authenticates to the token endpoint with:
///     a signed JWT (<c>private_key_jwt</c>, RFC 7523), which SAP highly recommends, or a client secret.
/// </summary>
public abstract class SLClientCredential
{
    private SLClientCredential()
    {
    }

    /// <summary>
    ///     Creates a credential that authenticates with a signed JWT, using the given RSA private key.
    /// </summary>
    /// <param name="base64Pkcs8PrivateKey">
    ///     The credentials issued by the SAP Business One Extension Single Sign-On Manager for
    ///     Signed JWT: a base64 encoded, unencrypted PKCS#8 RSA private key, without PEM armor.
    /// </param>
    public static SLClientCredential FromPrivateKey(string base64Pkcs8PrivateKey) => new SignedJwtCredential(CreateSigner(base64Pkcs8PrivateKey));

    /// <summary>
    ///     Creates a credential that authenticates with a signed JWT through a caller-supplied signer, for
    ///     keys that are not available to the application, like those kept in a key vault or an HSM.
    /// </summary>
    /// <param name="rs256Signer">
    ///     A delegate that returns the RS256 signature (RSASSA-PKCS1-v1_5 with SHA-256) of the given bytes. Signing and
    ///     sending the
    ///     request must take under about 50 seconds, as SAP's Keycloak rejects an assertion issued over 60 seconds earlier.
    /// </param>
    public static SLClientCredential FromSigner(Func<byte[], CancellationToken, Task<byte[]>> rs256Signer)
    {
        if (rs256Signer == null)
        {
            throw new ArgumentNullException(nameof(rs256Signer));
        }

        return new SignedJwtCredential(rs256Signer);
    }

    /// <summary>
    ///     Creates a credential that authenticates with a client secret, sent in the
    ///     request body (<c>client_secret_post</c>), as described by SAP's documentation.
    /// </summary>
    /// <param name="clientSecret">
    ///     The client secret.
    /// </param>
    public static SLClientCredential FromClientSecret(string clientSecret)
    {
        if (string.IsNullOrEmpty(clientSecret))
        {
            throw new ArgumentException("The client secret can not be empty.", nameof(clientSecret));
        }

        return new ClientSecretCredential(clientSecret);
    }

    /// <summary>
    ///     Returns a description of this credential, which never includes any part of it.
    /// </summary>
    public sealed override string ToString() => $"{nameof(SLClientCredential)}(redacted)";

    /// <summary>
    ///     Adds the parameters that authenticate the client to the given token request.
    /// </summary>
    internal abstract Task AddToTokenRequestAsync(IList<KeyValuePair<string, string>> form, string clientId, Uri tokenEndpoint, DateTimeOffset now, CancellationToken cancellationToken);

    private static Func<byte[], CancellationToken, Task<byte[]>> CreateSigner(string base64Pkcs8PrivateKey)
    {
        if (string.IsNullOrWhiteSpace(base64Pkcs8PrivateKey))
        {
            throw new ArgumentException("The private key can not be empty.", nameof(base64Pkcs8PrivateKey));
        }

        byte[] privateKey = null;
        var rsa = RSA.Create();

        try
        {
            privateKey = Convert.FromBase64String(base64Pkcs8PrivateKey);
#if NETSTANDARD2_0
            rsa.ImportParameters(Pkcs8RsaKeyReader.Read(privateKey));
#else
            rsa.ImportPkcs8PrivateKey(privateKey, out var bytesRead);

            if (bytesRead != privateKey.Length)
            {
                throw new CryptographicException();
            }
#endif
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            rsa.Dispose();

            // The exception is deliberately not chained, so nothing derived from the key's content goes along with it
            throw new ArgumentException("The private key must be a base64 encoded, unencrypted PKCS#8 RSA private key, without PEM armor.", nameof(base64Pkcs8PrivateKey));
        }
        finally
        {
            if (privateKey != null)
            {
                Array.Clear(privateKey, 0, privateKey.Length);
            }
        }

        return (data, _) => Task.FromResult(rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private sealed class ClientSecretCredential(string clientSecret) : SLClientCredential
    {
        internal override Task AddToTokenRequestAsync(IList<KeyValuePair<string, string>> form, string clientId, Uri tokenEndpoint, DateTimeOffset now, CancellationToken cancellationToken)
        {
            form.Add(new KeyValuePair<string, string>("client_secret", clientSecret));
            return Task.CompletedTask;
        }
    }

    private sealed class SignedJwtCredential(Func<byte[], CancellationToken, Task<byte[]>> signer) : SLClientCredential
    {
        private const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

        // Short-lived as recommended by RFC 7523 section 3, instead of the ±120 minutes of SAP's sample
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        // SAP's Keycloak rejects an iat over 60 seconds old and one over about 15 seconds ahead, so this only spends a little of
        // the time an assertion has to reach it, signing included, for a clock that is somewhat ahead of the server's
        private static readonly TimeSpan IssuedAtTolerance = TimeSpan.FromSeconds(10);

        // The start of validity has no such age limit, so this one can cover a clock further ahead
        private static readonly TimeSpan NotBeforeTolerance = TimeSpan.FromSeconds(60);

        internal override async Task AddToTokenRequestAsync(IList<KeyValuePair<string, string>> form, string clientId, Uri tokenEndpoint, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["alg"] = "RS256", ["typ"] = "JWT" });

            var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["iss"] = clientId,
                ["sub"] = clientId,
                ["aud"] = tokenEndpoint.OriginalString,
                ["jti"] = Guid.NewGuid().ToString(),
                ["iat"] = (now - IssuedAtTolerance).ToUnixTimeSeconds(),
                ["nbf"] = (now - NotBeforeTolerance).ToUnixTimeSeconds(),
                ["exp"] = (now + Lifetime).ToUnixTimeSeconds()
            });

            var signingInput = Base64UrlEncode(header) + "." + Base64UrlEncode(payload);
            var signature = await signer(Encoding.ASCII.GetBytes(signingInput), cancellationToken).ConfigureAwait(false);

            form.Add(new KeyValuePair<string, string>("client_assertion_type", ClientAssertionType));
            form.Add(new KeyValuePair<string, string>("client_assertion", signingInput + "." + Base64UrlEncode(signature)));
        }

        private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
