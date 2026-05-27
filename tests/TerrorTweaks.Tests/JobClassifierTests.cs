using TerrorTweaks.Util;

namespace TerrorTweaks.Tests;

public class JobClassifierTests
{
    [Theory]
    [InlineData(1u)]   // GLA
    [InlineData(7u)]   // ACN (last id before crafters)
    [InlineData(19u)]  // PLD
    [InlineData(36u)]  // BLU (combat; limited handled separately)
    public void CombatIds_AreCombat(uint id)
        => Assert.Equal(JobKind.Combat, JobClassifier.Classify(id));

    [Theory]
    [InlineData(8u)]   // CRP (first crafter)
    [InlineData(15u)]  // CUL (last crafter)
    public void CrafterIds_AreCrafter(uint id)
        => Assert.Equal(JobKind.Crafter, JobClassifier.Classify(id));

    [Theory]
    [InlineData(16u)]  // MIN (first gatherer)
    [InlineData(18u)]  // FSH (last gatherer)
    public void GathererIds_AreGatherer(uint id)
        => Assert.Equal(JobKind.Gatherer, JobClassifier.Classify(id));
}
