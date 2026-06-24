using System.Text.RegularExpressions;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Extracts the <c>note-assets/…</c> relative paths a note's Markdown references (pasted images and
    /// copied attachments). Asset files are written by <c>NoteAssetInserter</c> with GUID file names, so
    /// a relative path uniquely and immutably identifies one blob: the same path always denotes the same
    /// bytes on every device. That is what lets asset sync mirror by path rather than re-key per device
    /// (note ids differ across replicas, but the path embedded in the synced content does not).
    /// </summary>
    public static partial class AssetReferenceScanner
    {
        /// <summary>
        /// Returns the distinct <c>note-assets/…</c> relative paths referenced anywhere in the supplied
        /// note contents, normalized to forward slashes. Paths are matched inside Markdown image/link
        /// targets as well as bare text, so both <c>![alt](note-assets/…)</c> and
        /// <c>[name](note-assets/attachments/…)</c> forms are found.
        /// </summary>
        /// <param name="contents">The note Markdown bodies to scan.</param>
        /// <returns>The distinct referenced asset paths, each relative to the data directory.</returns>
        public static IReadOnlyCollection<string> Collect(IEnumerable<string?> contents)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? content in contents)
            {
                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                foreach (Match match in AssetPathRegex().Matches(content))
                {
                    paths.Add(match.Value.Replace('\\', '/'));
                }
            }

            return paths;
        }

        // Matches a note-assets relative path up to the first character that cannot belong to one: the
        // closing paren of a Markdown target, quotes, whitespace, or angle brackets. Asset file names are
        // GUID + extension, so the allowed run is letters, digits, and the few path/url-safe punctuation
        // characters those produce. Anchored at "note-assets/" so absolute or external links are ignored.
        [GeneratedRegex(@"note-assets/[A-Za-z0-9_./\-]+", RegexOptions.IgnoreCase)]
        private static partial Regex AssetPathRegex();
    }
}
