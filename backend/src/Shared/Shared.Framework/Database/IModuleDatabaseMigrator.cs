namespace FoodVision.Shared.Framework.Database;

/// <summary>
/// Applies the migrations owned by one module.
/// </summary>
public interface IModuleDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
