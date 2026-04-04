using System.IO;
using System.Text.Json;
using YASN.Logging;

namespace YASN.Sync
{
    /// <summary>
    /// Simple signature store that persists file hashes to disk for sync conflict detection.
    /// </summary>
    public class SignatureStore
    {
        private readonly string _path;
        private Dictionary<string, string> _signatures;

        public SignatureStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _signatures = LoadFromFile(path);
        }

        public string? Get(string fileName)
        {
            return fileName != null && _signatures.TryGetValue(fileName, out string? hash) ? hash : null;
        }

        public void Set(string fileName, string hash)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(hash))
            {
                return;
            }

            _signatures[fileName] = hash;
        }

        public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(_signatures);

        public void Save()
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(_signatures);
            File.WriteAllText(_path, json);
        }

        public static Dictionary<string, string> LoadFromFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                string json = File.ReadAllText(path);
                Dictionary<string, string>? data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return data != null
                    ? new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to load signature store from '{path}': {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException ex)
            {
                AppLogger.Warn($"Failed to load signature store from '{path}': {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to load signature store from '{path}': {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
