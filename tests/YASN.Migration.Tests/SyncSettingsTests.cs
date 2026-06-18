using YASN.Infrastructure.Settings;
using YASN.Infrastructure.Sync;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Serializes test classes that mutate the shared on-disk settings file. <see cref="SettingsStore"/>
    /// persists to a single local path, so two classes writing keys in parallel can clobber each other's
    /// values; this collection forces them to run sequentially.
    /// </summary>
    [CollectionDefinition("SharedSettingsFile", DisableParallelization = true)]
    public sealed class SharedSettingsFileCollection
    {
    }

    /// <summary>
    /// Covers the change-detection setting: its string parsing and the default applied when the key is
    /// absent or unrecognized. Guards against the persisted value depending on enum ordinals.
    /// </summary>
    [Collection("SharedSettingsFile")]
    public sealed class SyncSettingsTests
    {
        private const string Key = SyncSettings.ChangeDetectionKey;

        // Restore the key's prior value after each mutating test, matching SyncSettingsDeleteGateTests,
        // since the store writes through to a shared on-disk file.
        private static void WithStoredValue(string value, Action body)
        {
            SettingsStore store = new SettingsStore();
            string original = store.GetValue(Key, shouldSync: false, string.Empty);
            try
            {
                store.SetValue(Key, shouldSync: false, value);
                body();
            }
            finally
            {
                new SettingsStore().SetValue(Key, shouldSync: false, original);
            }
        }

        [Fact]
        public void ChangeDetectionDefaultsToETagWhenUnset()
        {
            SettingsStore store = new SettingsStore();
            store.SetValue(Key, shouldSync: false, string.Empty);
            Assert.Equal(ChangeDetectionMode.ETag, SyncSettings.ChangeDetection(store));
        }

        [Fact]
        public void ChangeDetectionReadsLastModified()
        {
            WithStoredValue(ChangeDetection.LastModifiedValue, () =>
                Assert.Equal(ChangeDetectionMode.LastModified, SyncSettings.ChangeDetection(new SettingsStore())));
        }

        [Fact]
        public void ChangeDetectionFallsBackToETagOnGarbage()
        {
            WithStoredValue("nonsense", () =>
                Assert.Equal(ChangeDetectionMode.ETag, SyncSettings.ChangeDetection(new SettingsStore())));
        }

        [Theory]
        [InlineData("etag", ChangeDetectionMode.ETag)]
        [InlineData("ETAG", ChangeDetectionMode.ETag)]
        [InlineData("lastModified", ChangeDetectionMode.LastModified)]
        [InlineData("LASTMODIFIED", ChangeDetectionMode.LastModified)]
        [InlineData(null, ChangeDetectionMode.ETag)]
        [InlineData("", ChangeDetectionMode.ETag)]
        public void ParseMapsValues(string? value, ChangeDetectionMode expected)
        {
            Assert.Equal(expected, ChangeDetection.Parse(value));
        }
    }
}
