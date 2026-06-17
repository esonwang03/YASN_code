namespace YASN.AvaloniaNotes
{
    /// <summary>
    /// Validates a proposed note title: non-empty after trimming and unique (case-insensitive)
    /// among existing titles. Pure logic with no UI dependency so it can be unit tested.
    /// </summary>
    public static class NoteTitleValidator
    {
        /// <summary>
        /// The localization key returned when the proposed title is empty.
        /// </summary>
        public const string EmptyErrorKey = "Rename.Empty";

        /// <summary>
        /// The localization key returned when the proposed title duplicates an existing one.
        /// </summary>
        public const string DuplicateErrorKey = "Rename.Duplicate";

        /// <summary>
        /// Validates a proposed title against the set of existing titles.
        /// </summary>
        /// <param name="proposed">The proposed title (untrimmed).</param>
        /// <param name="existingTitles">The titles to check uniqueness against (excluding self).</param>
        /// <param name="normalized">The trimmed title when valid; otherwise an empty string.</param>
        /// <param name="errorKey">The localization key describing the failure; otherwise null.</param>
        /// <returns><see langword="true"/> when the proposed title is valid.</returns>
        public static bool TryValidate(
            string? proposed,
            IEnumerable<string> existingTitles,
            out string normalized,
            out string? errorKey)
        {
            normalized = (proposed ?? string.Empty).Trim();

            if (normalized.Length == 0)
            {
                errorKey = EmptyErrorKey;
                return false;
            }

            foreach (string existing in existingTitles)
            {
                if (string.Equals(existing?.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    errorKey = DuplicateErrorKey;
                    return false;
                }
            }

            errorKey = null;
            return true;
        }
    }
}
