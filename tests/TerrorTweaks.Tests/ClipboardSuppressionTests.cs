using TerrorTweaks.Util;

namespace TerrorTweaks.Tests;

public class ClipboardSuppressionTests
{
    private static readonly string Glyph = ((char)0xE0BB).ToString();

    [Fact]
    public void Clean_StripsPluginGlyphAndSpacing()
    {
        Assert.Equal("Clipboard", ClipboardSuppression.Clean($"{Glyph} Clipboard "));
        Assert.Equal("Examine", ClipboardSuppression.Clean("Examine"));
    }

    [Fact]
    public void Read_MarksPluginEntries()
    {
        var entries = ClipboardSuppression.Read(["Examine", $"{Glyph}Copy Item Name"], null);

        Assert.Equal(2, entries.Count);
        Assert.False(entries[0].FromPlugin);
        Assert.True(entries[1].FromPlugin);
        Assert.Equal("Copy Item Name", entries[1].Text);
    }

    [Fact]
    public void Read_DropsOnlyOneCopyOfOurOwnEntry()
    {
        var entries = ClipboardSuppression.Read(
            [$"{Glyph}Clipboard", $"{Glyph}Clipboard", "Examine"],
            "Clipboard");

        Assert.Equal(["Clipboard", "Examine"], entries.Select(e => e.Text));
    }

    [Fact]
    public void Read_KeepsOurEntryWhenWeDidNotInject()
    {
        var entries = ClipboardSuppression.Read([$"{Glyph}Clipboard"], null);

        Assert.Equal(["Clipboard"], entries.Select(e => e.Text));
    }

    [Fact]
    public void ShouldSuppress_MatchesSubstringIgnoringCase()
    {
        var entries = ClipboardSuppression.Read([$"{Glyph}Copy Item Name"], null);

        Assert.True(ClipboardSuppression.ShouldSuppress(entries, ["copy"]));
        Assert.False(ClipboardSuppression.ShouldSuppress(entries, ["clipboard"]));
        Assert.False(ClipboardSuppression.ShouldSuppress(entries, []));
    }

    [Fact]
    public void ShouldSuppress_IgnoresEmptyMatches()
    {
        var entries = ClipboardSuppression.Read(["Examine"], null);

        Assert.False(ClipboardSuppression.ShouldSuppress(entries, [string.Empty]));
    }

    [Fact]
    public void Learn_AddsOnlyNewPluginEntries()
    {
        var learned = new List<string> { "Copy Item Name" };
        var entries = ClipboardSuppression.Read(
            ["Examine", $"{Glyph}copy item name", $"{Glyph}Search Market Board"],
            null);

        Assert.True(ClipboardSuppression.Learn(learned, entries));
        Assert.Equal(["Copy Item Name", "Search Market Board"], learned);

        Assert.False(ClipboardSuppression.Learn(learned, entries));
    }

    [Fact]
    public void Learn_StopsAtTheCap()
    {
        var learned = Enumerable.Range(0, ClipboardSuppression.MaxLearned).Select(i => $"Entry {i}").ToList();
        var entries = ClipboardSuppression.Read([$"{Glyph}One More"], null);

        Assert.False(ClipboardSuppression.Learn(learned, entries));
        Assert.Equal(ClipboardSuppression.MaxLearned, learned.Count);
    }
}
