using FoodVision.Api.Features.Analysis;
using FoodVision.Api.Infrastructure;
using FoodVision.Api.Presentation;
using Microsoft.AspNetCore.Http.Features;
using FoodVision.Api.Benchmark;
using FoodVision.Api.Domain;
using FoodVision.VideoAnalytics.Infrastructure.Postgres;
using FoodVision.Shared.Framework.Database;
using FoodVision.Shared.Core.Abstractions;

var builder = WebApplication.CreateBuilder(args);
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("FoodVision")))
    builder.Services.AddVideoAnalyticsPostgres(builder.Configuration);
builder.Services.AddVideoAnalyticsInference(builder.Configuration);
builder.Services.AddSingleton<IBenchmarkRunner, BenchmarkRunner>();
builder.Services.AddHandlers(typeof(AnalyzeVideoHandler).Assembly);
builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = long.MaxValue);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
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
app.Map("/api/benchmark", (HttpContext context, IBenchmarkRunner runner) => runner.Run(context));
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.Run();
