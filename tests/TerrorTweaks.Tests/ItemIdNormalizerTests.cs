using TerrorTweaks.Util;

namespace TerrorTweaks.Tests;

public class ItemIdNormalizerTests
{
    [Fact]
    public void NormalItemId_IsUnchanged()
        => Assert.Equal(4554u, ItemIdNormalizer.ToBaseItemId(4554u));

    [Fact]
    public void HqItemId_HasOneMillionOffsetRemoved()
        => Assert.Equal(4554u, ItemIdNormalizer.ToBaseItemId(1_004_554u));

    [Fact]
    public void CollectibleItemId_HasFiveHundredThousandOffsetRemoved()
        => Assert.Equal(1234u, ItemIdNormalizer.ToBaseItemId(501_234u));

    [Fact]
    public void Zero_StaysZero()
        => Assert.Equal(0u, ItemIdNormalizer.ToBaseItemId(0u));
}
