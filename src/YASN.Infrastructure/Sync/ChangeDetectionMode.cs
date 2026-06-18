namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// How a sync pass decides whether a remote note changed since the last pass. The validator token
    /// stored in the baseline is produced from one of these sources; see <see cref="ChangeDetection"/>
    /// for the string keys persisted in settings.
    /// </summary>
    public enum ChangeDetectionMode
    {
        /// <summary>Compare HTTP ETags. Accurate, but some WebDAV servers omit them.</summary>
        ETag,

        /// <summary>Compare Last-Modified timestamps. The fallback for servers without ETags.</summary>
        LastModified
    }

    /// <summary>
    /// Setting values and parsing for <see cref="ChangeDetectionMode"/>. Stored as a stable string so
    /// the persisted config does not depend on enum ordinals.
    /// </summary>
    public static class ChangeDetection
    {
        /// <summary>Persisted value selecting <see cref="ChangeDetectionMode.ETag"/>.</summary>
        public const string ETagValue = "etag";

        /// <summary>Persisted value selecting <see cref="ChangeDetectionMode.LastModified"/>.</summary>
        public const string LastModifiedValue = "lastModified";

        /// <summary>Parses a persisted value, defaulting to <see cref="ChangeDetectionMode.ETag"/>.</summary>
        public static ChangeDetectionMode Parse(string? value) =>
            string.Equals(value, LastModifiedValue, StringComparison.OrdinalIgnoreCase)
                ? ChangeDetectionMode.LastModified
                : ChangeDetectionMode.ETag;
    }
}
