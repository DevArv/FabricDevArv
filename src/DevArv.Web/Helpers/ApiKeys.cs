using System.Security.Cryptography;
using System.Text;

namespace DevArv.Web.Helpers;

public static class ApiKeys
{
    public static string Generate() => "dvw_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public static string Hash(string Key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key.Trim()))).ToLowerInvariant();
}
