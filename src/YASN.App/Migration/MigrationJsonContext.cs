using System.Text.Json;
using System.Text.Json.Serialization;

namespace YASN.Migration
{
    /// <summary>
    /// Source-generated read-side metadata for the legacy note index. Case-insensitive (so it binds
    /// both the old PascalCase WPF index and the new camelCase index) with lenient parsing, matching
    /// the previous reflection-based <c>ReadOptions</c>. Reflection-free for NativeAOT/trimming.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [JsonSerializable(typeof(LegacyIndex))]
    [JsonSerializable(typeof(List<LegacyNote>))]
    internal sealed partial class MigrationReadJsonContext : JsonSerializerContext
    {
    }

    /// <summary>
    /// Source-generated write-side metadata for the produced current-schema index. Indented to match
    /// the file the running app writes itself. Reflection-free for NativeAOT/trimming.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(NewIndex))]
    internal sealed partial class MigrationWriteJsonContext : JsonSerializerContext
    {
    }
}
