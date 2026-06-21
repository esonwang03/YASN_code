using YASN.Infrastructure.Reminders;

namespace YASN.Migration.Tests
{
    /// <summary>Verifies extraction of reminder rules from note Markdown content.</summary>
    public sealed class NoteReminderParserTests
    {
        [Fact]
        public void ParsesEnabledRule()
        {
            IReadOnlyList<NoteReminderRule> rules = NoteReminderParser.Parse("[!Standup][]{0 0 9 * * 1-5}{Daily standup}");

            NoteReminderRule rule = Assert.Single(rules);
            Assert.Equal("Standup", rule.DisplayText);
            Assert.True(rule.Enabled);
            Assert.Equal("0 0 9 * * 1-5", rule.CronText);
            Assert.Equal("Daily standup", rule.Content);
            Assert.True(rule.IsSchedulable);
        }

        [Theory]
        [InlineData("[!t][X]{* * * * *}{c}", false)]
        [InlineData("[!t][x]{* * * * *}{c}", false)]
        [InlineData("[!t][]{* * * * *}{c}", true)]
        [InlineData("[!t][on]{* * * * *}{c}", true)]
        [InlineData("[!t][1]{* * * * *}{c}", true)]
        [InlineData("[!t][X1]{* * * * *}{c}", false)]
        public void ControlSegmentTogglesEnabled(string content, bool expectedEnabled)
        {
            NoteReminderRule rule = Assert.Single(NoteReminderParser.Parse(content));
            Assert.Equal(expectedEnabled, rule.Enabled);
        }

        [Theory]
        [InlineData("[!t][]{* * * * *}{c}", false)]
        [InlineData("[!t][on]{* * * * *}{c}", false)]
        [InlineData("[!t][X]{* * * * *}{c}", false)]
        [InlineData("[!t][1]{* * * * *}{c}", true)]
        [InlineData("[!t][X1]{* * * * *}{c}", true)]
        public void ControlSegmentMarksOnce(string content, bool expectedOnce)
        {
            NoteReminderRule rule = Assert.Single(NoteReminderParser.Parse(content));
            Assert.Equal(expectedOnce, rule.Once);
        }

        [Theory]
        [InlineData("[!t][]{* * * * *}{c}", null)]
        [InlineData("[!t][on]{* * * * *}{c}", null)]
        [InlineData("[!t][1]{* * * * *}{c}", 1)]
        [InlineData("[!t][3]{* * * * *}{c}", 3)]
        [InlineData("[!t][X1]{* * * * *}{c}", 1)]
        [InlineData("[!t][12]{* * * * *}{c}", 12)]
        public void ControlSegmentParsesRemainingCount(string content, int? expectedCount)
        {
            NoteReminderRule rule = Assert.Single(NoteReminderParser.Parse(content));
            Assert.Equal(expectedCount, rule.RemainingCount);
            Assert.Equal(expectedCount is not null, rule.IsFinite);
        }

        [Fact]
        public void OnceRuleIsSchedulableUntilDisabled()
        {
            Assert.True(Assert.Single(NoteReminderParser.Parse("[!t][1]{* * * * *}{c}")).IsSchedulable);
            Assert.False(Assert.Single(NoteReminderParser.Parse("[!t][X1]{* * * * *}{c}")).IsSchedulable);
        }

        [Fact]
        public void DisabledRuleIsNotSchedulable()
        {
            NoteReminderRule rule = Assert.Single(NoteReminderParser.Parse("[!t][X]{* * * * *}{c}"));
            Assert.False(rule.IsSchedulable);
        }

        [Fact]
        public void InvalidCronYieldsRuleWithNullSchedule()
        {
            NoteReminderRule rule = Assert.Single(NoteReminderParser.Parse("[!t][]{not a cron}{c}"));
            Assert.Null(rule.Schedule);
            Assert.False(rule.IsSchedulable);
        }

        [Fact]
        public void ParsesMultipleRulesInOrder()
        {
            string content = "before [!a][]{* * * * *}{first} middle [!b][X]{0 9 * * *}{second} after";
            IReadOnlyList<NoteReminderRule> rules = NoteReminderParser.Parse(content);

            Assert.Equal(2, rules.Count);
            Assert.Equal("first", rules[0].Content);
            Assert.Equal("second", rules[1].Content);
        }

        [Theory]
        [InlineData("[a normal link](https://example.com)")]
        [InlineData("![an image](img.png)")]
        [InlineData("[!missing braces] just text")]
        [InlineData("[!nocontrol]{* * * * *}{c}")]
        public void IgnoresNonReminderSyntax(string content)
        {
            Assert.Empty(NoteReminderParser.Parse(content));
        }

        [Fact]
        public void RuleIdStableAcrossReparseAndIndependentOfControl()
        {
            string enabled = "[!label][]{0 9 * * *}{take meds}";
            string disabled = "[!different label][X]{0 9 * * *}{take meds}";

            string a = NoteReminderParser.Parse(enabled)[0].RuleId;
            string b = NoteReminderParser.Parse(disabled)[0].RuleId;

            Assert.Equal(a, b); // same cron + content => same id, regardless of label/enabled
        }

        [Fact]
        public void RuleIdChangesWithCronOrContent()
        {
            string baseId = NoteReminderParser.Parse("[!l][]{0 9 * * *}{x}")[0].RuleId;
            string diffCron = NoteReminderParser.Parse("[!l][]{0 10 * * *}{x}")[0].RuleId;
            string diffContent = NoteReminderParser.Parse("[!l][]{0 9 * * *}{y}")[0].RuleId;

            Assert.NotEqual(baseId, diffCron);
            Assert.NotEqual(baseId, diffContent);
        }
    }
}
