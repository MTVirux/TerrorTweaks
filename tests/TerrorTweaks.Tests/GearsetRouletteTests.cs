using System;
using TerrorTweaks.Tweaks.JobRoulette;

namespace TerrorTweaks.Tests;

public class GearsetRouletteTests
{
    private static readonly RouletteOptions None =
        new(false, false, false, false, false, false, false, false);

    [Fact]
    public void Tank_RequiresIncludeTanks()
    {
        Assert.False(GearsetRoulette.IsEligible(JobCategory.Tank, false, None));
        Assert.True(GearsetRoulette.IsEligible(JobCategory.Tank, false, None with { IncludeTanks = true }));
    }

    [Fact]
    public void Healer_RequiresIncludeHealers()
    {
        Assert.False(GearsetRoulette.IsEligible(JobCategory.Healer, false, None));
        Assert.True(GearsetRoulette.IsEligible(JobCategory.Healer, false, None with { IncludeHealers = true }));
    }

    [Fact]
    public void Melee_RequiresIncludeMelee()
    {
        Assert.False(GearsetRoulette.IsEligible(JobCategory.Melee, false, None));
        Assert.True(GearsetRoulette.IsEligible(JobCategory.Melee, false, None with { IncludeMelee = true }));
    }

    [Fact]
    public void PhysRanged_RequiresIncludePhysRanged()
    {
        Assert.False(GearsetRoulette.IsEligible(JobCategory.PhysRanged, false, None));
        Assert.True(GearsetRoulette.IsEligible(JobCategory.PhysRanged, false, None with { IncludePhysRanged = true }));
    }

    [Fact]
    public void Caster_RequiresIncludeCasters()
    {
        Assert.False(GearsetRoulette.IsEligible(JobCategory.Caster, false, None));
        Assert.True(GearsetRoulette.IsEligible(JobCategory.Caster, false, None with { IncludeCasters = true }));
    }

    [Fact]
    public void Crafter_RequiresIncludeCrafters()
    {
        Assert.False(GearsetRoulette.IsEligible(JobCategory.Crafter, false, None));
        Assert.True(GearsetRoulette.IsEligible(JobCategory.Crafter, false, None with { IncludeCrafters = true }));
    }

    [Fact]
    public void Gatherer_RequiresIncludeGatherers()
    {
        Assert.False(GearsetRoulette.IsEligible(JobCategory.Gatherer, false, None));
        Assert.True(GearsetRoulette.IsEligible(JobCategory.Gatherer, false, None with { IncludeGatherers = true }));
    }

    [Fact]
    public void Other_IsNeverEligible()
        => Assert.False(GearsetRoulette.IsEligible(JobCategory.Other, false,
            new RouletteOptions(true, true, true, true, true, true, true, true)));

    [Fact]
    public void Limited_GatedByIncludeLimited_RegardlessOfRole()
    {
        // Blue Mage is a limited Caster.
        Assert.False(GearsetRoulette.IsEligible(JobCategory.Caster, true, None));
        Assert.True(GearsetRoulette.IsEligible(JobCategory.Caster, true, None with { IncludeLimited = true }));
        // The Casters toggle alone must not surface a limited job.
        Assert.False(GearsetRoulette.IsEligible(JobCategory.Caster, true, None with { IncludeCasters = true }));
    }

    [Fact]
    public void FirstPerJob_KeepsLowestSlotPerJob()
    {
        var candidates = new (int, uint)[]
        {
            (5, 19u),  // PLD at slot 5
            (1, 19u),  // PLD at slot 1 -> representative
            (3, 21u),  // WAR at slot 3
        };

        Assert.Equal(new[] { 1, 3 }, GearsetRoulette.FirstPerJob(candidates));
    }

    [Fact]
    public void FirstPerJob_Empty_ReturnsEmpty()
        => Assert.Empty(GearsetRoulette.FirstPerJob(Array.Empty<(int, uint)>()));

    [Fact]
    public void Pick_Empty_ReturnsNull()
        => Assert.Null(GearsetRoulette.Pick(Array.Empty<int>(), new Random(0)));

    [Fact]
    public void Pick_ReturnsElementFromList()
    {
        var list = new[] { 10, 20, 30 };
        Assert.Contains(GearsetRoulette.Pick(list, new Random(0))!.Value, list);
    }
}
