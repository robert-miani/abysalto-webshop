namespace CartService.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

internal static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// The one place that decides how the service talks to PostgreSQL. The running service, the tests, and the
    /// design-time factory all use it.
    /// </summary>
    public static DbContextOptionsBuilder UseCartDatabase(this DbContextOptionsBuilder builder, string connectionString)
    {
        return builder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();
    }
}
