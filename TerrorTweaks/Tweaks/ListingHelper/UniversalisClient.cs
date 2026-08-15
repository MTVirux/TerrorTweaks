using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerrorTweaks.Tweaks.ListingHelper;

// A third party price aggregator, asked only about items the board itself came back empty on,
// so an ordinary price check never leaves the game.
internal static class UniversalisClient
{
    private const int ListingsWanted = 20;
    private const int EntriesWanted = 20;

    // Well inside the pacing between two lookups, so a slow answer can never pile up behind the
    // next one.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Http = Build();

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    internal static async Task<UniversalisItem?> Fetch(uint itemId, string scope, CancellationToken token)
    {
        var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(scope)}/{itemId}"
                  + $"?listings={ListingsWanted}&entries={EntriesWanted}";

        try
        {
            using var response = await Http.GetAsync(url, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var parsed = await JsonSerializer.DeserializeAsync<Response>(body, Json, token).ConfigureAwait(false);

            return parsed is null ? null : Convert(parsed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            Services.Log.Warning($"Listing Helper: Universalis had no answer for item {itemId} on {scope} - {ex.Message}");
            return null;
        }
    }

    private static HttpClient Build()
    {
        var http = new HttpClient { Timeout = Timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TerrorTweaks/1.0 (Dalamud plugin)");
        return http;
    }

    private static UniversalisItem Convert(Response response)
    {
        var listings = new List<UniversalisListing>();
        foreach (var entry in response.Listings ?? [])
            listings.Add(new UniversalisListing(Price(entry.PricePerUnit), entry.Hq, entry.WorldName ?? string.Empty));

        var sales = new List<UniversalisSale>();
        foreach (var entry in response.RecentHistory ?? [])
            sales.Add(new UniversalisSale(Price(entry.PricePerUnit), entry.Hq, entry.WorldName ?? string.Empty));

        return new UniversalisItem(listings, sales);
    }

    private static uint Price(long pricePerUnit) => (uint)Math.Clamp(pricePerUnit, 0, uint.MaxValue);

    // Only the fields the fallback prices from. A data centre query is the one that tags every
    // entry with the world it came from, which is why the scope is never a single world.
    private sealed class Response
    {
        public List<Entry>? Listings { get; set; }

        public List<Entry>? RecentHistory { get; set; }
    }

    private sealed class Entry
    {
        public long PricePerUnit { get; set; }

        public bool Hq { get; set; }

        public string? WorldName { get; set; }
    }
}
