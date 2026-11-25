namespace FoodVision.Api.Infrastructure;

public sealed class AnalysisOptions
{
    public string? RfdetrKeypointModelPath { get; set; } = "models/rfdetr-keypoint-preview.onnx";
    public float RfdetrKeypointConfidence { get; set; } = 0.40f;
    public string? PersonPoseModelPath { get; set; }
    public string? PersonDetectionModelPath { get; set; }
    public string TableDetectionModelPath { get; set; } = "models/yolo11x.onnx";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public int FrameSize { get; set; } = 640;
    public double SamplingFramesPerSecond { get; set; } = 1;
    public int BenchmarkGpuConcurrency { get; set; } = 2;
}
