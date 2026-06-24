using YASN.Cli;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the shared text helpers behind <c>note edit</c> and <c>note glance</c>. The append rule
    /// must produce identical bytes whether an edit runs against a live editor (open note) or the
    /// repository (closed note), so a program edit and a user edit never disagree by window state.
    /// </summary>
    public sealed class CliEditJoinTests
    {
        [Fact]
        public void AppendToEmptyReturnsAppendedOnly()
        {
            Assert.Equal("added", CliText.AppendContent(string.Empty, "added"));
        }

        [Fact]
        public void AppendInsertsSingleNewlineWhenMissing()
        {
            Assert.Equal("a\nb", CliText.AppendContent("a", "b"));
        }

        [Fact]
        public void AppendDoesNotDoubleNewline()
        {
            Assert.Equal("a\nb", CliText.AppendContent("a\n", "b"));
        }

        [Theory]
        [InlineData("1-3", 1, 3)]
        [InlineData("5", 5, 5)]
        public void ValidRangesParse(string text, int start, int end)
        {
            Assert.True(CliText.TryParseLineRange(text, out (int Start, int End) range));
            Assert.Equal(start, range.Start);
            Assert.Equal(end, range.End);
        }

        [Theory]
        [InlineData("0-3")]
        [InlineData("4-2")]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("-2")]
        public void InvalidRangesRejected(string text)
        {
            Assert.False(CliText.TryParseLineRange(text, out _));
        }

        [Fact]
        public void SplitLinesHandlesMixedNewlines()
        {
            string[] lines = CliText.SplitLines("a\r\nb\nc\rd");

            Assert.Equal(["a", "b", "c", "d"], lines);
        }
    }
}
