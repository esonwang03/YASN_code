using CronosExpression = Cronos.CronExpression;

namespace YASN.Infrastructure.Reminders
{
    /// <summary>
    /// A parsed crontab expression, backed by the Cronos library. Accepts 5 fields
    /// (min hour day-of-month month day-of-week) or 6 fields (with a leading seconds field), with the
    /// usual <c>*</c>, comma lists, hyphen ranges (including wrapping ranges such as <c>23-01</c>),
    /// <c>*/step</c> increments, and named months/days. Schedules are evaluated in UTC, matching the
    /// reminder scheduler's UTC clock.
    /// </summary>
    public sealed class CronExpression
    {
        private readonly CronosExpression expression;

        private CronExpression(CronosExpression expression)
        {
            this.expression = expression;
        }

        /// <summary>
        /// Attempts to parse a 5- or 6-field cron expression. The field count selects the format: a
        /// 6-field expression is parsed with a leading seconds field.
        /// </summary>
        /// <param name="expression">The raw cron text.</param>
        /// <param name="result">The parsed expression when successful.</param>
        /// <returns><see langword="true"/> when the expression is valid.</returns>
        public static bool TryParse(string? expression, out CronExpression? result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            int fieldCount = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            if (fieldCount != 5 && fieldCount != 6)
            {
                return false;
            }

            Cronos.CronFormat format = fieldCount == 6 ? Cronos.CronFormat.IncludeSeconds : Cronos.CronFormat.Standard;
            try
            {
                result = new CronExpression(CronosExpression.Parse(expression.Trim(), format));
                return true;
            }
            catch (Cronos.CronFormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the next time strictly after <paramref name="after"/> that matches this expression,
        /// or <see langword="null"/> when none occurs (for example an impossible date such as
        /// <c>0 0 31 2 *</c>). The schedule is evaluated in UTC; the result carries a zero offset.
        /// </summary>
        /// <param name="after">The exclusive lower bound; the result is &gt; this value.</param>
        public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
        {
            return expression.GetNextOccurrence(after, TimeZoneInfo.Utc, inclusive: false);
        }
    }
}
