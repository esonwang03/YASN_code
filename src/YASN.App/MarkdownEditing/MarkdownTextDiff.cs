namespace YASN.MarkdownEditing
{
    /// <summary>
    /// The minimal single-span edit that turns one string into another: replace
    /// <see cref="RemovedLength"/> characters at <see cref="Offset"/> with <see cref="InsertedText"/>.
    /// Computed by trimming the common prefix and common suffix the two strings share, so a whole-string
    /// transform can be applied to an AvaloniaEdit document as one <c>Replace</c> over just the changed
    /// region — preserving the undo stack and any caret/selection outside that region.
    /// </summary>
    /// <param name="Offset">The zero-based offset where the changed span starts.</param>
    /// <param name="RemovedLength">The number of characters removed from the old string at <paramref name="Offset"/>.</param>
    /// <param name="InsertedText">The text inserted in their place.</param>
    public readonly record struct MarkdownTextSplice(int Offset, int RemovedLength, string InsertedText)
    {
        /// <summary>
        /// Whether this splice changes anything. A no-op splice (identical strings) removes nothing and
        /// inserts nothing, so callers can skip issuing a document edit.
        /// </summary>
        public bool IsNoOp => RemovedLength == 0 && InsertedText.Length == 0;
    }

    /// <summary>
    /// Computes the minimal <see cref="MarkdownTextSplice"/> between two strings by common prefix/suffix
    /// diffing. Used to apply a whole-string content transform (checklist toggle, reminder edit, file
    /// insert) as a single in-place document replace instead of a full-text reset.
    /// </summary>
    public static class MarkdownTextDiff
    {
        /// <summary>
        /// Returns the single contiguous edit that transforms <paramref name="oldText"/> into
        /// <paramref name="newText"/>. Skips the longest shared prefix and the longest shared suffix
        /// (without overlapping the prefix), so the result spans only the region that actually differs.
        /// Identical inputs yield a no-op splice at offset 0.
        /// </summary>
        /// <param name="oldText">The current text.</param>
        /// <param name="newText">The desired text.</param>
        /// <returns>The minimal splice from old to new.</returns>
        public static MarkdownTextSplice Compute(string oldText, string newText)
        {
            oldText ??= string.Empty;
            newText ??= string.Empty;

            int oldLength = oldText.Length;
            int newLength = newText.Length;
            int max = Math.Min(oldLength, newLength);

            // Longest common prefix.
            int prefix = 0;
            while (prefix < max && oldText[prefix] == newText[prefix])
            {
                prefix++;
            }

            // Longest common suffix that does not overlap the matched prefix in either string.
            int suffix = 0;
            int suffixLimit = max - prefix;
            while (suffix < suffixLimit
                && oldText[oldLength - 1 - suffix] == newText[newLength - 1 - suffix])
            {
                suffix++;
            }

            int removedLength = oldLength - prefix - suffix;
            string insertedText = newText.Substring(prefix, newLength - prefix - suffix);
            return new MarkdownTextSplice(prefix, removedLength, insertedText);
        }
    }
}
