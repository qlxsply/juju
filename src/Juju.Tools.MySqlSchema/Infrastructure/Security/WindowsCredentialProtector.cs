using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;

namespace Juju.Tools.MySqlSchema.Infrastructure.Security;

/// <summary>Protects connection passwords for the current Windows user; ciphertext is safe for the local profile file.</summary>
public sealed class WindowsCredentialProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("juju.mysql-schema.connection.v1");

    [SupportedOSPlatform("windows")]
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        { return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [SupportedOSPlatform("windows")]
    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        var protectedBytes = Convert.FromBase64String(ciphertext);
        var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

}
