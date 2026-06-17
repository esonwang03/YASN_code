using YASN.SingleNote;

namespace YASN.Migration.Tests
{
    /// <summary>Verifies the Markdig reminder badge rendering via the preview document pipeline.</summary>
    public sealed class ReminderRenderTests
    {
        private static string Render(string markdown) =>
            MarkdownPreviewDocument.Render(markdown, "style/default.css");

        [Fact]
        public void EnabledReminderRendersBadgeWithDisplayText()
        {
            string html = Render("[!Standup][]{0 0 9 * * 1-5}{Daily standup}");

            Assert.Contains("class=\"yasn-reminder\"", html, StringComparison.Ordinal);
            Assert.Contains("Standup", html, StringComparison.Ordinal);
            Assert.Contains("\U0001F514", html, StringComparison.Ordinal); // bell glyph
            Assert.DoesNotContain("yasn-reminder-disabled", html, StringComparison.Ordinal);
        }

        [Fact]
        public void DisabledReminderRendersDisabledClass()
        {
            string html = Render("[!Off][X]{* * * * *}{nope}");
            Assert.Contains("yasn-reminder-disabled", html, StringComparison.Ordinal);
        }

        [Fact]
        public void InvalidCronRendersInvalidClass()
        {
            string html = Render("[!Bad][]{not cron}{x}");
            Assert.Contains("yasn-reminder-invalid", html, StringComparison.Ordinal);
        }

        [Fact]
        public void TooltipDescribesScheduleAndContent()
        {
            string html = Render("[!Meds][]{0 9 * * *}{take meds}");
            Assert.Contains("title=\"Reminder (enabled): 0 9 * * *", html, StringComparison.Ordinal);
            Assert.Contains("take meds\"", html, StringComparison.Ordinal);
        }

        [Fact]
        public void DisplayTextSupportsInlineMarkdown()
        {
            string html = Render("[!**bold**][]{* * * * *}{c}");
            Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
        }

        [Fact]
        public void OnceReminderRendersOnceClassAndTooltip()
        {
            string html = Render("[!Call][1]{0 10 * * *}{Ring back}");
            Assert.Contains("yasn-reminder-once", html, StringComparison.Ordinal);
            Assert.Contains("title=\"Reminder (enabled, once): 0 10 * * *", html, StringComparison.Ordinal);
        }

        [Fact]
        public void SpentOnceReminderRendersDisabled()
        {
            string html = Render("[!Call][X1]{0 10 * * *}{Ring back}");
            Assert.Contains("yasn-reminder-disabled", html, StringComparison.Ordinal);
            Assert.Contains("yasn-reminder-once", html, StringComparison.Ordinal);
            Assert.Contains("Reminder (disabled, once)", html, StringComparison.Ordinal);
        }

        [Fact]
        public void OrdinaryLinksAreNotTreatedAsReminders()
        {
            string html = Render("[click here](https://example.com)");
            Assert.DoesNotContain("yasn-reminder", html, StringComparison.Ordinal);
            Assert.Contains("href=\"https://example.com\"", html, StringComparison.Ordinal);
        }
    }
}
