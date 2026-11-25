namespace FoodVision.Api.Infrastructure;

/// <summary>
/// Holds the prepared tensor for each model used to analyze one frame. Inputs
/// with identical shapes share a buffer because model calls are sequential.
/// </summary>
internal sealed class InferenceWorkspace
{
    public InferenceWorkspace(YoloOnnxModel tableDetector, YoloOnnxModel? personPose, PersonDetector? personDetector, bool rfdetrKeypoint)
    {
        TableInput = new InferenceInput(tableDetector.InputWidth, tableDetector.InputHeight);
        PoseInput = personPose is null || SameShape(TableInput, personPose.InputWidth, personPose.InputHeight)
            ? TableInput
            : new InferenceInput(personPose.InputWidth, personPose.InputHeight);
        PersonInput = personDetector is null || SameShape(TableInput, personDetector.InputWidth, personDetector.InputHeight)
            ? TableInput
            : SameShape(PoseInput, personDetector.InputWidth, personDetector.InputHeight)
                ? PoseInput
                : new InferenceInput(personDetector.InputWidth, personDetector.InputHeight);
        RfDetrKeypointInput = rfdetrKeypoint ? new RfDetrKeypointInput() : null;
    }

    public InferenceInput TableInput { get; }
    public InferenceInput PoseInput { get; }
    public InferenceInput PersonInput { get; }
    public RfDetrKeypointInput? RfDetrKeypointInput { get; }

    public void Prepare(byte[] bgr, int sourceWidth, int sourceHeight)
    {
        TableInput.Prepare(bgr, sourceWidth, sourceHeight);
        if (!ReferenceEquals(PoseInput, TableInput)) PoseInput.Prepare(bgr, sourceWidth, sourceHeight);
        if (!ReferenceEquals(PersonInput, TableInput) && !ReferenceEquals(PersonInput, PoseInput))
            PersonInput.Prepare(bgr, sourceWidth, sourceHeight);
        RfDetrKeypointInput?.Prepare(bgr, sourceWidth, sourceHeight);
    }

    private static bool SameShape(InferenceInput input, int width, int height) => input.Width == width && input.Height == height;
}
