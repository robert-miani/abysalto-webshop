namespace CartService.Infrastructure.Persistence;

using System.ComponentModel.DataAnnotations;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Applies pending migrations when the service starts. Meant for local runs and tests. In production the
    /// pipeline applies migrations before the new version starts, so that several instances never race.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; init; }
}
