using YASN.Infrastructure.Sync;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the wire document round-trips and that the content hash ignores volatile timing.
    /// </summary>
    public sealed class NoteWireSerializerTests
    {
        /// <summary>
        /// A document round-trips through serialize/deserialize.
        /// </summary>
        [Fact]
        public void RoundTripsDocument()
        {
            SyncNoteDocument doc = new SyncNoteDocument
            {
                SyncKey = "k1",
                Title = "Hello",
                Content = "# Body",
                Width = 400,
                Deleted = false
            };

            SyncNoteDocument? parsed = NoteWireSerializer.Deserialize(NoteWireSerializer.Serialize(doc));

            Assert.NotNull(parsed);
            Assert.Equal("k1", parsed!.SyncKey);
            Assert.Equal("Hello", parsed.Title);
            Assert.Equal("# Body", parsed.Content);
            Assert.Equal(400, parsed.Width);
        }

        /// <summary>
        /// The content hash is stable across differing modified timestamps.
        /// </summary>
        [Fact]
        public void ContentHashIgnoresModifiedTime()
        {
            SyncNoteDocument a = new SyncNoteDocument { SyncKey = "k", Content = "same", ModifiedAtUtc = DateTimeOffset.UnixEpoch };
            SyncNoteDocument b = new SyncNoteDocument { SyncKey = "k", Content = "same", ModifiedAtUtc = DateTimeOffset.UnixEpoch.AddHours(5) };

            Assert.Equal(NoteWireSerializer.ComputeContentHash(a), NoteWireSerializer.ComputeContentHash(b));
        }

        /// <summary>
        /// Different content produces different hashes.
        /// </summary>
        [Fact]
        public void ContentHashDiffersOnContent()
        {
            SyncNoteDocument a = new SyncNoteDocument { SyncKey = "k", Content = "one" };
            SyncNoteDocument b = new SyncNoteDocument { SyncKey = "k", Content = "two" };

            Assert.NotEqual(NoteWireSerializer.ComputeContentHash(a), NoteWireSerializer.ComputeContentHash(b));
        }

        /// <summary>
        /// Empty or invalid payloads deserialize to null.
        /// </summary>
        [Fact]
        public void DeserializeReturnsNullOnEmpty()
        {
            Assert.Null(NoteWireSerializer.Deserialize(Array.Empty<byte>()));
            Assert.Null(NoteWireSerializer.Deserialize(new byte[] { 0x7b, 0x7b }));
        }
    }
}
