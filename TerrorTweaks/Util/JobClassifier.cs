namespace TerrorTweaks.Util;

public enum JobCategory
{
    Tank,
    Healer,
    Melee,
    PhysRanged,
    Caster,
    Crafter,
    Gatherer,
    Other,
}

internal static class JobClassifier
{
    // ClassJob row ids are stable game data. Crafters (Disciples of the Hand) are 8-15
    // and gatherers (Disciples of the Land) are 16-18; combat jobs are split by role.
    // Unknown ids fall through to Other (never eligible) so a new job stays out of the
    // roulette until it is classified here.
    public static JobCategory Classify(uint classJobId) => classJobId switch
    {
        1 or 3 or 19 or 21 or 32 or 37                   => JobCategory.Tank,
        6 or 24 or 28 or 33 or 40                        => JobCategory.Healer,
        2 or 4 or 20 or 22 or 29 or 30 or 34 or 39 or 41 => JobCategory.Melee,
        5 or 23 or 31 or 38                              => JobCategory.PhysRanged,
        7 or 25 or 26 or 27 or 35 or 36 or 42            => JobCategory.Caster,
        >= 8 and <= 15                                   => JobCategory.Crafter,
        >= 16 and <= 18                                  => JobCategory.Gatherer,
        _                                                => JobCategory.Other,
    };
}
