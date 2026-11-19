using FoodVision.Api.Features.Analysis;
using FoodVision.Api.Infrastructure;
using FoodVision.Api.Presentation;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<AnalysisOptions>(builder.Configuration.GetSection("Analysis"));
builder.Services.AddSingleton<IDispatcher, Dispatcher>();
builder.Services.AddSingleton<IVideoAnalyzer, VideoAnalyzer>();
builder.Services.AddAnalysisHandlers();
builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = long.MaxValue);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.Run();
