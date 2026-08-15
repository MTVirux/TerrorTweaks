using TerrorTweaks.Tweaks.ListingHelper;

namespace TerrorTweaks.Tests;

public class UniversalisFallbackTests
{
    private const string Home = "Phoenix";
    private const string DataCentre = "Light";

    private static UniversalisItem Item(
        IReadOnlyList<UniversalisListing>? listings = null,
        IReadOnlyList<UniversalisSale>? sales = null) =>
        new(listings ?? [], sales ?? []);

    private static UniversalisPrice Resolve(
        UniversalisItem item,
        UniversalisSource source,
        bool highQuality = true,
        bool ignoreQuality = false,
        int undercutGil = 1) =>
        UniversalisFallback.Resolve(item, source, highQuality, ignoreQuality, undercutGil, Home, DataCentre);

    [Fact]
    public void Cheapest_UndercutsTheLowestListingOnTheDataCentre()
    {
        var item = Item([
            new(1000, true, "Phoenix"),
            new(800, true, "Odin"),
            new(950, true, "Lich"),
        ]);

        var result = Resolve(item, UniversalisSource.CheapestListing);

        Assert.Equal(UniversalisBasis.Listing, result.Basis);
        Assert.Equal(799, result.Price);
        Assert.Equal("Odin", result.Scope);
    }

    [Fact]
    public void Cheapest_IgnoresListingsOfTheOtherQuality()
    {
        var item = Item([
            new(100, false, "Odin"),
            new(800, true, "Lich"),
        ]);

        var result = Resolve(item, UniversalisSource.CheapestListing);

        Assert.Equal(799, result.Price);
        Assert.Equal("Lich", result.Scope);
    }

    [Fact]
    public void Cheapest_IgnoreQuality_TakesTheCheapestOfEitherQuality()
    {
        var item = Item([
            new(100, false, "Odin"),
            new(800, true, "Lich"),
        ]);

        var result = Resolve(item, UniversalisSource.CheapestListing, ignoreQuality: true);

        Assert.Equal(99, result.Price);
        Assert.Equal("Odin", result.Scope);
    }

    [Fact]
    public void Cheapest_FloorsAtMinPrice()
    {
        var item = Item([new(3, true, "Odin")]);

        var result = Resolve(item, UniversalisSource.CheapestListing, undercutGil: 10);

        Assert.Equal(UndercutCalculator.MinPrice, result.Price);
    }

    [Fact]
    public void Cheapest_NothingListedOnTheDataCentre_ReportsNothing()
    {
        var item = Item(sales: [new(500, true, "Phoenix")]);

        var result = Resolve(item, UniversalisSource.CheapestListing);

        Assert.Equal(UniversalisBasis.None, result.Basis);
        Assert.Equal(0, result.Price);
    }

    [Fact]
    public void SaleAverage_AveragesRecentHomeWorldSales()
    {
        var item = Item(sales: [
            new(1000, true, "Phoenix"),
            new(1200, true, "Phoenix"),
            new(50, true, "Odin"),
        ]);

        var result = Resolve(item, UniversalisSource.SaleAverage);

        Assert.Equal(UniversalisBasis.SaleAverage, result.Basis);
        Assert.Equal(1100, result.Price);
        Assert.Equal(Home, result.Scope);
    }

    [Fact]
    public void SaleAverage_TakesNoUndercut()
    {
        var item = Item(sales: [new(1000, true, "Phoenix")]);

        var result = Resolve(item, UniversalisSource.SaleAverage, undercutGil: 500);

        Assert.Equal(1000, result.Price);
    }

    [Fact]
    public void SaleAverage_RoundsToTheNearestGil()
    {
        var item = Item(sales: [
            new(100, true, "Phoenix"),
            new(101, true, "Phoenix"),
        ]);

        var result = Resolve(item, UniversalisSource.SaleAverage);

        Assert.Equal(101, result.Price);
    }

    [Fact]
    public void SaleAverage_IgnoresSalesOfTheOtherQuality()
    {
        var item = Item(sales: [
            new(100, false, "Phoenix"),
            new(900, true, "Phoenix"),
        ]);

        var result = Resolve(item, UniversalisSource.SaleAverage);

        Assert.Equal(900, result.Price);
    }

    [Fact]
    public void SaleAverage_NoHomeWorldSales_FallsBackToTheDataCentre()
    {
        var item = Item(sales: [
            new(800, true, "Odin"),
            new(1000, true, "Lich"),
        ]);

        var result = Resolve(item, UniversalisSource.SaleAverage);

        Assert.Equal(UniversalisBasis.SaleAverage, result.Basis);
        Assert.Equal(900, result.Price);
        Assert.Equal(DataCentre, result.Scope);
    }

    [Fact]
    public void SaleAverage_NoSalesAnywhere_ReportsNothing()
    {
        var item = Item([new(500, true, "Odin")]);

        var result = Resolve(item, UniversalisSource.SaleAverage);

        Assert.Equal(UniversalisBasis.None, result.Basis);
    }

    [Fact]
    public void CheapestThenSaleAverage_PrefersALiveListing()
    {
        var item = Item(
            [new(500, true, "Odin")],
            [new(9000, true, "Phoenix")]);

        var result = Resolve(item, UniversalisSource.CheapestThenSaleAverage);

        Assert.Equal(UniversalisBasis.Listing, result.Basis);
        Assert.Equal(499, result.Price);
    }

    [Fact]
    public void CheapestThenSaleAverage_EmptyDataCentre_UsesTheSaleAverage()
    {
        var item = Item(sales: [new(9000, true, "Phoenix")]);

        var result = Resolve(item, UniversalisSource.CheapestThenSaleAverage);

        Assert.Equal(UniversalisBasis.SaleAverage, result.Basis);
        Assert.Equal(9000, result.Price);
    }

    [Fact]
    public void CheapestThenSaleAverage_OnlyOtherQualityAround_ReportsNothing()
    {
        var item = Item(
            [new(500, false, "Odin")],
            [new(9000, false, "Phoenix")]);

        var result = Resolve(item, UniversalisSource.CheapestThenSaleAverage);

        Assert.Equal(UniversalisBasis.None, result.Basis);
    }

    [Fact]
    public void Resolve_NothingAtAll_ReportsNothing()
    {
        var result = Resolve(Item(), UniversalisSource.CheapestThenSaleAverage);

        Assert.Equal(UniversalisBasis.None, result.Basis);
        Assert.Equal(0, result.Price);
    }

    [Fact]
    public void Resolve_ClampsAtMaxPrice()
    {
        var item = Item([new(4_000_000_000, true, "Odin")]);

        var result = Resolve(item, UniversalisSource.CheapestListing, undercutGil: 0);

        Assert.Equal(UndercutCalculator.MaxPrice, result.Price);
    }
}
