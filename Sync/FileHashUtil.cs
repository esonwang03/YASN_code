using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace YASN.Sync
{
    public static class FileHashUtil
    {
        public static string ComputeFileHash(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                return ComputeStreamHash(stream);
            }
            catch
            {
                return null;
            }
        }

        public static string ComputeStreamHash(Stream stream)
        {
            try
            {
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(stream);
                return ToHex(hash);
            }
            catch
            {
                return null;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
