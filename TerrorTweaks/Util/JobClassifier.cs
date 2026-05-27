namespace TerrorTweaks.Util;

internal enum JobKind
{
    Combat,
    Crafter,
    Gatherer,
}

internal static class JobClassifier
{
    // ClassJob row ids are stable game data: 8-15 are Disciples of the Hand (crafters),
    // 16-18 are Disciples of the Land (gatherers); everything else is a combat job.
    public static JobKind Classify(uint classJobId) => classJobId switch
    {
        >= 8 and <= 15  => JobKind.Crafter,
        >= 16 and <= 18 => JobKind.Gatherer,
        _               => JobKind.Combat,
    };
}
