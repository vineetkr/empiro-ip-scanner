using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.IO;

namespace Empiro.IpScanner.Wpf;

internal sealed record Detection(float X, float Y, float Width, float Height, string Label, float Confidence);

/// <summary>Ultralytics YOLOv8/11 detection ONNX adapter for standard [1,84,8400] / [1,8400,84] exports.</summary>
internal sealed class YoloDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly string[] _labels;
    public bool UsingCuda { get; }

    public YoloDetector(string modelPath)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException("Choose an ONNX model file first.", modelPath);
        SessionOptions? gpuOptions = null;
        InferenceSession? session = null;
        try
        {
            gpuOptions = new SessionOptions();
            gpuOptions.AppendExecutionProvider_CUDA(0);
            session = new InferenceSession(modelPath, gpuOptions);
            UsingCuda = true;
        }
        catch (Exception)
        {
            session?.Dispose();
            session = new InferenceSession(modelPath);
            UsingCuda = false;
        }
        finally { gpuOptions?.Dispose(); }
        _session = session!;
        var input = _session.InputMetadata.First();
        _inputName = input.Key;
        var dims = input.Value.Dimensions;
        _inputHeight = dims.Length > 2 && dims[^2] > 0 ? dims[^2] : 640;
        _inputWidth = dims.Length > 3 && dims[^1] > 0 ? dims[^1] : 640;
        _outputName = _session.OutputMetadata.Keys.First();
        var labelsPath = Path.Combine(Path.GetDirectoryName(modelPath)!, "coco.names");
        _labels = File.Exists(labelsPath) ? File.ReadAllLines(labelsPath) : CocoLabels;
    }

    public IReadOnlyList<Detection> Detect(Mat bgr, float threshold)
    {
        // Letterbox with the conventional gray pad so coordinates and object proportions
        // match Ultralytics detection exports instead of stretching the camera aspect ratio.
        var scale = Math.Min((float)_inputWidth / bgr.Width, (float)_inputHeight / bgr.Height);
        var resizedWidth = Math.Max(1, (int)Math.Round(bgr.Width * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(bgr.Height * scale));
        var padX = (_inputWidth - resizedWidth) / 2;
        var padY = (_inputHeight - resizedHeight) / 2;
        using var resized = new Mat();
        CvInvoke.Resize(bgr, resized, new System.Drawing.Size(resizedWidth, resizedHeight));
        using var letterboxed = new Mat(_inputHeight, _inputWidth, DepthType.Cv8U, 3);
        letterboxed.SetTo(new MCvScalar(114, 114, 114));
        using (var region = new Mat(letterboxed, new System.Drawing.Rectangle(padX, padY, resizedWidth, resizedHeight)))
            resized.CopyTo(region);
        using var rgb = new Mat();
        CvInvoke.CvtColor(letterboxed, rgb, ColorConversion.Bgr2Rgb);
        using var image = rgb.ToImage<Rgb, byte>();
        var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
        for (var y = 0; y < _inputHeight; y++)
        for (var x = 0; x < _inputWidth; x++)
        {
            var pixel = image[y, x];
            tensor[0, 0, y, x] = (float)(pixel.Red / 255.0);
            tensor[0, 1, y, x] = (float)(pixel.Green / 255.0);
            tensor[0, 2, y, x] = (float)(pixel.Blue / 255.0);
        }

        var input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
        using var outputs = _session.Run([input]);
        var output = outputs.First(o => o.Name == _outputName).AsTensor<float>();
        var dims = output.Dimensions.ToArray();
        if (dims.Length != 3) throw new InvalidDataException("Expected a three-dimensional YOLO detection output.");
        var channelsFirst = dims[1] < dims[2];
        var attributes = channelsFirst ? dims[1] : dims[2];
        var candidates = channelsFirst ? dims[2] : dims[1];
        if (attributes < 5) throw new InvalidDataException("The ONNX output does not look like a YOLO detection head.");
        float At(int candidate, int channel) => channelsFirst ? output[0, channel, candidate] : output[0, candidate, channel];

        var found = new List<Detection>();
        for (var i = 0; i < candidates; i++)
        {
            var bestClass = 0;
            var score = 0f;
            for (var c = 4; c < attributes; c++)
            {
                var value = At(i, c);
                if (value > score) { score = value; bestClass = c - 4; }
            }
            if (score < threshold) continue;
            var cx = At(i, 0); var cy = At(i, 1); var width = At(i, 2); var height = At(i, 3);
            var x1 = Math.Clamp(((cx - width / 2) - padX) / scale, 0, bgr.Width);
            var y1 = Math.Clamp(((cy - height / 2) - padY) / scale, 0, bgr.Height);
            var x2 = Math.Clamp(((cx + width / 2) - padX) / scale, 0, bgr.Width);
            var y2 = Math.Clamp(((cy + height / 2) - padY) / scale, 0, bgr.Height);
            found.Add(new Detection(
                x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1),
                bestClass < _labels.Length ? _labels[bestClass] : $"class {bestClass}", score));
        }
        return NonMaximumSuppression(found, 0.45f);
    }

    private static IReadOnlyList<Detection> NonMaximumSuppression(List<Detection> boxes, float iouThreshold)
    {
        var result = new List<Detection>();
        foreach (var candidate in boxes.OrderByDescending(x => x.Confidence))
        {
            if (result.Any(kept => kept.Label == candidate.Label && IoU(kept, candidate) > iouThreshold)) continue;
            result.Add(candidate);
        }
        return result;
    }

    private static float IoU(Detection a, Detection b)
    {
        var w = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
        var h = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        var intersection = w * h;
        return intersection / Math.Max(a.Width * a.Height + b.Width * b.Height - intersection, 1);
    }

    public void Dispose() => _session.Dispose();

    private static readonly string[] CocoLabels = ("person,bicycle,car,motorcycle,airplane,bus,train,truck,boat,traffic light," +
        "fire hydrant,stop sign,parking meter,bench,bird,cat,dog,horse,sheep,cow,elephant,bear,zebra,giraffe," +
        "backpack,umbrella,handbag,tie,suitcase,frisbee,skis,snowboard,sports ball,kite,baseball bat,baseball glove," +
        "skateboard,surfboard,tennis racket,bottle,wine glass,cup,fork,knife,spoon,bowl,banana,apple,sandwich,orange," +
        "broccoli,carrot,hot dog,pizza,donut,cake,chair,couch,potted plant,bed,dining table,toilet,tv,laptop,mouse," +
        "remote,keyboard,cell phone,microwave,oven,toaster,sink,refrigerator,book,clock,vase,scissors,teddy bear," +
        "hair drier,toothbrush").Split(',');
}
