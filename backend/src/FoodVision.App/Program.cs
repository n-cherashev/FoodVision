using FoodVision.Tables.Infrastructure.Postgres;
using FoodVision.Tables.Application;
using FoodVision.Orders.Infrastructure.Postgres;
using FoodVision.Orders.Application;
using FoodVision.Shared.Framework.Database;
using FoodVision.Shared.Core.Abstractions;

var builder = WebApplication.CreateBuilder(args);
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("FoodVision")))
{
    builder.Services.AddTablesPostgres(builder.Configuration);
    builder.Services.AddOrdersPostgres(builder.Configuration);
}
builder.Services.AddTablesApplication();
builder.Services.AddOrdersApplication();
builder.Services.AddHandlers(typeof(FoodVision.Tables.Application.Features.GetTableZonesHandler).Assembly);
builder.Services.AddHandlers(typeof(FoodVision.Orders.Application.Features.OpenOrderHandler).Assembly);
builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173", "http://localhost:8081"])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    foreach (var migrator in scope.ServiceProvider.GetServices<IModuleDatabaseMigrator>())
        await migrator.MigrateAsync();
}
app.UseWebSockets();
app.UseCors();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.Run();
