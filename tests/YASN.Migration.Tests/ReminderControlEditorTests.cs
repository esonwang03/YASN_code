using YASN.Infrastructure.Reminders;

namespace YASN.Migration.Tests
{
    /// <summary>Verifies the in-place rewrite that auto-disables a fired fire-once reminder.</summary>
    public sealed class ReminderControlEditorTests
    {
        [Fact]
        public void DisablesOnceRuleByPrependingX()
        {
            string content = "[!Call][1]{0 10 * * *}{Ring back}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TryDisableOnce(content, ruleId, out string updated);

            Assert.True(changed);
            Assert.Equal("[!Call][X1]{0 10 * * *}{Ring back}", updated);
        }

        [Fact]
        public void PreservesSurroundingTextAndOtherRules()
        {
            string content = "a [!one][1]{* * * * *}{first} b [!two][]{0 9 * * *}{second} c";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            ReminderControlEditor.TryDisableOnce(content, ruleId, out string updated);

            Assert.Equal("a [!one][X1]{* * * * *}{first} b [!two][]{0 9 * * *}{second} c", updated);
        }

        [Fact]
        public void NoMatchReturnsFalseAndUnchanged()
        {
            string content = "[!Always][]{0 9 * * *}{recurring}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TryDisableOnce(content, ruleId, out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }

        [Fact]
        public void AlreadyDisabledOnceIsIdempotent()
        {
            string content = "[!Call][X1]{0 10 * * *}{Ring back}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TryDisableOnce(content, ruleId, out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }

        [Fact]
        public void UnknownRuleIdLeavesContentUnchanged()
        {
            string content = "[!Call][1]{0 10 * * *}{Ring back}";

            bool changed = ReminderControlEditor.TryDisableOnce(content, "deadbeef", out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }
    }
}
