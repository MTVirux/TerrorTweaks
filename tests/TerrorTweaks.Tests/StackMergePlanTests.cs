using TerrorTweaks.Tweaks.ListingHelper;

namespace TerrorTweaks.Tests;

public class StackMergePlanTests
{
    [Fact]
    public void Next_ASlotAlreadyHoldsEnough_IsNothingToDo()
    {
        Assert.Null(StackMergePlan.Next([6, 4], wanted: 5, maxStackSize: 99));
    }

    [Fact]
    public void Next_SplitAcrossSlots_PoursTheSecondLargestIntoTheLargest()
    {
        var merge = StackMergePlan.Next([4, 4, 4], wanted: 5, maxStackSize: 99);
        Assert.Equal(new StackMerge(From: 1, To: 0), merge);
    }

    [Fact]
    public void Next_PicksTheLargestSlotAsTheDestination()
    {
        var merge = StackMergePlan.Next([2, 7, 3], wanted: 9, maxStackSize: 99);
        Assert.Equal(new StackMerge(From: 2, To: 1), merge);
    }

    [Fact]
    public void Next_NotEnoughInTotal_IsNothingToDo()
    {
        Assert.Null(StackMergePlan.Next([2, 2], wanted: 9, maxStackSize: 99));
    }

    [Fact]
    public void Next_OneSlotShortOfEnough_IsNothingToDo()
    {
        Assert.Null(StackMergePlan.Next([4], wanted: 5, maxStackSize: 99));
    }

    [Fact]
    public void Next_MoreWantedThanAStackCanHold_IsNothingToDo()
    {
        // No amount of merging fits nine into a slot that caps at eight.
        Assert.Null(StackMergePlan.Next([5, 5], wanted: 9, maxStackSize: 8));
    }

    [Fact]
    public void Next_FillsTheDestinationToItsCap()
    {
        var merge = StackMergePlan.Next([6, 6, 6], wanted: 8, maxStackSize: 8);
        Assert.Equal(new StackMerge(From: 1, To: 0), merge);
    }

    [Fact]
    public void Next_NoSlots_IsNothingToDo()
    {
        Assert.Null(StackMergePlan.Next([], wanted: 5, maxStackSize: 99));
    }

    [Fact]
    public void Next_UnknownStackSize_IsNothingToDo()
    {
        Assert.Null(StackMergePlan.Next([4, 4], wanted: 5, maxStackSize: 0));
    }
}
