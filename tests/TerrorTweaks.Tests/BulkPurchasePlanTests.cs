using TerrorTweaks.Tweaks.BulkPurchase;

namespace TerrorTweaks.Tests;

public class BulkPurchasePlanTests
{
    [Fact]
    public void Resolve_Flat_IgnoresOwned()
        => Assert.Equal(24000, BulkPurchasePlan.Resolve(24000, 1203, topUp: false));

    [Fact]
    public void Resolve_TopUp_SubtractsOwned()
        => Assert.Equal(22797, BulkPurchasePlan.Resolve(24000, 1203, topUp: true));

    [Fact]
    public void Resolve_TopUp_AlreadyAtTarget_IsZero()
        => Assert.Equal(0, BulkPurchasePlan.Resolve(100, 250, topUp: true));

    [Fact]
    public void Resolve_NegativeRequest_IsZero()
        => Assert.Equal(0, BulkPurchasePlan.Resolve(-5, 0, topUp: false));

    [Fact]
    public void Build_ZeroAmount_IsBlocked()
    {
        var plan = BulkPurchasePlan.Build(0, 10, 999, 1_000_000, 30);
        Assert.False(plan.CanStart);
        Assert.Equal(PurchaseBlock.NothingToBuy, plan.Block);
    }

    [Fact]
    public void Build_SplitsIntoBatchesOf99()
    {
        Assert.Equal(1, BulkPurchasePlan.Build(1, 1, 999, long.MaxValue, 99).Purchases);
        Assert.Equal(1, BulkPurchasePlan.Build(99, 1, 999, long.MaxValue, 99).Purchases);
        Assert.Equal(2, BulkPurchasePlan.Build(100, 1, 999, long.MaxValue, 99).Purchases);
        Assert.Equal(243, BulkPurchasePlan.Build(24000, 1, 999, long.MaxValue, 99).Purchases);
    }

    [Fact]
    public void Build_TotalCost_DoesNotOverflow()
    {
        var plan = BulkPurchasePlan.Build(999_999, 100_000, 999, long.MaxValue, 99);
        Assert.Equal(99_999_900_000L, plan.TotalCost);
    }

    [Fact]
    public void Build_NotEnoughGil_IsBlocked()
    {
        var plan = BulkPurchasePlan.Build(100, 50, 999, 4999, 30);
        Assert.Equal(PurchaseBlock.NotEnoughGil, plan.Block);
        Assert.False(plan.CanStart);
    }

    [Fact]
    public void Build_ExactGil_IsAllowed()
        => Assert.True(BulkPurchasePlan.Build(100, 50, 999, 5000, 30).CanStart);

    [Fact]
    public void Build_SlotsNeeded_RoundsUpByStackSize()
    {
        Assert.Equal(25, BulkPurchasePlan.Build(24000, 1, 999, long.MaxValue, 99).SlotsNeeded);
        Assert.Equal(1, BulkPurchasePlan.Build(999, 1, 999, long.MaxValue, 99).SlotsNeeded);
        Assert.Equal(2, BulkPurchasePlan.Build(1000, 1, 999, long.MaxValue, 99).SlotsNeeded);
    }

    [Fact]
    public void Build_UnknownStackSize_TreatsEveryItemAsASlot()
        => Assert.Equal(50, BulkPurchasePlan.Build(50, 1, 0, long.MaxValue, 99).SlotsNeeded);

    [Fact]
    public void Build_SpaceWarning_DoesNotBlockTheJob()
    {
        var plan = BulkPurchasePlan.Build(24000, 1, 999, long.MaxValue, freeSlots: 3);
        Assert.True(plan.SpaceWarning);
        Assert.True(plan.CanStart);
    }

    [Fact]
    public void Build_EnoughSlots_HasNoWarning()
        => Assert.False(BulkPurchasePlan.Build(24000, 1, 999, long.MaxValue, freeSlots: 25).SpaceWarning);

    [Fact]
    public void NextBatch_CapsAt99AndFloorsAtZero()
    {
        Assert.Equal(99, BulkPurchasePlan.NextBatch(24000));
        Assert.Equal(99, BulkPurchasePlan.NextBatch(99));
        Assert.Equal(7, BulkPurchasePlan.NextBatch(7));
        Assert.Equal(0, BulkPurchasePlan.NextBatch(0));
        Assert.Equal(0, BulkPurchasePlan.NextBatch(-3));
    }
}
