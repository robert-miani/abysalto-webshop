namespace CartService.Api.IntegrationTests.Authentication;

using CartService.Api.Authentication;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class CartAuthenticationOptionsValidatorTests
{
    private const string LongKey = "a-development-key-that-is-long-enough-0123";

    [Fact]
    public void ProductionNeedsAnAuthority()
    {
        ValidateOptionsResult result = Validate(Environments.Production, new CartAuthenticationOptions { Audience = "api" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authority");
    }

    [Fact]
    public void ProductionRefusesADevelopmentSigningKey()
    {
        ValidateOptionsResult result = Validate(
            Environments.Production,
            new CartAuthenticationOptions { Audience = "api", Authority = "https://login.example.com/tenant", DevelopmentSigningKey = LongKey });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("DevelopmentSigningKey");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("UAT")]
    [InlineData("production")]
    [InlineData("SomethingNobodyThoughtOf")]
    public void EveryEnvironmentThatIsNotLocalRefusesADevelopmentSigningKey(string environment)
    {
        ValidateOptionsResult result = Validate(
            environment,
            new CartAuthenticationOptions { Audience = "api", Authority = "https://login.example.com/tenant", DevelopmentSigningKey = LongKey });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("DevelopmentSigningKey");
        result.FailureMessage.ShouldContain(environment);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("UAT")]
    public void EveryEnvironmentThatIsNotLocalNeedsAnAuthority(string environment)
    {
        ValidateOptionsResult result = Validate(environment, new CartAuthenticationOptions { Audience = "api", DevelopmentSigningKey = LongKey });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authority");
    }

    [Fact]
    public void StagingWithAnAuthorityIsValid()
    {
        ValidateOptionsResult result = Validate(
            "Staging",
            new CartAuthenticationOptions { Audience = "api", Authority = "https://login.example.com/tenant" });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void TheTestEnvironmentMayUseADevelopmentSigningKey()
    {
        ValidateOptionsResult result = Validate("Testing", new CartAuthenticationOptions { Audience = "api", DevelopmentSigningKey = LongKey });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void ProductionWithAnAuthorityIsValid()
    {
        ValidateOptionsResult result = Validate(
            Environments.Production,
            new CartAuthenticationOptions { Audience = "api", Authority = "https://login.example.com/tenant" });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void DevelopmentWithALongEnoughKeyIsValid()
    {
        ValidateOptionsResult result = Validate(
            Environments.Development,
            new CartAuthenticationOptions { Audience = "api", DevelopmentSigningKey = LongKey });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void DevelopmentRejectsAKeyThatIsTooShort()
    {
        ValidateOptionsResult result = Validate(
            Environments.Development,
            new CartAuthenticationOptions { Audience = "api", DevelopmentSigningKey = "short" });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void DevelopmentNeedsAnAuthorityOrAKey()
    {
        ValidateOptionsResult result = Validate(Environments.Development, new CartAuthenticationOptions { Audience = "api" });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void DevelopmentCanAlsoUseARealAuthority()
    {
        ValidateOptionsResult result = Validate(
            Environments.Development,
            new CartAuthenticationOptions { Audience = "api", Authority = "https://login.example.com/tenant" });

        result.Succeeded.ShouldBeTrue();
    }

    private static ValidateOptionsResult Validate(string environment, CartAuthenticationOptions options)
    {
        return new CartAuthenticationOptionsValidator(new TestHostEnvironment(environment)).Validate(null, options);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
