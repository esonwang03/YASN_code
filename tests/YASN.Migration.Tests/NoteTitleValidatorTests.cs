using YASN.AvaloniaNotes;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies note title validation: trimming, empty rejection, and case-insensitive uniqueness.
    /// </summary>
    public sealed class NoteTitleValidatorTests
    {
        /// <summary>
        /// A trimmed, unique title is accepted and normalized.
        /// </summary>
        [Fact]
        public void AcceptsUniqueTrimmedTitle()
        {
            bool ok = NoteTitleValidator.TryValidate(
                "  Shopping list  ",
                new[] { "Ideas", "Todo" },
                out string normalized,
                out string? errorKey);

            Assert.True(ok);
            Assert.Null(errorKey);
            Assert.Equal("Shopping list", normalized);
        }

        /// <summary>
        /// An empty or whitespace title is rejected.
        /// </summary>
        [Fact]
        public void RejectsEmptyTitle()
        {
            bool ok = NoteTitleValidator.TryValidate(
                "   ",
                Array.Empty<string>(),
                out _,
                out string? errorKey);

            Assert.False(ok);
            Assert.Equal(NoteTitleValidator.EmptyErrorKey, errorKey);
        }

        /// <summary>
        /// A title duplicating an existing one (ignoring case) is rejected.
        /// </summary>
        [Fact]
        public void RejectsCaseInsensitiveDuplicate()
        {
            bool ok = NoteTitleValidator.TryValidate(
                "todo",
                new[] { "Ideas", "TODO" },
                out _,
                out string? errorKey);

            Assert.False(ok);
            Assert.Equal(NoteTitleValidator.DuplicateErrorKey, errorKey);
        }
    }
}
