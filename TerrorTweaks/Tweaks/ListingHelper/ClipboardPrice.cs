using System;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal static class ClipboardPrice
{
    public const int MaxPrice = 999_999_999;

    // Prices get copied from all sorts of places, so grouped digits ("12,345", "12 345",
    // "12.345") and trailing units ("12,345 gil") all parse. A leading label, a second
    // number or a k/m multiplier does not: guessing which digits are the price risks
    // relisting a whole retainer at the wrong value.
    public static bool TryParse(string? text, out int price)
    {
        price = 0;

        var span = (text ?? string.Empty).AsSpan().Trim();
        if (span.Length == 0 || !char.IsAsciiDigit(span[0]))
            return false;

        long value = 0;
        var i = 0;
        while (i < span.Length)
        {
            var c = span[i];
            if (char.IsAsciiDigit(c))
            {
                value = value * 10 + (c - '0');
                if (value > MaxPrice)
                    return false;

                i++;
                continue;
            }

            if (!IsSeparator(c))
                break;

            var digits = DigitsAfter(span, i + 1);
            if (digits == 3)
            {
                i++;
                continue;
            }

            // A one or two digit tail behind a decimal point is a fraction, not gil. Only a
            // dot counts: a comma that is not grouping three digits is too ambiguous to read.
            if (c == '.' && digits is 1 or 2 && i + 1 + digits == span.Length)
            {
                i = span.Length;
                break;
            }

            break;
        }

        if (value < 1)
            return false;

        var rest = span[i..].TrimStart();
        if (rest.Length > 0 && rest[0] is 'k' or 'K' or 'm' or 'M')
            return false;

        foreach (var c in rest)
        {
            if (char.IsAsciiDigit(c))
                return false;
        }

        price = (int)value;
        return true;
    }

    private static bool IsSeparator(char c) => c is ',' or '.' or '_' or '\'' || char.IsWhiteSpace(c);

    private static int DigitsAfter(ReadOnlySpan<char> span, int start)
    {
        var count = 0;
        while (start + count < span.Length && char.IsAsciiDigit(span[start + count]))
            count++;

        return count;
    }
}
