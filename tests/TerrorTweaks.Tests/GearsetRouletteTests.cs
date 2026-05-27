using System;
using TerrorTweaks.Util;

namespace TerrorTweaks.Tests;

public class GearsetRouletteTests
{
    [Fact]
    public void CombatNonLimited_AlwaysEligible()
        => Assert.True(GearsetRoulette.IsEligible(JobKind.Combat, false, new RouletteOptions(false, false, false)));

    [Fact]
    public void CombatLimited_RequiresIncludeLimited()
    {
        Assert.False(GearsetRoulette.IsEligible(JobKind.Combat, true, new RouletteOptions(false, false, false)));
        Assert.True(GearsetRoulette.IsEligible(JobKind.Combat, true, new RouletteOptions(false, false, true)));
    }

    [Fact]
    public void Crafter_RequiresIncludeCrafters()
    {
        Assert.False(GearsetRoulette.IsEligible(JobKind.Crafter, false, new RouletteOptions(false, false, false)));
        Assert.True(GearsetRoulette.IsEligible(JobKind.Crafter, false, new RouletteOptions(true, false, false)));
    }

    [Fact]
    public void Gatherer_RequiresIncludeGatherers()
    {
        Assert.False(GearsetRoulette.IsEligible(JobKind.Gatherer, false, new RouletteOptions(false, false, false)));
        Assert.True(GearsetRoulette.IsEligible(JobKind.Gatherer, false, new RouletteOptions(false, true, false)));
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
