using YASN.Infrastructure.Reminders;

namespace YASN.Migration.Tests
{
    /// <summary>Verifies cron parsing and next-occurrence computation for 5- and 6-field expressions.</summary>
    public sealed class CronExpressionTests
    {
        private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi, int s = 0) =>
            new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.Zero);

        [Theory]
        [InlineData("* * * * *")]
        [InlineData("0 9 * * 1-5")]
        [InlineData("*/15 * * * *")]
        [InlineData("0 0 9 * * 1-5")]
        [InlineData("30 */2 1,15 * *")]
        public void ParsesValidExpressions(string expr)
        {
            Assert.True(CronExpression.TryParse(expr, out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("* * * *")]
        [InlineData("* * * * * * *")]
        [InlineData("60 * * * *")]
        [InlineData("* 24 * * *")]
        [InlineData("* * 0 * *")]
        [InlineData("* * * 13 *")]
        [InlineData("abc * * * *")]
        [InlineData("5-1 * * * *")]
        public void RejectsInvalidExpressions(string expr)
        {
            Assert.False(CronExpression.TryParse(expr, out _));
        }

        [Fact]
        public void EveryMinuteAdvancesByOneMinute()
        {
            CronExpression.TryParse("* * * * *", out CronExpression? cron);
            DateTimeOffset next = cron!.GetNextOccurrence(Utc(2026, 1, 1, 10, 30, 15))!.Value;
            Assert.Equal(Utc(2026, 1, 1, 10, 31), next);
        }

        [Fact]
        public void DailyNineAmRollsToNextDay()
        {
            CronExpression.TryParse("0 9 * * *", out CronExpression? cron);
            DateTimeOffset next = cron!.GetNextOccurrence(Utc(2026, 1, 1, 10, 0))!.Value;
            Assert.Equal(Utc(2026, 1, 2, 9, 0), next);
        }

        [Fact]
        public void WeekdayScheduleSkipsWeekend()
        {
            // 2026-01-02 is a Friday; next weekday 09:00 after Friday 10:00 is Monday 2026-01-05.
            CronExpression.TryParse("0 9 * * 1-5", out CronExpression? cron);
            DateTimeOffset next = cron!.GetNextOccurrence(Utc(2026, 1, 2, 10, 0))!.Value;
            Assert.Equal(Utc(2026, 1, 5, 9, 0), next);
        }

        [Fact]
        public void SundayMatchesBothZeroAndSeven()
        {
            CronExpression.TryParse("0 9 * * 7", out CronExpression? cron);
            // 2026-01-04 is a Sunday.
            DateTimeOffset next = cron!.GetNextOccurrence(Utc(2026, 1, 1, 0, 0))!.Value;
            Assert.Equal(DayOfWeek.Sunday, next.DayOfWeek);
            Assert.Equal(Utc(2026, 1, 4, 9, 0), next);
        }

        [Fact]
        public void SixFieldSecondsStep()
        {
            CronExpression.TryParse("*/30 * * * * *", out CronExpression? cron);
            DateTimeOffset next = cron!.GetNextOccurrence(Utc(2026, 1, 1, 10, 0, 5))!.Value;
            Assert.Equal(Utc(2026, 1, 1, 10, 0, 30), next);
        }

        [Fact]
        public void ImpossibleDateReturnsNull()
        {
            // Feb 31 never occurs.
            CronExpression.TryParse("0 0 31 2 *", out CronExpression? cron);
            Assert.Null(cron!.GetNextOccurrence(Utc(2026, 1, 1, 0, 0)));
        }
    }
}
