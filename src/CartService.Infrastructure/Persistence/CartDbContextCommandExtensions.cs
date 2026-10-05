namespace CartService.Infrastructure.Persistence;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static class CartDbContextCommandExtensions
{
    /// <summary>
    /// Creates a raw SQL command on the connection of the context, for statements that Entity Framework Core
    /// cannot express, such as <c>INSERT ... ON CONFLICT</c> and <c>UPDATE ... RETURNING</c>.
    /// </summary>
    public static async Task<NpgsqlCommand> CreateCommandAsync(this CartDbContext context, string sql, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);

        return new NpgsqlCommand(sql, (NpgsqlConnection)context.Database.GetDbConnection());
    }
}
