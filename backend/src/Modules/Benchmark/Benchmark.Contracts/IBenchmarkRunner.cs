using Microsoft.AspNetCore.Http;

namespace FoodVision.Api.Benchmark;

public interface IBenchmarkRunner
{
    Task Run(HttpContext context);
}
