namespace CartService.Infrastructure.Persistence;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Applies pending migrations before the service accepts requests, when <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/>
/// is on. Production leaves it off and applies migrations in the delivery pipeline.
/// </summary>
internal sealed class DatabaseMigrationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DatabaseOptions> _options;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(
        IServiceScopeFactory scopeFactory,
        IOptions<DatabaseOptions> options,
        ILogger<DatabaseMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.ApplyMigrationsOnStartup)
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        CartDbContext context = scope.ServiceProvider.GetRequiredService<CartDbContext>();

        _logger.LogInformation("Applying database migrations.");
        await context.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Database migrations are applied.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
