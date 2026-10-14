using System.Security.Cryptography;
using System.Text;
using Application.Interfaces.V1;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// accounts.py hash_password / check_password: PBKDF2-HMAC-SHA256, 200 000 iterations, 16-byte salt,
    /// stored as base64(salt) + "$" + base64(dk). Hashes written by the Python tool verify unchanged.
    /// </summary>
    public sealed class PasswordHasher : IPasswordHasher
    {
        public const int Iterations = 200_000;

        public string Hash(string password) => Hash(password, RandomNumberGenerator.GetBytes(16));

        public static string Hash(string password, byte[] salt)
        {
            var dk = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
            return Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(dk);
        }

        public bool Verify(string password, string? stored)
        {
            try
            {
                if (string.IsNullOrEmpty(stored)) return false;
                var i = stored.IndexOf('$');
                if (i < 0) return false;
                var salt = Convert.FromBase64String(stored[..i]);
                var h = stored[(i + 1)..];
                var dk = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Convert.ToBase64String(dk)), Encoding.ASCII.GetBytes(h));
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
