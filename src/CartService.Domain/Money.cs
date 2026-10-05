namespace CartService.Domain;

using System;

/// <summary>
/// An amount of money in one currency. Amounts are rounded to two decimals.
/// </summary>
public sealed record Money
{
    public const string Euro = "EUR";

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Eur(decimal amount)
    {
        return Create(amount, Euro);
    }

    public static Money Zero(string currency)
    {
        return Create(0m, currency);
    }

    public static Money Create(decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "An amount of money cannot be negative.");
        }

        return new Money(decimal.Round(amount, 2, MidpointRounding.AwayFromZero), currency.ToUpperInvariant());
    }

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);

        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int factor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(factor);

        return new Money(Amount * factor, Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Cannot combine {Currency} and {other.Currency}.");
        }
    }
}
