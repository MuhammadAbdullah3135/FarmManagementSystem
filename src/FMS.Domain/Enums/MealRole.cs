namespace FMS.Domain.Enums;

/// <summary>
/// What part a feeding played in the day's ration: the main course, something offered
/// alongside it, or neither.
/// </summary>
public enum MealRole
{
    Main = 0,
    Supplement = 1,
    Other = 2
}
