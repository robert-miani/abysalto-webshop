namespace CartService.Domain.Tests;

using System;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class MoneyTests
{
    [Fact]
    public void EurCreatesAnAmountInEuro()
    {
        Money money = Money.Eur(12.5m);

        money.Amount.ShouldBe(12.5m);
        money.Currency.ShouldBe("EUR");
    }

    [Theory]
    [InlineData(1.004, 1.00)]
    [InlineData(1.005, 1.01)]
    [InlineData(1.006, 1.01)]
    public void AmountsAreRoundedToTwoDecimalsAwayFromZero(double amount, double expected)
    {
        Money money = Money.Eur((decimal)amount);

        money.Amount.ShouldBe((decimal)expected);
    }

    [Fact]
    public void NegativeAmountIsRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Money.Eur(-0.01m));
    }

    [Fact]
    public void CurrencyIsNormalizedToUpperCase()
    {
        Money.Create(1m, "eur").ShouldBe(Money.Eur(1m));
    }

    [Fact]
    public void MoneyWithTheSameAmountAndCurrencyIsEqual()
    {
        Money.Eur(5m).ShouldBe(Money.Eur(5.00m));
    }

    [Fact]
    public void AddSumsAmountsInTheSameCurrency()
    {
        Money total = Money.Eur(1.10m).Add(Money.Eur(2.25m));

        total.ShouldBe(Money.Eur(3.35m));
    }

    [Fact]
    public void AddRejectsDifferentCurrencies()
    {
        Should.Throw<InvalidOperationException>(() => Money.Eur(1m).Add(Money.Create(1m, "HRK")));
    }

    [Fact]
    public void MultiplyScalesTheAmountByAWholeNumber()
    {
        Money.Eur(3.33m).Multiply(3).ShouldBe(Money.Eur(9.99m));
    }

    [Fact]
    public void MultiplyRejectsANegativeFactor()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Money.Eur(1m).Multiply(-1));
    }

    [Fact]
    public void ZeroIsTheNeutralElementOfAdd()
    {
        Money.Zero("EUR").Add(Money.Eur(4m)).ShouldBe(Money.Eur(4m));
    }
}
