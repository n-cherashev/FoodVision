using FoodVision.Shared.Framework.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodVision.Orders.Infrastructure.Postgres;

public sealed class OrdersDatabaseMigrator(OrdersDbContext dbContext) : IModuleDatabaseMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.MigrateAsync(cancellationToken);
}
