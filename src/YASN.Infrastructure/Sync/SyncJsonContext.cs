using System.Text.Json.Serialization;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Source-generated serialization metadata for the on-the-wire sync document so it round-trips
    /// without reflection under NativeAOT/trimming. Indented to match the uploaded file layout; the
    /// content-hash path serializes compactly via a separate options instance built over this
    /// context's resolver (see <see cref="NoteWireSerializer"/>).
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(SyncNoteDocument))]
    public partial class SyncJsonContext : JsonSerializerContext
    {
    }
}
