using FoodVision.Shared.Framework.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodVision.Tables.Infrastructure.Postgres;

public sealed class TablesDatabaseMigrator(TablesDbContext dbContext) : IModuleDatabaseMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.MigrateAsync(cancellationToken);
}
