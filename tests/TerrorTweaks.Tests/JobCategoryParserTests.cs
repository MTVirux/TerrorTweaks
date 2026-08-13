using TerrorTweaks.Tweaks.JobRoulette;

namespace TerrorTweaks.Tests;

public class JobCategoryParserTests
{
    [Theory]
    [InlineData("tanks", JobCategory.Tank)]
    [InlineData("healers", JobCategory.Healer)]
    [InlineData("melee", JobCategory.Melee)]
    [InlineData("casters", JobCategory.Caster)]
    [InlineData("physranged", JobCategory.PhysRanged)]
    [InlineData("crafters", JobCategory.Crafter)]
    [InlineData("gatherers", JobCategory.Gatherer)]
    [InlineData("pr", JobCategory.PhysRanged)]
    [InlineData("heals", JobCategory.Healer)]
    [InlineData("doh", JobCategory.Crafter)]
    [InlineData("magic", JobCategory.Caster)]
    [InlineData("TANKS", JobCategory.Tank)]
    [InlineData("Healer", JobCategory.Healer)]
    [InlineData("  tanks  ", JobCategory.Tank)]
    public void ParsesKnownTokens(string arg, JobCategory expected)
    {
        Assert.True(JobCategoryParser.TryParse(arg, out var category));
        Assert.Equal(expected, category);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("frobnicate")]
    public void RejectsUnknownOrBlank(string? arg)
        => Assert.False(JobCategoryParser.TryParse(arg, out _));
}
