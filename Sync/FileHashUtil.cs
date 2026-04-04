using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using YASN.Logging;

namespace YASN.Sync
{
    public static class FileHashUtil
    {
        public static string? ComputeFileHash(string filePath)
        {
            try
            {
                using FileStream stream = File.OpenRead(filePath);
                return ComputeStreamHash(stream);
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to hash file '{filePath}': {ex.Message}");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to hash file '{filePath}': {ex.Message}");
                return null;
            }
            catch (SecurityException ex)
            {
                AppLogger.Debug($"Failed to hash file '{filePath}': {ex.Message}");
                return null;
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Debug($"Failed to hash file '{filePath}': {ex.Message}");
                return null;
            }
            catch (ArgumentException ex)
            {
                AppLogger.Debug($"Failed to hash file '{filePath}': {ex.Message}");
                return null;
            }
        }

        public static string? ComputeStreamHash(Stream stream)
        {
            try
            {
                using SHA256 sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(stream);
                return ToHex(hash);
            }
            catch (CryptographicException ex)
            {
                AppLogger.Debug($"Failed to hash stream: {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to hash stream: {ex.Message}");
                return null;
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Debug($"Failed to hash stream: {ex.Message}");
                return null;
            }
            catch (ObjectDisposedException ex)
            {
                AppLogger.Debug($"Failed to hash stream: {ex.Message}");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to hash stream: {ex.Message}");
                return null;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
