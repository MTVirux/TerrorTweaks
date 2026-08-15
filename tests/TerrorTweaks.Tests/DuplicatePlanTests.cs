using TerrorTweaks.Tweaks.ListingHelper;

namespace TerrorTweaks.Tests;

public class DuplicatePlanTests
{
    [Fact]
    public void Build_OneCopy_FromASingleStack()
    {
        var plan = DuplicatePlan.Build([12], stackSize: 5, occupiedSlots: 3, wanted: 1);
        Assert.True(plan.CanStart);
        Assert.Equal(1, plan.Copies);
    }

    [Fact]
    public void Build_Fill_TakesWholeStacksOnly()
    {
        var plan = DuplicatePlan.Build([12], stackSize: 5, occupiedSlots: 3, wanted: DuplicatePlan.MarketSlots);
        Assert.Equal(2, plan.Copies);
    }

    [Fact]
    public void Build_Fill_CountsEachSourceSlotSeparately()
    {
        // Three slots of four cannot make a stack of five between them - a copy comes out of
        // one slot, never pooled across several.
        var plan = DuplicatePlan.Build([4, 4, 4], stackSize: 5, occupiedSlots: 0, wanted: DuplicatePlan.MarketSlots);
        Assert.False(plan.CanStart);
        Assert.Equal(DuplicateBlock.NotEnoughStock, plan.Block);
    }

    [Fact]
    public void Build_Fill_AddsUpAcrossSlots()
    {
        var plan = DuplicatePlan.Build([5, 11, 3], stackSize: 5, occupiedSlots: 0, wanted: DuplicatePlan.MarketSlots);
        Assert.Equal(3, plan.Copies);
    }

    [Fact]
    public void Build_Fill_StopsAtTheFreeMarketSlots()
    {
        var plan = DuplicatePlan.Build([999], stackSize: 1, occupiedSlots: 18, wanted: DuplicatePlan.MarketSlots);
        Assert.Equal(2, plan.Copies);
    }

    [Fact]
    public void Build_FullMarket_IsBlocked()
    {
        var plan = DuplicatePlan.Build([999], stackSize: 1, occupiedSlots: DuplicatePlan.MarketSlots, wanted: 1);
        Assert.False(plan.CanStart);
        Assert.Equal(DuplicateBlock.MarketFull, plan.Block);
    }

    [Fact]
    public void Build_ShortOfAWholeStack_IsBlocked()
    {
        var plan = DuplicatePlan.Build([4], stackSize: 5, occupiedSlots: 0, wanted: 1);
        Assert.False(plan.CanStart);
        Assert.Equal(DuplicateBlock.NotEnoughStock, plan.Block);
    }

    [Fact]
    public void Build_NoSourceSlots_IsBlocked()
    {
        var plan = DuplicatePlan.Build([], stackSize: 5, occupiedSlots: 0, wanted: 1);
        Assert.False(plan.CanStart);
        Assert.Equal(DuplicateBlock.NotEnoughStock, plan.Block);
    }

    [Fact]
    public void Build_ExactlyOneStackLeft_MakesOneCopy()
    {
        var plan = DuplicatePlan.Build([5], stackSize: 5, occupiedSlots: 0, wanted: DuplicatePlan.MarketSlots);
        Assert.Equal(1, plan.Copies);
    }

    [Fact]
    public void Build_UnknownStackSize_IsBlocked()
    {
        var plan = DuplicatePlan.Build([999], stackSize: 0, occupiedSlots: 0, wanted: 1);
        Assert.False(plan.CanStart);
        Assert.Equal(DuplicateBlock.NotEnoughStock, plan.Block);
    }
}
