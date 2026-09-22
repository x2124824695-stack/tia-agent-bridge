using System.Security.Cryptography;
using System.Text;

namespace TiaAgent.Host.Safety;

public static class Hashing
{
    public static string Sha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
