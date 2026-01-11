using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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

        private Dictionary<string, string> LoadDictionary(string path)
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

        private void SaveDictionary(Dictionary<string, string> data, string path)
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
    }
}
