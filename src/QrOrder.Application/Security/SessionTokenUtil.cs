using System.Security.Cryptography;
using System.Text;

namespace QrOrder.Application.Security
{
    public static class SessionTokenUtil
    {
        public static string NewToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(58);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        public static string Sha256(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }
    }
}
