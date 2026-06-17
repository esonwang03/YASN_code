using YASN.AvaloniaNotes;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies note title derivation from Markdown content, including reminder-token reduction so a
    /// note that begins with a reminder yields a clean title (used in reminder notifications).
    /// </summary>
    public sealed class NoteTitleFormatterTests
    {
        [Fact]
        public void EmptyContentIsUntitled()
        {
            Assert.Equal("Untitled note", NoteTitleFormatter.GetTitle(""));
            Assert.Equal("Untitled note", NoteTitleFormatter.GetTitle(null));
        }

        [Fact]
        public void UsesFirstMeaningfulLineStrippingHeadingMarks()
        {
            Assert.Equal("Groceries", NoteTitleFormatter.GetTitle("# Groceries\n\n- milk"));
        }

        [Fact]
        public void ReminderTokenReducesToDisplayText()
        {
            string title = NoteTitleFormatter.GetTitle("[!Standup][]{0 0 9 * * 1-5}{Daily standup}");
            Assert.Equal("Standup", title);
        }

        [Fact]
        public void ReminderTokenInlineWithOtherTextKeepsBoth()
        {
            string title = NoteTitleFormatter.GetTitle("Morning [!Standup][]{0 9 * * *}{ping} routine");
            Assert.Equal("Morning Standup routine", title);
        }

        [Fact]
        public void DisabledReminderStillReducesToDisplayText()
        {
            string title = NoteTitleFormatter.GetTitle("[!Meds][X]{0 9 * * *}{take meds}");
            Assert.Equal("Meds", title);
        }

        [Fact]
        public void OrdinaryFirstLineWithLinkIsUnaffected()
        {
            string title = NoteTitleFormatter.GetTitle("See [docs](https://example.com)");
            Assert.Equal("See [docs](https://example.com)", title);
        }
    }
}
