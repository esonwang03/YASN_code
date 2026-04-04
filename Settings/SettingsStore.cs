using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using YASN.Logging;

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
            foreach (SettingField field in fields)
            {
                Dictionary<string, string> map = field.ShouldSync ? _syncSettings : _localSettings;
                if (!map.TryGetValue(field.Key, out string? value))
                {
                    continue;
                }

                try
                {
                    switch (field.FieldType)
                    {
                        case SettingFieldType.Toggle:
                            if (bool.TryParse(value, out bool boolValue))
                            {
                                field.BoolValue = boolValue;
                            }
                            break;
                        default:
                            field.Value = value;
                            break;
                    }
                }
                catch (FormatException ex)
                {
                    AppLogger.Debug($"Failed to apply setting '{field.Key}': {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    AppLogger.Debug($"Failed to apply setting '{field.Key}': {ex.Message}");
                }
            }
        }

        public void PersistField(SettingField field)
        {
            Dictionary<string, string> map = field.ShouldSync ? _syncSettings : _localSettings;
            string path = field.ShouldSync ? _syncPath : _localPath;
            string value = field.FieldType == SettingFieldType.Toggle
                ? field.BoolValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : field.Value ?? string.Empty;

            map[field.Key] = value;
            SaveDictionary(map, path);
        }

        public string GetValue(string key, bool shouldSync, string defaultValue = "")
        {
            Dictionary<string, string> map = shouldSync ? _syncSettings : _localSettings;
            return map.GetValueOrDefault(key, defaultValue);
        }

        public bool ExportToFile(string path, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                SettingsExportPayload payload = new SettingsExportPayload
                {
                    SchemaVersion = 1,
                    SyncSettings = new Dictionary<string, string>(_syncSettings),
                    LocalSettings = new Dictionary<string, string>(_localSettings)
                };

                string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                return true;
            }
            catch (IOException ex)
            {
                errorMessage = ex.Message;
                AppLogger.Warn($"Failed to export settings to '{path}': {ex.Message}");
                return false;
            }
            catch (JsonException ex)
            {
                errorMessage = ex.Message;
                AppLogger.Warn($"Failed to export settings to '{path}': {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                errorMessage = ex.Message;
                AppLogger.Warn($"Failed to export settings to '{path}': {ex.Message}");
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

                string json = File.ReadAllText(path);
                SettingsExportPayload? payload = JsonSerializer.Deserialize<SettingsExportPayload>(json);
                if (payload == null)
                {
                    errorMessage = "Invalid settings file.";
                    return false;
                }

                _syncSettings.Clear();
                _localSettings.Clear();

                foreach (KeyValuePair<string, string> kv in payload.SyncSettings)
                {
                    _syncSettings[kv.Key] = kv.Value ?? string.Empty;
                }

                foreach (KeyValuePair<string, string> kv in payload.LocalSettings)
                {
                    _localSettings[kv.Key] = kv.Value ?? string.Empty;
                }

                SaveDictionary(_syncSettings, _syncPath);
                SaveDictionary(_localSettings, _localPath);
                return true;
            }
            catch (IOException ex)
            {
                errorMessage = ex.Message;
                AppLogger.Warn($"Failed to import settings from '{path}': {ex.Message}");
                return false;
            }
            catch (JsonException ex)
            {
                errorMessage = ex.Message;
                AppLogger.Warn($"Failed to import settings from '{path}': {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                errorMessage = ex.Message;
                AppLogger.Warn($"Failed to import settings from '{path}': {ex.Message}");
                return false;
            }
        }

        private static Dictionary<string, string> LoadDictionary(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to load settings dictionary from '{path}': {ex.Message}");
            }
            catch (JsonException ex)
            {
                AppLogger.Debug($"Failed to load settings dictionary from '{path}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to load settings dictionary from '{path}': {ex.Message}");
            }

            return new Dictionary<string, string>();
        }

        private static void SaveDictionary(Dictionary<string, string> data, string path)
        {
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to save settings dictionary to '{path}': {ex.Message}");
            }
            catch (JsonException ex)
            {
                AppLogger.Warn($"Failed to save settings dictionary to '{path}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to save settings dictionary to '{path}': {ex.Message}");
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
