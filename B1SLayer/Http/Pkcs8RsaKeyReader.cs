using System;
using System.Security.Cryptography;

namespace B1SLayer;

/// <summary>
///     Reads an unencrypted RSA private key in PKCS#8 DER format, the format of the credentials issued by the
///     SAP Business One Extension Single Sign-On Manager, into <see cref="RSAParameters" />.
/// </summary>
/// <remarks>
///     Only used on netstandard2.0, where <c>RSA.ImportPkcs8PrivateKey</c> doesn't exist. Compiled on every target so
///     tests can compare it against that method. It is an encoding parser, with no cryptography of its own, and its input
///     is the
///     application's own key. Error messages never include any part of the key.
/// </remarks>
internal static class Pkcs8RsaKeyReader
{
    private const byte IntegerTag = 0x02;
    private const byte OctetStringTag = 0x04;
    private const byte ObjectIdentifierTag = 0x06;
    private const byte SequenceTag = 0x30;

    // 1.2.840.113549.1.1.1 (rsaEncryption)
    private static readonly byte[] RsaEncryptionOid = [0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01];

    public static RSAParameters Read(byte[] der)
    {
        var position = 0;

        // PrivateKeyInfo ::= SEQUENCE { version, privateKeyAlgorithm, privateKey OCTET STRING, attributes [0] OPTIONAL }
        var privateKeyInfo = ReadElement(der, ref position, der.Length, SequenceTag);
        EnsureConsumed(position, der.Length);

        position = privateKeyInfo.Start;
        EnsureVersionZero(der, ref position, privateKeyInfo.End);

        var algorithm = ReadElement(der, ref position, privateKeyInfo.End, SequenceTag);
        var algorithmPosition = algorithm.Start;
        var oid = ReadElement(der, ref algorithmPosition, algorithm.End, ObjectIdentifierTag);

        if (!Matches(der, oid, RsaEncryptionOid))
        {
            throw new CryptographicException("The private key is not an RSA key.");
        }

        var privateKey = ReadElement(der, ref position, privateKeyInfo.End, OctetStringTag);
        position = privateKey.Start;

        // RSAPrivateKey ::= SEQUENCE { version, n, e, d, p, q, dp, dq, qinv, otherPrimeInfos OPTIONAL }
        var rsaPrivateKey = ReadElement(der, ref position, privateKey.End, SequenceTag);
        EnsureConsumed(position, privateKey.End);

        position = rsaPrivateKey.Start;
        EnsureVersionZero(der, ref position, rsaPrivateKey.End);

        var modulus = ReadUnsignedInteger(der, ref position, rsaPrivateKey.End);
        var exponent = ReadUnsignedInteger(der, ref position, rsaPrivateKey.End);
        var d = ReadUnsignedInteger(der, ref position, rsaPrivateKey.End);
        var p = ReadUnsignedInteger(der, ref position, rsaPrivateKey.End);
        var q = ReadUnsignedInteger(der, ref position, rsaPrivateKey.End);
        var dp = ReadUnsignedInteger(der, ref position, rsaPrivateKey.End);
        var dq = ReadUnsignedInteger(der, ref position, rsaPrivateKey.End);
        var inverseQ = ReadUnsignedInteger(der, ref position, rsaPrivateKey.End);
        EnsureConsumed(position, rsaPrivateKey.End);

        // RSA.ImportParameters requires these exact lengths, while DER drops the leading zeros of smaller values
        var halfLength = (modulus.Length + 1) / 2;

        return new RSAParameters
        {
            Modulus = modulus,
            Exponent = exponent,
            D = PadLeft(d, modulus.Length),
            P = PadLeft(p, halfLength),
            Q = PadLeft(q, halfLength),
            DP = PadLeft(dp, halfLength),
            DQ = PadLeft(dq, halfLength),
            InverseQ = PadLeft(inverseQ, halfLength)
        };
    }

    private static void EnsureVersionZero(byte[] der, ref int position, int limit)
    {
        var version = ReadElement(der, ref position, limit, IntegerTag);

        if (version.End - version.Start != 1 || der[version.Start] != 0)
        {
            throw new CryptographicException("The private key has an unsupported version.");
        }
    }

    private static void EnsureConsumed(int position, int limit)
    {
        if (position != limit)
        {
            throw Malformed();
        }
    }

    private static bool Matches(byte[] der, Element element, byte[] expected)
    {
        if (element.End - element.Start != expected.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
            if (der[element.Start + i] != expected[i])
            {
                return false;
            }

        return true;
    }

    private static byte[] ReadUnsignedInteger(byte[] der, ref int position, int limit)
    {
        var element = ReadElement(der, ref position, limit, IntegerTag);
        var start = element.Start;

        // The sign byte DER adds to a value whose high bit is set isn't part of the value
        while (start < element.End - 1 && der[start] == 0) start++;

        var value = new byte[element.End - start];
        Buffer.BlockCopy(der, start, value, 0, value.Length);
        return value;
    }

    private static byte[] PadLeft(byte[] value, int length)
    {
        if (value.Length == length)
        {
            return value;
        }

        if (value.Length > length)
        {
            throw Malformed();
        }

        var padded = new byte[length];
        Buffer.BlockCopy(value, 0, padded, length - value.Length, value.Length);
        Array.Clear(value, 0, value.Length);
        return padded;
    }

    private static Element ReadElement(byte[] der, ref int position, int limit, byte expectedTag)
    {
        if (position >= limit || der[position] != expectedTag)
        {
            throw Malformed();
        }

        position++;

        if (position >= limit)
        {
            throw Malformed();
        }

        int length = der[position++];

        // The long form: the low bits tell how many bytes hold the length, and a valid key never needs more than 4
        if (length >= 0x80)
        {
            var lengthBytes = length & 0x7F;

            if (lengthBytes is 0 or > 4 || limit - position < lengthBytes)
            {
                throw Malformed();
            }

            length = 0;

            for (var i = 0; i < lengthBytes; i++)
            {
                if (length > int.MaxValue >> 8)
                {
                    throw Malformed();
                }

                length = length << 8 | der[position++];
            }
        }

        if (length > limit - position)
        {
            throw Malformed();
        }

        var element = new Element(position, position + length);
        position = element.End;
        return element;
    }

    private static CryptographicException Malformed() => new("The private key is not a valid PKCS#8 encoded RSA key.");

    private readonly struct Element(
        int start,
        int end)
    {
        public int Start { get; } = start;
        public int End { get; } = end;
    }
}
