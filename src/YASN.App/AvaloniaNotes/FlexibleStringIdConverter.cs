using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YASN.AvaloniaNotes
{
    /// <summary>
    /// Reads a note id stored as either a JSON string (schema v6+, GUID) or a JSON number (legacy
    /// schemas, integer handle), always surfacing it as a string. This lets the index load even when a
    /// pre-v6 file has not yet been normalized by the schema migrator; the repository's backfill then
    /// settles the id and sync key.
    /// </summary>
    internal sealed class FlexibleStringIdConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? string.Empty,
                JsonTokenType.Number => reader.TryGetInt64(out long value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.Null => string.Empty,
                _ => string.Empty
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
