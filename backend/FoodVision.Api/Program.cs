using FoodVision.Api.Features.Analysis;
using FoodVision.Api.Infrastructure;
using FoodVision.Api.Presentation;
using Microsoft.AspNetCore.Http.Features;
using FoodVision.Api.Benchmark;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<AnalysisOptions>(builder.Configuration.GetSection("Analysis"));
var analysisOptions = builder.Configuration.GetSection("Analysis").Get<AnalysisOptions>() ?? new AnalysisOptions();
GpuInferenceGate.Configure(analysisOptions.BenchmarkGpuConcurrency);
builder.Services.AddSingleton<InferenceRuntime>();
builder.Services.AddSingleton<IDispatcher, Dispatcher>();
builder.Services.AddSingleton<IVideoAnalyzer, VideoAnalyzer>();
builder.Services.AddSingleton<BenchmarkRunner>();
builder.Services.AddAnalysisHandlers();
builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = long.MaxValue);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseWebSockets();
app.UseCors();
app.MapControllers();
app.Map("/api/benchmark", (HttpContext context, BenchmarkRunner runner) => runner.Run(context));
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.Run();
