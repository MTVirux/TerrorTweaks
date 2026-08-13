using Lumina.Excel.Sheets;
using TerrorTweaks.Framework;

namespace TerrorTweaks.Util;

internal static class ItemNames
{
    // Names read straight off the game's UI structs are SeStrings wrapped in item-link
    // payloads, which render as stray glyphs around the name. The sheet row is plain text.
    public static string Lookup(uint itemId)
    {
        var baseId = ItemIdNormalizer.ToBaseItemId(itemId);
        return Services.DataManager.GetExcelSheet<Item>().TryGetRow(baseId, out var row)
            ? row.Name.ExtractText()
            : string.Empty;
    }
}
