using FoodVision.Tables.Application.Interfaces;
using FoodVision.Tables.Domain;
using Microsoft.EntityFrameworkCore;

namespace FoodVision.Tables.Infrastructure.Postgres.Repositories;

public sealed class TableZoneRepository(TablesDbContext dbContext) : ITableZoneRepository
{
    public async Task<IReadOnlyList<TableZone>> GetAll(
        string restaurantId = "default",
        CancellationToken cancellationToken = default)
    {
        return await dbContext.TableZones
            .Where(z => z.RestaurantId == restaurantId)
            .OrderBy(z => z.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAll(
        IEnumerable<TableZone> zones,
        string restaurantId = "default",
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.TableZones
            .Where(z => z.RestaurantId == restaurantId)
            .ToListAsync(cancellationToken);

        var existingDict = existing.ToDictionary(z => z.Id);
        var inputList = zones.ToList();
        var inputIds = inputList.Select(z => z.Id).ToHashSet();

        foreach (var zone in inputList)
        {
            if (existingDict.TryGetValue(zone.Id, out var existingZone))
            {
                existingZone.Update(zone.Name, zone.Capacity, zone.Geometry, zone.IsActive);
            }
            else
            {
                await dbContext.TableZones.AddAsync(zone, cancellationToken);
            }
        }

        foreach (var oldZone in existing)
        {
            if (!inputIds.Contains(oldZone.Id))
            {
                dbContext.TableZones.Remove(oldZone);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
