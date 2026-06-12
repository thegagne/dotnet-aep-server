using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Aep.Server.Http;

/// <summary>Options for page-token protection, bound from <c>PageToken</c>.</summary>
public sealed class PageTokenOptions
{
    /// <summary>
    /// Base64-encoded 32-byte key used to encrypt page tokens. Leave unset to use a random
    /// per-process key (fine for a single instance; tokens then expire on restart). Set a
    /// stable key to keep tokens valid across restarts and across multiple instances.
    /// </summary>
    public string? Key { get; set; }
}

/// <summary>
/// Turns an internal list cursor (the last id) into an AEP-158 page token and back. Tokens are
/// AES-GCM encrypted with a server-side key, so they are <b>opaque</b> (reveal nothing about the
/// cursor) and <b>unforgeable</b> (a client cannot craft or tamper with one). The token is also
/// bound to the resource, so a token from one resource is rejected on another. Output is URL-safe.
/// </summary>
public sealed class PageTokenProtector
{
    private const int NonceSize = 12; // AES-GCM standard nonce
    private const int TagSize = 16;   // AES-GCM auth tag
    private readonly byte[] _key;

    public PageTokenProtector(IOptions<PageTokenOptions> options)
    {
        var configured = options.Value.Key;
        _key = string.IsNullOrEmpty(configured) ? RandomNumberGenerator.GetBytes(32) : Convert.FromBase64String(configured);
        if (_key.Length != 32)
            throw new InvalidOperationException("PageToken:Key must be a base64-encoded 32-byte key.");
    }

    /// <summary>Encrypts <paramref name="cursor"/> (bound to <paramref name="resource"/>) into an opaque token.</summary>
    public string Protect(string resource, string cursor)
    {
        var plaintext = Encoding.UTF8.GetBytes($"{resource}\n{cursor}");
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var blob = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceSize);
        ciphertext.CopyTo(blob, NonceSize + TagSize);
        return Base64UrlEncode(blob);
    }

    /// <summary>
    /// Decrypts a token back to its cursor, or returns null if it is malformed, tampered with,
    /// forged, or was issued for a different resource. The caller maps null to HTTP 400.
    /// </summary>
    public string? Unprotect(string resource, string token)
    {
        byte[] blob;
        try { blob = Base64UrlDecode(token); }
        catch (FormatException) { return null; }

        if (blob.Length < NonceSize + TagSize)
            return null;

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var ciphertext = blob.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            return null; // tampered / forged / wrong key
        }

        var text = Encoding.UTF8.GetString(plaintext);
        var separator = text.IndexOf('\n');
        if (separator < 0)
            return null;
        return text[..separator] == resource ? text[(separator + 1)..] : null;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] Base64UrlDecode(string token)
    {
        var base64 = token.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(base64);
    }
}
