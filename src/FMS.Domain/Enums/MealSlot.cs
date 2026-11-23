namespace FMS.Domain.Enums;

/// <summary>
/// Which feeding of the day a record belongs to.
/// </summary>
/// <remarks>
/// "Afternoon" is not a separate value: the farm vocabulary already calls the middle
/// feeding <see cref="Midday"/> in feeding schedules and in the locale files, and two
/// words for one feeding would split every report that groups by it.
/// </remarks>
public enum MealSlot
{
    Morning = 0,
    Midday = 1,
    Evening = 2,
    Night = 3
}
