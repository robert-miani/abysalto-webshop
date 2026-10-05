namespace CartService.Api.IntegrationTests.Catalog;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;
using CartService.Infrastructure.Catalog;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ConfiguredProductCatalogTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AConfiguredProductIsFoundWithItsNameAndEuroPrice()
    {
        ConfiguredProductCatalog catalog = CreateCatalog(new ProductOptions { Id = "tee", Name = "T-shirt", Price = 19.9m });

        CatalogProduct? product = await catalog.FindAsync("tee", Token);

        product.ShouldNotBeNull();
        product.ProductId.ShouldBe("tee");
        product.Name.ShouldBe("T-shirt");
        product.UnitPrice.ShouldBe(Money.Eur(19.90m));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("TEE")]
    [InlineData("")]
    public async Task AnUnknownProductIdIsNotFound(string productId)
    {
        ConfiguredProductCatalog catalog = CreateCatalog(new ProductOptions { Id = "tee", Name = "T-shirt", Price = 19.9m });

        (await catalog.FindAsync(productId, Token)).ShouldBeNull();
    }

    [Fact]
    public void AnEmptyCatalogIsInvalid()
    {
        ValidateOptionsResult result = Validate();

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void ADuplicateProductIdIsInvalid()
    {
        ValidateOptionsResult result = Validate(
            new ProductOptions { Id = "tee", Name = "T-shirt", Price = 10m },
            new ProductOptions { Id = "tee", Name = "Another T-shirt", Price = 12m });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("tee");
    }

    [Fact]
    public void ACatalogWithDistinctProductsIsValid()
    {
        ValidateOptionsResult result = Validate(
            new ProductOptions { Id = "tee", Name = "T-shirt", Price = 10m },
            new ProductOptions { Id = "cap", Name = "Cap", Price = 5m });

        result.Succeeded.ShouldBeTrue();
    }

    private static ConfiguredProductCatalog CreateCatalog(params ProductOptions[] products)
    {
        return new ConfiguredProductCatalog(Options.Create(new ProductCatalogOptions { Products = new List<ProductOptions>(products) }));
    }

    private static ValidateOptionsResult Validate(params ProductOptions[] products)
    {
        ProductCatalogOptions options = new ProductCatalogOptions { Products = new List<ProductOptions>(products) };

        return new ProductCatalogOptionsValidator().Validate(null, options);
    }
}
