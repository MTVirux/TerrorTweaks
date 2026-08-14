using TerrorTweaks.Framework;

namespace TerrorTweaks.Tests;

public class TweakOrderingTests
{
    [Fact]
    public void Arrange_KeepsSavedOrder()
    {
        string[] saved = ["Charlie", "Alpha", "Bravo"];
        string[] present = ["Alpha", "Bravo", "Charlie"];

        Assert.Equal(["Charlie", "Alpha", "Bravo"], TweakOrdering.Arrange(saved, present));
    }

    [Fact]
    public void Arrange_PutsUnsavedNamesFirstAlphabetically()
    {
        string[] saved = ["Charlie", "Alpha"];
        string[] present = ["Alpha", "Charlie", "Zulu", "Bravo"];

        Assert.Equal(["Bravo", "Zulu", "Charlie", "Alpha"], TweakOrdering.Arrange(saved, present));
    }

    [Fact]
    public void Arrange_DropsSavedNamesThatAreGone()
    {
        string[] saved = ["Charlie", "Removed", "Alpha"];
        string[] present = ["Alpha", "Charlie"];

        Assert.Equal(["Charlie", "Alpha"], TweakOrdering.Arrange(saved, present));
    }

    [Fact]
    public void Arrange_EmptySavedIsAlphabetical()
    {
        string[] present = ["Charlie", "Alpha", "Bravo"];

        Assert.Equal(["Alpha", "Bravo", "Charlie"], TweakOrdering.Arrange([], present));
    }

    [Fact]
    public void Arrange_IgnoresDuplicateSavedNames()
    {
        string[] saved = ["Alpha", "Alpha", "Bravo"];
        string[] present = ["Alpha", "Bravo"];

        Assert.Equal(["Alpha", "Bravo"], TweakOrdering.Arrange(saved, present));
    }

    [Fact]
    public void Move_Down_ShiftsInterveningUp()
    {
        List<string> order = ["Alpha", "Bravo", "Charlie", "Delta"];

        TweakOrdering.Move(order, 0, 2);

        Assert.Equal(["Bravo", "Charlie", "Alpha", "Delta"], order);
    }

    [Fact]
    public void Move_Up_ShiftsInterveningDown()
    {
        List<string> order = ["Alpha", "Bravo", "Charlie", "Delta"];

        TweakOrdering.Move(order, 3, 1);

        Assert.Equal(["Alpha", "Delta", "Bravo", "Charlie"], order);
    }

    [Fact]
    public void Move_ToSameIndex_IsNoOp()
    {
        List<string> order = ["Alpha", "Bravo"];

        TweakOrdering.Move(order, 1, 1);

        Assert.Equal(["Alpha", "Bravo"], order);
    }

    [Fact]
    public void Move_OutOfRange_IsNoOp()
    {
        List<string> order = ["Alpha", "Bravo"];

        TweakOrdering.Move(order, 0, 5);
        TweakOrdering.Move(order, -1, 0);

        Assert.Equal(["Alpha", "Bravo"], order);
    }
}
