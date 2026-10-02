namespace CartService.Architecture.Tests;

using System;
using System.Linq;
using System.Reflection;
using Shouldly;
using Xunit;

[Trait("Category", "Architecture")]
public sealed class LayerDependencyTests
{
    private const string Domain = "CartService.Domain";
    private const string Application = "CartService.Application";
    private const string Infrastructure = "CartService.Infrastructure";
    private const string Api = "CartService.Api";

    [Fact]
    public void DomainDependsOnNoOtherLayer()
    {
        ReferencedNames(Domain).ShouldNotContain(name => IsLayer(name));
    }

    [Fact]
    public void DomainDependsOnlyOnTheBaseLibrary()
    {
        ReferencedNames(Domain).ShouldAllBe(name => name.StartsWith("System.", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationDependsOnlyOnDomain()
    {
        string[] layers = ReferencedNames(Application).Where(name => IsLayer(name)).ToArray();

        layers.ShouldBeSubsetOf(new[] { Domain });
    }

    [Fact]
    public void ApplicationDoesNotDependOnInfrastructureTechnology()
    {
        string[] forbiddenPrefixes = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Azure.",
            "StackExchange.Redis",
            "Npgsql",
        };

        ReferencedNames(Application).ShouldNotContain(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void InfrastructureDoesNotDependOnApi()
    {
        ReferencedNames(Infrastructure).ShouldNotContain(Api);
    }

    private static bool IsLayer(string assemblyName)
    {
        return assemblyName.StartsWith("CartService.", StringComparison.Ordinal);
    }

    private static string[] ReferencedNames(string assemblyName)
    {
        Assembly assembly = Assembly.Load(new AssemblyName(assemblyName));
        return assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToArray();
    }
}
