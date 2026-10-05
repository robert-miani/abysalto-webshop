namespace CartService.Api.IntegrationTests.RateLimiting;

using System;
using CartService.Api.RateLimiting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

public sealed class RateLimitingOptionsValidatorTests
{
    private readonly RateLimitingOptionsValidator _validator = new RateLimitingOptionsValidator();

    [Fact]
    public void TheDefaultsAreValid()
    {
        ValidateOptionsResult result = _validator.Validate(null, new RateLimitingOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(100_001, 1, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 11, 1)]
    [InlineData(10, 1, 0)]
    [InlineData(10, 1, 3601)]
    public void ABucketThatMakesNoSenseIsRefused(int tokenLimit, int tokensPerPeriod, int periodSeconds)
    {
        RateLimitingOptions options = new RateLimitingOptions
        {
            Strict = new RateLimitBucket
            {
                TokenLimit = tokenLimit,
                TokensPerPeriod = tokensPerPeriod,
                ReplenishmentPeriod = TimeSpan.FromSeconds(periodSeconds),
            },
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage!.ShouldContain("RateLimiting:Strict");
    }

    [Fact]
    public void AMissingBucketIsRefused()
    {
        RateLimitingOptions options = new RateLimitingOptions { Default = null! };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.FailureMessage!.ShouldContain("RateLimiting:Default is required");
    }
}
