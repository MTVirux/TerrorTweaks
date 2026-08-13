using System;

namespace TerrorTweaks.Util;

internal enum PurchaseBlock
{
    None,
    NothingToBuy,
    NotEnoughGil,
}

internal readonly record struct PurchasePlan(
    int Amount,
    int Purchases,
    long TotalCost,
    int SlotsNeeded,
    bool SpaceWarning,
    PurchaseBlock Block)
{
    public bool CanStart => Block == PurchaseBlock.None;
}

internal static class BulkPurchasePlan
{
    // Gil vendors cap a single transaction at 99 no matter how large the item stacks -
    // Grade 5 Dark Matter stacks to 999 but still only sells 99 at a time.
    public const int MaxPerPurchase = 99;

    // "Top up" reads the entered number as a target total owned; otherwise it is a flat
    // amount to buy on top of what the player already has.
    public static int Resolve(int requested, int owned, bool topUp)
        => Math.Max(0, topUp ? requested - owned : requested);

    public static PurchasePlan Build(int amount, int unitPrice, int stackSize, long gil, int freeSlots)
    {
        if (amount <= 0)
            return new PurchasePlan(0, 0, 0, 0, false, PurchaseBlock.NothingToBuy);

        var cost = (long)amount * unitPrice;
        // Counts every item as needing fresh slots, ignoring partial stacks already held,
        // so this only ever over-estimates - hence a warning rather than a hard block.
        var slots = stackSize <= 0 ? amount : (amount + stackSize - 1) / stackSize;

        return new PurchasePlan(
            amount,
            (amount + MaxPerPurchase - 1) / MaxPerPurchase,
            cost,
            slots,
            slots > freeSlots,
            cost > gil ? PurchaseBlock.NotEnoughGil : PurchaseBlock.None);
    }

    public static int NextBatch(int remaining) => Math.Clamp(remaining, 0, MaxPerPurchase);
}
