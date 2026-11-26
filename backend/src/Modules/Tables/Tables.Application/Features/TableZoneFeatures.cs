using CSharpFunctionalExtensions;
using FoodVision.Shared.Core.Abstractions;
using FoodVision.Tables.Application.Interfaces;
using FoodVision.Tables.Contracts;
using FoodVision.Tables.Domain;

namespace FoodVision.Tables.Application.Features;

public sealed record GetTableZonesQuery(string RestaurantId = "default") : ICommand;

public sealed class GetTableZonesHandler(ITableZoneRepository repository)
    : ICommandHandler<IReadOnlyList<TableZoneDto>, GetTableZonesQuery>
{
    public async Task<Result<IReadOnlyList<TableZoneDto>, string>> Handle(
        GetTableZonesQuery query,
        CancellationToken cancellationToken = default)
    {
        var zones = await repository.GetAll(query.RestaurantId, cancellationToken);
        var dtos = zones.Select(z => new TableZoneDto(
            z.Id,
            z.Name,
            z.Capacity,
            z.Geometry.DisplayPolygon.Select(p => new TablePointDto(p.X, p.Y)).ToList(),
            z.Geometry.CountingPolygon.Select(p => new TablePointDto(p.X, p.Y)).ToList(),
            z.IsActive
        )).ToList();

        return Result.Success<IReadOnlyList<TableZoneDto>, string>(dtos);
    }
}

public sealed record SaveTableZonesCommand(
    IReadOnlyList<TableZoneDto> Zones,
    string RestaurantId = "default") : ICommand;

public sealed class SaveTableZonesHandler(ITableZoneRepository repository)
    : ICommandHandler<IReadOnlyList<TableZoneDto>, SaveTableZonesCommand>
{
    public async Task<Result<IReadOnlyList<TableZoneDto>, string>> Handle(
        SaveTableZonesCommand command,
        CancellationToken cancellationToken = default)
    {
        var entities = command.Zones.Select(dto =>
            new TableZone(
                dto.Id,
                dto.Name,
                dto.Capacity,
                new TableGeometry(
                    dto.DisplayPolygon.Select(p => new TablePoint(p.X, p.Y)).ToList(),
                    dto.CountingPolygon.Select(p => new TablePoint(p.X, p.Y)).ToList()
                ),
                command.RestaurantId
            )
        ).ToList();

        await repository.SaveAll(entities, command.RestaurantId, cancellationToken);

        var resultDtos = entities.Select(z => new TableZoneDto(
            z.Id,
            z.Name,
            z.Capacity,
            z.Geometry.DisplayPolygon.Select(p => new TablePointDto(p.X, p.Y)).ToList(),
            z.Geometry.CountingPolygon.Select(p => new TablePointDto(p.X, p.Y)).ToList(),
            z.IsActive
        )).ToList();

        return Result.Success<IReadOnlyList<TableZoneDto>, string>(resultDtos);
    }
}
