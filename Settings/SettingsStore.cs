using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YASN.Settings
{
    public class SettingsStore
    {
        private readonly string _syncPath = AppPaths.SyncSettingsPath;
        private readonly string _localPath = AppPaths.LocalSettingsPath;
        private readonly Dictionary<string, string> _syncSettings;
        private readonly Dictionary<string, string> _localSettings;

        public SettingsStore()
        {
            _syncSettings = LoadDictionary(_syncPath);
            _localSettings = LoadDictionary(_localPath);
        }

        public void ApplyValues(IEnumerable<SettingField> fields)
        {
            foreach (var field in fields)
            {
                var map = field.ShouldSync ? _syncSettings : _localSettings;
                if (!map.TryGetValue(field.Key, out var value))
                {
                    continue;
                }

                try
                {
                    switch (field.FieldType)
                    {
                        case SettingFieldType.Toggle:
                            if (bool.TryParse(value, out var boolValue))
                            {
                                field.BoolValue = boolValue;
                            }
                            break;
                        default:
                            field.Value = value;
                            break;
                    }
                }
                catch
                {
                    // Ignore malformed values and keep defaults
                }
            }
        }

        public void PersistField(SettingField field)
        {
            var map = field.ShouldSync ? _syncSettings : _localSettings;
            var path = field.ShouldSync ? _syncPath : _localPath;
            var value = field.FieldType == SettingFieldType.Toggle
                ? field.BoolValue.ToString()
                : field.Value ?? string.Empty;

            map[field.Key] = value;
            SaveDictionary(map, path);
        }

        public string GetValue(string key, bool shouldSync, string defaultValue = "")
        {
            var map = shouldSync ? _syncSettings : _localSettings;
            return map.GetValueOrDefault(key, defaultValue);
        }

        public bool ExportToFile(string path, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var payload = new SettingsExportPayload
                {
                    SchemaVersion = 1,
                    SyncSettings = new Dictionary<string, string>(_syncSettings),
                    LocalSettings = new Dictionary<string, string>(_localSettings)
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool ImportFromFile(string path, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                if (!File.Exists(path))
                {
                    errorMessage = "File not found.";
                    return false;
                }

                var json = File.ReadAllText(path);
                var payload = JsonSerializer.Deserialize<SettingsExportPayload>(json);
                if (payload == null)
                {
                    errorMessage = "Invalid settings file.";
                    return false;
                }

                _syncSettings.Clear();
                _localSettings.Clear();

                foreach (var kv in payload.SyncSettings)
                {
                    _syncSettings[kv.Key] = kv.Value ?? string.Empty;
                }

                foreach (var kv in payload.LocalSettings)
                {
                    _localSettings[kv.Key] = kv.Value ?? string.Empty;
                }

                SaveDictionary(_syncSettings, _syncPath);
                SaveDictionary(_localSettings, _localPath);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static Dictionary<string, string> LoadDictionary(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
            }
            catch
            {
                // Ignore corrupt files and start fresh
            }

            return new Dictionary<string, string>();
        }

        private static void SaveDictionary(Dictionary<string, string> data, string path)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Swallow IO errors to avoid crashing settings UI
            }
        }

        private sealed class SettingsExportPayload
        {
            [JsonPropertyName("schemaVersion")]
            public int SchemaVersion { get; set; } = 1;

            [JsonPropertyName("syncSettings")]
            public Dictionary<string, string> SyncSettings { get; set; } = new();

            [JsonPropertyName("localSettings")]
            public Dictionary<string, string> LocalSettings { get; set; } = new();
        }
    }
}
