using YASN.MarkdownEditing;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the minimal prefix/suffix diff that turns a whole-string content transform into one
    /// in-place document edit, so programmatic edits stay undoable and caret-preserving.
    /// </summary>
    public sealed class MarkdownTextDiffTests
    {
        [Fact]
        public void IdenticalStringsProduceNoOp()
        {
            MarkdownTextSplice splice = MarkdownTextDiff.Compute("same text", "same text");

            Assert.True(splice.IsNoOp);
            Assert.Equal(0, splice.RemovedLength);
            Assert.Equal(string.Empty, splice.InsertedText);
        }

        [Fact]
        public void PureInsertionInTheMiddle()
        {
            // "ac" -> "abc": insert "b" at offset 1, remove nothing.
            MarkdownTextSplice splice = MarkdownTextDiff.Compute("ac", "abc");

            Assert.Equal(1, splice.Offset);
            Assert.Equal(0, splice.RemovedLength);
            Assert.Equal("b", splice.InsertedText);
        }

        [Fact]
        public void PureDeletionInTheMiddle()
        {
            // "abc" -> "ac": remove "b" at offset 1, insert nothing.
            MarkdownTextSplice splice = MarkdownTextDiff.Compute("abc", "ac");

            Assert.Equal(1, splice.Offset);
            Assert.Equal(1, splice.RemovedLength);
            Assert.Equal(string.Empty, splice.InsertedText);
        }

        [Fact]
        public void MidReplacementSpansOnlyChangedRegion()
        {
            // The checkbox-toggle case: only the marker char changes.
            MarkdownTextSplice splice = MarkdownTextDiff.Compute("- [ ] task", "- [x] task");

            Assert.Equal(3, splice.Offset);
            Assert.Equal(1, splice.RemovedLength);
            Assert.Equal("x", splice.InsertedText);
        }

        [Fact]
        public void PrefixOnlyChange()
        {
            // Append at the end: shared prefix is the whole old string.
            MarkdownTextSplice splice = MarkdownTextDiff.Compute("hello", "hello world");

            Assert.Equal(5, splice.Offset);
            Assert.Equal(0, splice.RemovedLength);
            Assert.Equal(" world", splice.InsertedText);
        }

        [Fact]
        public void SuffixOnlyChange()
        {
            // Insert at the start: shared suffix is the whole old string.
            MarkdownTextSplice splice = MarkdownTextDiff.Compute("world", "hello world");

            Assert.Equal(0, splice.Offset);
            Assert.Equal(0, splice.RemovedLength);
            Assert.Equal("hello ", splice.InsertedText);
        }

        [Fact]
        public void FromEmptyToContent()
        {
            MarkdownTextSplice splice = MarkdownTextDiff.Compute(string.Empty, "new");

            Assert.Equal(0, splice.Offset);
            Assert.Equal(0, splice.RemovedLength);
            Assert.Equal("new", splice.InsertedText);
        }

        [Fact]
        public void FromContentToEmpty()
        {
            MarkdownTextSplice splice = MarkdownTextDiff.Compute("gone", string.Empty);

            Assert.Equal(0, splice.Offset);
            Assert.Equal(4, splice.RemovedLength);
            Assert.Equal(string.Empty, splice.InsertedText);
        }

        [Fact]
        public void MultiLineEditTouchesOnlyOneLine()
        {
            string oldText = "line one\nline two\nline three";
            string newText = "line one\nline TWO\nline three";

            MarkdownTextSplice splice = MarkdownTextDiff.Compute(oldText, newText);

            // Applying the splice reproduces newText, and it spans only the changed run.
            string applied = string.Concat(
                oldText.AsSpan(0, splice.Offset),
                splice.InsertedText,
                oldText.AsSpan(splice.Offset + splice.RemovedLength));
            Assert.Equal(newText, applied);
            Assert.Equal("TWO", splice.InsertedText);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("abc", "abc")]
        [InlineData("abc", "xyz")]
        [InlineData("a repeated repeated tail", "a repeated tail")]
        [InlineData("aaaa", "aa")]
        [InlineData("aa", "aaaa")]
        public void ApplyingSpliceAlwaysReproducesNewText(string oldText, string newText)
        {
            MarkdownTextSplice splice = MarkdownTextDiff.Compute(oldText, newText);

            string applied = string.Concat(
                oldText.AsSpan(0, splice.Offset),
                splice.InsertedText,
                oldText.AsSpan(splice.Offset + splice.RemovedLength));
            Assert.Equal(newText, applied);
        }
    }
}
