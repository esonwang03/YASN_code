using System.Text.Json.Serialization;

namespace YASN.Infrastructure
{
    /// <summary>
    /// Source-generated serialization metadata for the small, schema-free JSON shapes used across the
    /// infrastructure layer: the string/string settings and signature maps, and the reminder
    /// last-fired map. Lets these round-trip without reflection under NativeAOT/trimming.
    /// </summary>
    /// <remarks>
    /// Indented output, matching the previous behavior of the user-facing settings files. The
    /// signature and reminder-state files (previously compact) are internal and regenerated, so the
    /// cosmetic whitespace change is immaterial.
    /// </remarks>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(Dictionary<string, DateTimeOffset>))]
    public partial class InfrastructureJsonContext : JsonSerializerContext
    {
    }
}
