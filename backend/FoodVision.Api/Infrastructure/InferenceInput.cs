using Microsoft.ML.OnnxRuntime.Tensors;

namespace FoodVision.Api.Infrastructure;

/// <summary>
/// Reusable letterboxed RGB tensor for one inference worker. A buffer must not
/// be shared by overlapping inference calls.
/// </summary>
internal sealed class InferenceInput
{
    private const float PaddingValue = 114f / 255f;
    private const float ChannelScale = 1f / 255f;
    private readonly int[] _shape;
    private readonly int[] _sourceX;
    private readonly int[] _sourceY;
    private bool _mapsInitialized;
    private int _lastWidth;
    private int _lastHeight;

    public InferenceInput(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        _shape = [1, 3, height, width];
        Tensor = new DenseTensor<float>(_shape);
        _sourceX = new int[width];
        _sourceY = new int[height];
        Tensor.Buffer.Span.Fill(PaddingValue);
    }

    public int Width { get; }
    public int Height { get; }
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }
    public float Scale { get; private set; }
    public int PadX { get; private set; }
    public int PadY { get; private set; }
    public DenseTensor<float> Tensor { get; }

    public void Prepare(byte[] bgr, int sourceWidth, int sourceHeight)
    {
        ArgumentNullException.ThrowIfNull(bgr);
        if (sourceWidth <= 0 || sourceHeight <= 0 || bgr.Length < checked(sourceWidth * sourceHeight * 3))
            throw new ArgumentException("Кадр BGR имеет некорректный размер.", nameof(bgr));

        var scale = Math.Min((float)Width / sourceWidth, (float)Height / sourceHeight);
        var scaledWidth = (int)Math.Round(sourceWidth * scale);
        var scaledHeight = (int)Math.Round(sourceHeight * scale);
        var padX = (Width - scaledWidth) / 2;
        var padY = (Height - scaledHeight) / 2;
        var layoutChanged = !_mapsInitialized || sourceWidth != _lastWidth || sourceHeight != _lastHeight;

        if (layoutChanged)
        {
            Tensor.Buffer.Span.Fill(PaddingValue);
            for (var x = 0; x < scaledWidth; x++)
                _sourceX[x] = Math.Min(sourceWidth - 1, (int)(x / scale));
            for (var y = 0; y < scaledHeight; y++)
                _sourceY[y] = Math.Min(sourceHeight - 1, (int)(y / scale));
            _lastWidth = sourceWidth;
            _lastHeight = sourceHeight;
            _mapsInitialized = true;
        }

        var plane = Width * Height;
        var tensor = Tensor.Buffer.Span;
        for (var y = 0; y < scaledHeight; y++)
        {
            var sourceOffset = _sourceY[y] * sourceWidth * 3;
            var targetOffset = (y + padY) * Width + padX;
            for (var x = 0; x < scaledWidth; x++)
            {
                var source = sourceOffset + _sourceX[x] * 3;
                var target = targetOffset + x;
                tensor[target] = bgr[source + 2] * ChannelScale;
                tensor[plane + target] = bgr[source + 1] * ChannelScale;
                tensor[2 * plane + target] = bgr[source] * ChannelScale;
            }
        }

        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Scale = scale;
        PadX = padX;
        PadY = padY;
    }
}
