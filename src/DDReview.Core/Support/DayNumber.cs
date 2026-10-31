namespace DDReview.Core.Support;

/// <summary>
/// DDReview stores days as a count where day 0 = 2003-09-29 (see fn_CalcWeekNumberBaseDate / fn_CalcDayNumber).
/// </summary>
public static class DayNumber
{
    public static readonly DateOnly BaseDate = new(2003, 9, 29);

    /// <summary>The "no comparison period" sentinel the old app emits is int.MinValue + today's DayNumber (it changes daily).</summary>
    public static int FromDate(DateOnly date, DateOnly? baseDate = null) =>
        date.DayNumber - (baseDate ?? BaseDate).DayNumber;

    public static DateOnly ToDate(int dayNumber, DateOnly? baseDate = null) =>
        (baseDate ?? BaseDate).AddDays(dayNumber);
}
