namespace CartService.Api.RateLimiting;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

/// <summary>
/// Refuses to start with a bucket that makes no sense, for example a bucket that never refills, instead of
/// failing on the first request.
/// </summary>
internal sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    private static readonly TimeSpan MinPeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxPeriod = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = new List<string>();
        Check(failures, nameof(options.Default), options.Default);
        Check(failures, nameof(options.Strict), options.Strict);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Check(List<string> failures, string name, RateLimitBucket? bucket)
    {
        string section = $"{RateLimitingOptions.SectionName}:{name}";

        if (bucket is null)
        {
            failures.Add($"{section} is required.");

            return;
        }

        if (bucket.TokenLimit is < 1 or > 100_000)
        {
            failures.Add($"{section}:TokenLimit must be between 1 and 100000.");
        }

        if (bucket.TokensPerPeriod < 1 || bucket.TokensPerPeriod > bucket.TokenLimit)
        {
            failures.Add($"{section}:TokensPerPeriod must be at least 1 and not more than TokenLimit.");
        }

        if (bucket.ReplenishmentPeriod < MinPeriod || bucket.ReplenishmentPeriod > MaxPeriod)
        {
            failures.Add($"{section}:ReplenishmentPeriod must be between 00:00:01 and 01:00:00.");
        }
    }
}
