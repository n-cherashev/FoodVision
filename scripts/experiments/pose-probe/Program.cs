using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FoodVision.Api.Benchmark;

if (args.Length < 4 || (args[0] != "baseline" && args[0] != "pose"))
    throw new ArgumentException("baseline|pose POSE_MODEL PERSON_MODEL FRAME...");

var assembly = typeof(BenchmarkRunner).Assembly;
var poseType = assembly.GetType("FoodVision.Api.Infrastructure.YoloOnnxModel", true)!;
var personType = assembly.GetType("FoodVision.Api.Infrastructure.PersonDetector", true)!;
var inputType = assembly.GetType("FoodVision.Api.Infrastructure.InferenceInput", true)!;
using var poseModel = (IDisposable)Activator.CreateInstance(poseType, args[1], 640, true)!;
using var personModel = args[0] == "baseline" ? (IDisposable)Activator.CreateInstance(personType, args[2])! : null;
var width = (int)poseType.GetProperty("InputWidth")!.GetValue(poseModel)!;
var height = (int)poseType.GetProperty("InputHeight")!.GetValue(poseModel)!;
var poseInput = Activator.CreateInstance(inputType, width, height)!;
var personInput = Activator.CreateInstance(inputType, 640, 640)!;
var prepare = inputType.GetMethod("Prepare")!;
var poseDetect = poseType.GetMethod("Detect")!;
var personDetect = personType.GetMethod("Detect")!;
object RunPose() => poseDetect.Invoke(poseModel, [poseInput, .08f, 0])!;
object RunPerson(object poses) => personDetect.Invoke(personModel, [personInput, poses])!;

var result = new List<object>();
foreach (var path in args.Skip(3))
{
    var frame = File.ReadAllBytes(path);
    prepare.Invoke(poseInput, [frame, 640, 360]);
    if (personModel is not null) prepare.Invoke(personInput, [frame, 640, 360]);
    if (result.Count == 0) { var warm = RunPose(); if (personModel is not null) RunPerson(warm); }
    var timer = Stopwatch.StartNew();
    var poses = RunPose();
    var poseMs = timer.Elapsed.TotalMilliseconds;
    var people = personModel is null ? poses : RunPerson(poses);
    var totalMs = timer.Elapsed.TotalMilliseconds;
    result.Add(new { frame = Path.GetFileName(path), poseMs, totalMs, people });
}
Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], poseWidth = width, poseHeight = height, result }));
