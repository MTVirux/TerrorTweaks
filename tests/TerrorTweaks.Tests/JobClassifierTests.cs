using TerrorTweaks.Tweaks.JobRoulette;

namespace TerrorTweaks.Tests;

public class JobClassifierTests
{
    [Theory]
    [InlineData(1u)]   // GLA
    [InlineData(3u)]   // MRD
    [InlineData(19u)]  // PLD
    [InlineData(21u)]  // WAR
    [InlineData(32u)]  // DRK
    [InlineData(37u)]  // GNB
    public void TankIds_AreTank(uint id)
        => Assert.Equal(JobCategory.Tank, JobClassifier.Classify(id));

    [Theory]
    [InlineData(6u)]   // CNJ
    [InlineData(24u)]  // WHM
    [InlineData(28u)]  // SCH
    [InlineData(33u)]  // AST
    [InlineData(40u)]  // SGE
    public void HealerIds_AreHealer(uint id)
        => Assert.Equal(JobCategory.Healer, JobClassifier.Classify(id));

    [Theory]
    [InlineData(2u)]   // PGL
    [InlineData(4u)]   // LNC
    [InlineData(20u)]  // MNK
    [InlineData(22u)]  // DRG
    [InlineData(29u)]  // ROG
    [InlineData(30u)]  // NIN
    [InlineData(34u)]  // SAM
    [InlineData(39u)]  // RPR
    [InlineData(41u)]  // VPR
    public void MeleeIds_AreMelee(uint id)
        => Assert.Equal(JobCategory.Melee, JobClassifier.Classify(id));

    [Theory]
    [InlineData(5u)]   // ARC
    [InlineData(23u)]  // BRD
    [InlineData(31u)]  // MCH
    [InlineData(38u)]  // DNC
    public void PhysRangedIds_ArePhysRanged(uint id)
        => Assert.Equal(JobCategory.PhysRanged, JobClassifier.Classify(id));

    [Theory]
    [InlineData(7u)]   // THM
    [InlineData(25u)]  // BLM
    [InlineData(26u)]  // ACN
    [InlineData(27u)]  // SMN
    [InlineData(35u)]  // RDM
    [InlineData(36u)]  // BLU
    [InlineData(42u)]  // PCT
    public void CasterIds_AreCaster(uint id)
        => Assert.Equal(JobCategory.Caster, JobClassifier.Classify(id));

    [Theory]
    [InlineData(8u)]   // CRP
    [InlineData(15u)]  // CUL
    public void CrafterIds_AreCrafter(uint id)
        => Assert.Equal(JobCategory.Crafter, JobClassifier.Classify(id));

    [Theory]
    [InlineData(16u)]  // MIN
    [InlineData(18u)]  // FSH
    public void GathererIds_AreGatherer(uint id)
        => Assert.Equal(JobCategory.Gatherer, JobClassifier.Classify(id));

    [Theory]
    [InlineData(0u)]    // ADV
    [InlineData(999u)]  // unknown / future job
    public void UnknownIds_AreOther(uint id)
        => Assert.Equal(JobCategory.Other, JobClassifier.Classify(id));
}
