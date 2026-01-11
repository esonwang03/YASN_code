using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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

        public string Get(string fileName)
        {
            return fileName != null && _signatures.TryGetValue(fileName, out var hash) ? hash : null;
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
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_signatures);
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

                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return data != null
                    ? new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
