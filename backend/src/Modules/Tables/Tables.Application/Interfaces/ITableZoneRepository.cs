using FoodVision.Tables.Domain;

namespace FoodVision.Tables.Application.Interfaces;

public interface ITableZoneRepository
{
    Task<IReadOnlyList<TableZone>> GetAll(string restaurantId = "default", CancellationToken cancellationToken = default);
    Task SaveAll(IEnumerable<TableZone> zones, string restaurantId = "default", CancellationToken cancellationToken = default);
}
