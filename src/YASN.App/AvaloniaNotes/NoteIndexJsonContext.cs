using System.Text.Json.Serialization;

namespace YASN.AvaloniaNotes
{
    /// <summary>
    /// Source-generated serialization metadata for the note index so <c>notes.index.json</c>
    /// round-trips without reflection under NativeAOT/trimming. Indented to preserve the existing
    /// on-disk file layout. Property-level converters (e.g. <see cref="FlexibleStringIdConverter"/>)
    /// declared on the model compose with the generated metadata.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(NoteIndexData))]
    internal sealed partial class NoteIndexJsonContext : JsonSerializerContext
    {
    }
}
