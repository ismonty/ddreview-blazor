namespace DDReview.Core;

/// <summary>How the date entries keep their dates in step while a person types.</summary>
public static class DateRules
{
    /// <summary>
    /// The To date that goes with a From date: the last day of the month when From is the first of a month, otherwise six days later (a week).
    /// </summary>
    public static DateTime EndFor(DateTime from)
    {
        if (from.Year < 1900 || from.Year > 9000) return from;   // a cleared or nonsense date: leave the other date alone
        return from.Day == 1 ? new DateTime(from.Year, from.Month, DateTime.DaysInMonth(from.Year, from.Month)) : from.AddDays(6);
    }

    /// <summary>
    /// As <see cref="EndFor(DateTime)"/>, but a month that has not finished yet stops at <paramref name="yesterday"/> (never before the start itself),
    /// so a From of the 1st of this month gives 1st through yesterday.
    /// </summary>
    public static DateTime EndFor(DateTime from, DateTime yesterday)
    {
        var end = EndFor(from);
        if (from.Year < 1900 || from.Year > 9000 || from.Day != 1) return end;
        return end > yesterday ? (yesterday < from ? from : yesterday.Date) : end;
    }

    public static DateOnly EndFor(DateOnly from) => DateOnly.FromDateTime(EndFor(from.ToDateTime(TimeOnly.MinValue)));

    /// <summary>The From date after the To date was changed: when the new end falls before the start, the start becomes that end date (a one-day range).</summary>
    public static DateTime StartFor(DateTime from, DateTime end) =>
        end.Year < 1900 || end.Year > 9000 || from.Year < 1900 ? from : (end < from ? end : from);

    public static DateOnly StartFor(DateOnly from, DateOnly end) => DateOnly.FromDateTime(StartFor(from.ToDateTime(TimeOnly.MinValue), end.ToDateTime(TimeOnly.MinValue)));

    /// <summary>The usual distance of a compare date from the start date: the same weekday a year earlier.</summary>
    public const int DefaultCompareOffsetDays = 364;

    /// <summary>The compare date that keeps its distance from the start date (<paramref name="offsetDays"/> days earlier).</summary>
    public static DateTime CompareFor(DateTime from, int offsetDays) => from.Year < 1900 ? from : from.AddDays(-offsetDays);

    public static DateTime Yesterday => DateTime.Today.AddDays(-1);
    public static DateTime LastMonthStart(DateTime today) => new DateTime(today.Year, today.Month, 1).AddMonths(-1);
    public static DateTime LastMonthEnd(DateTime today) => new DateTime(today.Year, today.Month, 1).AddDays(-1);
    public static DateTime YearStart(DateTime today) => new(today.Year, 1, 1);

    public enum StartPreset { Yesterday, LastMonthStart, YearStart }
    public enum EndPreset { Yesterday, LastMonthEnd }

    /// <summary>
    /// A shortcut chosen for the From date. When it lands after the To date, the To date moves to whatever fits the shortcut:
    /// Yesterday gives yesterday, Last Month Start gives the end of last month, Year Start gives yesterday (year to date).
    /// </summary>
    public static (DateTime Start, DateTime End) ApplyStart(StartPreset p, DateTime end, DateTime today)
    {
        var start = p switch { StartPreset.Yesterday => today.AddDays(-1), StartPreset.LastMonthStart => LastMonthStart(today), _ => YearStart(today) };
        if (end.Year >= 1900 && end >= start) return (start, end);
        return (start, p == StartPreset.LastMonthStart ? LastMonthEnd(today) : today.AddDays(-1));
    }

    /// <summary>A shortcut chosen for the To date. When the From date is then after it, From becomes the first of the To date's month.</summary>
    public static (DateTime Start, DateTime End) ApplyEnd(EndPreset p, DateTime start, DateTime today)
    {
        var end = p == EndPreset.Yesterday ? today.AddDays(-1) : LastMonthEnd(today);
        if (start.Year >= 1900 && start <= end) return (start, end);
        return (new DateTime(end.Year, end.Month, 1), end);
    }

    /// <summary>
    /// Previous / Next. A range that is a whole month (the 1st through the month's last day, or through yesterday when that month is still running)
    /// moves by calendar months, and keeps ending on the month's last day, or yesterday if that is sooner. Any other range moves by its own length.
    /// </summary>
    public static (DateTime Start, DateTime End) Shift(DateTime start, DateTime end, int dir, DateTime yesterday)
    {
        bool wholeMonth = start.Day == 1 && start.Year == end.Year && start.Month == end.Month
            && (end.Day == DateTime.DaysInMonth(end.Year, end.Month) || end.Date == yesterday.Date);
        if (wholeMonth)
        {
            var ns = start.AddMonths(dir);
            return (ns, EndFor(ns, yesterday));
        }
        var days = (int)(end - start).TotalDays + 1;
        return (start.AddDays(dir * days), end.AddDays(dir * days));
    }
}
