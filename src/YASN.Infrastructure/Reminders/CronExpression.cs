using System.Globalization;

namespace YASN.Infrastructure.Reminders
{
    /// <summary>
    /// A parsed crontab expression supporting 5 fields (min hour day-of-month month day-of-week) or
    /// 6 fields (with a leading seconds field). Each field accepts <c>*</c>, comma lists, hyphen
    /// ranges, and <c>*/step</c> or <c>lo-hi/step</c> increments. Named months/days are not supported.
    /// </summary>
    public sealed class CronExpression
    {
        private readonly bool[] seconds;
        private readonly bool[] minutes;
        private readonly bool[] hours;
        private readonly bool[] daysOfMonth;
        private readonly bool[] months;
        private readonly bool[] daysOfWeek;
        private readonly bool hasSeconds;

        private CronExpression(bool[] seconds, bool[] minutes, bool[] hours, bool[] daysOfMonth, bool[] months, bool[] daysOfWeek, bool hasSeconds)
        {
            this.seconds = seconds;
            this.minutes = minutes;
            this.hours = hours;
            this.daysOfMonth = daysOfMonth;
            this.months = months;
            this.daysOfWeek = daysOfWeek;
            this.hasSeconds = hasSeconds;
        }

        /// <summary>
        /// Attempts to parse a 5- or 6-field cron expression.
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

            string[] fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool hasSeconds = fields.Length == 6;
            if (fields.Length != 5 && fields.Length != 6)
            {
                return false;
            }

            int index = 0;
            bool[]? sec = hasSeconds ? TryField(fields[index++], 0, 59) : Single(0);
            bool[]? min = TryField(fields[index++], 0, 59);
            bool[]? hr = TryField(fields[index++], 0, 23);
            bool[]? dom = TryField(fields[index++], 1, 31);
            bool[]? mon = TryField(fields[index++], 1, 12);
            bool[]? dow = TryField(fields[index], 0, 7);

            if (sec is null || min is null || hr is null || dom is null || mon is null || dow is null)
            {
                return false;
            }

            // Cron treats Sunday as both 0 and 7; fold 7 into 0 and keep a 7-slot array.
            bool[] normalizedDow = new bool[7];
            for (int day = 0; day <= 7; day++)
            {
                if (dow[day])
                {
                    normalizedDow[day % 7] = true;
                }
            }

            result = new CronExpression(sec, min, hr, dom, mon, normalizedDow, hasSeconds);
            return true;

            static bool[] Single(int value)
            {
                bool[] set = new bool[60];
                set[value] = true;
                return set;
            }

            static bool[]? TryField(string field, int lo, int hi)
            {
                return ParseField(field, lo, hi);
            }
        }

        /// <summary>
        /// Parses one cron field into a presence set spanning [lo, hi]. Returns <see langword="null"/>
        /// when the field is malformed or out of range.
        /// </summary>
        private static bool[]? ParseField(string field, int lo, int hi)
        {
            if (string.IsNullOrEmpty(field))
            {
                return null;
            }

            bool[] set = new bool[hi + 1];
            foreach (string part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!ApplyPart(part, lo, hi, set))
                {
                    return null;
                }
            }

            return set.Skip(lo).Take(hi - lo + 1).Any(v => v) ? set : null;
        }

        private static bool ApplyPart(string part, int lo, int hi, bool[] set)
        {
            int step = 1;
            string rangePart = part;
            int slash = part.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                if (!int.TryParse(part[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step <= 0)
                {
                    return false;
                }

                rangePart = part[..slash];
            }

            int rangeLo;
            int rangeHi;
            if (rangePart == "*")
            {
                rangeLo = lo;
                rangeHi = hi;
            }
            else if (rangePart.Contains('-', StringComparison.Ordinal))
            {
                string[] bounds = rangePart.Split('-');
                if (bounds.Length != 2 ||
                    !int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out rangeLo) ||
                    !int.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out rangeHi))
                {
                    return false;
                }
            }
            else
            {
                if (!int.TryParse(rangePart, NumberStyles.None, CultureInfo.InvariantCulture, out rangeLo))
                {
                    return false;
                }

                rangeHi = slash >= 0 ? hi : rangeLo;
            }

            if (rangeLo < lo || rangeHi > hi || rangeLo > rangeHi)
            {
                return false;
            }

            for (int value = rangeLo; value <= rangeHi; value += step)
            {
                set[value] = true;
            }

            return true;
        }

        /// <summary>
        /// Returns the next time strictly after <paramref name="after"/> that matches this expression,
        /// or <see langword="null"/> when none occurs within a bounded search horizon (guards against
        /// impossible dates such as <c>0 0 31 2 *</c>).
        /// </summary>
        /// <param name="after">The exclusive lower bound; the result is &gt; this value.</param>
        public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
        {
            TimeSpan offset = after.Offset;
            DateTimeOffset horizon = after.AddYears(5);

            if (hasSeconds)
            {
                DateTimeOffset candidate = Truncate(after, includeSeconds: true).AddSeconds(1);
                for (; candidate <= horizon; candidate = candidate.AddSeconds(1))
                {
                    if (Matches(candidate))
                    {
                        return candidate;
                    }
                }

                return null;
            }

            DateTimeOffset minuteCandidate = Truncate(after, includeSeconds: false).AddMinutes(1);
            for (; minuteCandidate <= horizon; minuteCandidate = minuteCandidate.AddMinutes(1))
            {
                if (Matches(minuteCandidate))
                {
                    return minuteCandidate;
                }
            }

            return null;

            static DateTimeOffset Truncate(DateTimeOffset value, bool includeSeconds)
            {
                return includeSeconds
                    ? new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Offset)
                    : new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);
            }
        }

        private bool Matches(DateTimeOffset time)
        {
            if (hasSeconds && !seconds[time.Second])
            {
                return false;
            }

            return minutes[time.Minute]
                && hours[time.Hour]
                && daysOfMonth[time.Day]
                && months[time.Month]
                && daysOfWeek[(int)time.DayOfWeek];
        }
    }
}
