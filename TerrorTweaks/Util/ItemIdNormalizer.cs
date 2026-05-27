namespace TerrorTweaks.Util;

internal static class ItemIdNormalizer
{
    // Context menus and item links encode quality in the item id: +1,000,000 for HQ,
    // +500,000 for collectible. Strip the offset to look the row up in the Item sheet.
    public static uint ToBaseItemId(uint itemId) => itemId switch
    {
        >= 1_000_000 => itemId - 1_000_000,
        >= 500_000   => itemId - 500_000,
        _            => itemId,
    };
}
