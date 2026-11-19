namespace FoodVision.Api.Infrastructure;

public sealed class AnalysisOptions
{
    public string PersonPoseModelPath { get; set; } = "models/yolo11x-pose.onnx";
    public string TableDetectionModelPath { get; set; } = "models/yolo11x.onnx";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public int FrameSize { get; set; } = 640;
    public double SamplingFramesPerSecond { get; set; } = 1;
}
