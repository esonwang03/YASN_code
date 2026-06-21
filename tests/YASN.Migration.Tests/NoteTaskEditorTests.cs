using YASN.Infrastructure.Markdown;

namespace YASN.Migration.Tests
{
    /// <summary>Verifies the in-place rewrite that toggles a Markdown task-list checkbox by line.</summary>
    public sealed class NoteTaskEditorTests
    {
        [Fact]
        public void ChecksAnUncheckedItem()
        {
            string content = "# Todo\n- [ ] buy milk\n- [ ] walk dog";

            bool changed = NoteTaskEditor.TrySetChecked(content, 1, isChecked: true, out string updated);

            Assert.True(changed);
            Assert.Equal("# Todo\n- [x] buy milk\n- [ ] walk dog", updated);
        }

        [Fact]
        public void UnchecksACheckedItem()
        {
            string content = "- [x] done";

            bool changed = NoteTaskEditor.TrySetChecked(content, 0, isChecked: false, out string updated);

            Assert.True(changed);
            Assert.Equal("- [ ] done", updated);
        }

        [Fact]
        public void PreservesIndentationBulletAndCrlf()
        {
            string content = "intro\r\n  * [ ] nested item\r\ntail";

            bool changed = NoteTaskEditor.TrySetChecked(content, 1, isChecked: true, out string updated);

            Assert.True(changed);
            Assert.Equal("intro\r\n  * [x] nested item\r\ntail", updated);
        }

        [Fact]
        public void AlreadyInTargetStateIsNoOp()
        {
            string content = "- [x] done";

            bool changed = NoteTaskEditor.TrySetChecked(content, 0, isChecked: true, out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }

        [Fact]
        public void NonTaskLineIsNoOp()
        {
            string content = "# Heading\njust a paragraph";

            bool changed = NoteTaskEditor.TrySetChecked(content, 1, isChecked: true, out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }

        [Fact]
        public void OutOfRangeLineIsNoOp()
        {
            string content = "- [ ] only line";

            bool changed = NoteTaskEditor.TrySetChecked(content, 5, isChecked: true, out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }
    }
}
