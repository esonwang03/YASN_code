using YASN.Infrastructure.Reminders;

namespace YASN.Migration.Tests
{
    /// <summary>Verifies the in-place rewrite that counts down a fired finite reminder.</summary>
    public sealed class ReminderControlEditorTests
    {
        [Fact]
        public void DisablesOnceRuleByPrependingX()
        {
            string content = "[!Call][1]{0 10 * * *}{Ring back}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TryReduceCounter(content, ruleId, out string updated);

            Assert.True(changed);
            Assert.Equal("[!Call][X1]{0 10 * * *}{Ring back}", updated);
        }

        [Fact]
        public void DecrementsCountAboveOne()
        {
            string content = "[!Call][3]{0 10 * * *}{Ring back}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TryReduceCounter(content, ruleId, out string updated);

            Assert.True(changed);
            Assert.Equal("[!Call][2]{0 10 * * *}{Ring back}", updated);
        }

        [Fact]
        public void CountReachingOneBecomesSpentOnNextReduce()
        {
            string content = "[!Call][2]{0 10 * * *}{Ring back}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            ReminderControlEditor.TryReduceCounter(content, ruleId, out string once);
            Assert.Equal("[!Call][1]{0 10 * * *}{Ring back}", once);

            string nextRuleId = NoteReminderParser.Parse(once)[0].RuleId;
            ReminderControlEditor.TryReduceCounter(once, nextRuleId, out string twice);
            Assert.Equal("[!Call][X1]{0 10 * * *}{Ring back}", twice);
        }

        [Fact]
        public void PreservesSurroundingTextAndOtherRules()
        {
            string content = "a [!one][1]{* * * * *}{first} b [!two][]{0 9 * * *}{second} c";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            ReminderControlEditor.TryReduceCounter(content, ruleId, out string updated);

            Assert.Equal("a [!one][X1]{* * * * *}{first} b [!two][]{0 9 * * *}{second} c", updated);
        }

        [Fact]
        public void RecurringRuleReturnsFalseAndUnchanged()
        {
            string content = "[!Always][]{0 9 * * *}{recurring}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TryReduceCounter(content, ruleId, out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }

        [Fact]
        public void AlreadyDisabledOnceIsIdempotent()
        {
            string content = "[!Call][X1]{0 10 * * *}{Ring back}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TryReduceCounter(content, ruleId, out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }

        [Fact]
        public void UnknownRuleIdLeavesContentUnchanged()
        {
            string content = "[!Call][1]{0 10 * * *}{Ring back}";

            bool changed = ReminderControlEditor.TryReduceCounter(content, "deadbeef", out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }

        [Fact]
        public void DisablesEnabledRule()
        {
            string content = "[!Call][]{0 10 * * *}{Ring back}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TrySetEnabled(content, ruleId, enabled: false, out string updated);

            Assert.True(changed);
            Assert.Equal("[!Call][X]{0 10 * * *}{Ring back}", updated);
        }

        [Fact]
        public void EnablesDisabledRulePreservingCount()
        {
            string content = "[!Call][X3]{0 10 * * *}{Ring back}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TrySetEnabled(content, ruleId, enabled: true, out string updated);

            Assert.True(changed);
            Assert.Equal("[!Call][3]{0 10 * * *}{Ring back}", updated);
        }

        [Fact]
        public void SetEnabledToCurrentStateIsNoOp()
        {
            string content = "[!Call][]{0 10 * * *}{Ring back}";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TrySetEnabled(content, ruleId, enabled: true, out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }

        [Fact]
        public void DeletesRuleAndAbsorbsOneTrailingSpace()
        {
            string content = "a [!one][]{* * * * *}{first} b";
            string ruleId = NoteReminderParser.Parse(content)[0].RuleId;

            bool changed = ReminderControlEditor.TryDelete(content, ruleId, out string updated);

            Assert.True(changed);
            Assert.Equal("a b", updated);
        }

        [Fact]
        public void DeleteUnknownRuleIsNoOp()
        {
            string content = "[!Call][]{0 10 * * *}{Ring back}";

            bool changed = ReminderControlEditor.TryDelete(content, "deadbeef", out string updated);

            Assert.False(changed);
            Assert.Equal(content, updated);
        }
    }
}
