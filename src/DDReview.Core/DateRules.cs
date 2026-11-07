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

    public static DateOnly EndFor(DateOnly from) => DateOnly.FromDateTime(EndFor(from.ToDateTime(TimeOnly.MinValue)));

    /// <summary>The usual distance of a compare date from the start date: the same weekday a year earlier.</summary>
    public const int DefaultCompareOffsetDays = 364;

    /// <summary>The compare date that keeps its distance from the start date (<paramref name="offsetDays"/> days earlier).</summary>
    public static DateTime CompareFor(DateTime from, int offsetDays) => from.Year < 1900 ? from : from.AddDays(-offsetDays);
}
