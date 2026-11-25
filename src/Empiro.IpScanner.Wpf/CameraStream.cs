using Emgu.CV;
using System.IO;

namespace Empiro.IpScanner.Wpf;

internal sealed class CameraStream(LatestFrameSlot frames, Action<string> report)
{
    public async Task RunAsync(string rtspUrl, CancellationToken cancellationToken)
    {
        var retry = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // FFmpeg reads these options when VideoCapture is constructed.
                Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "rtsp_transport;tcp|stimeout;5000000");
                using var capture = new VideoCapture(rtspUrl, VideoCapture.API.Ffmpeg);
                if (!capture.IsOpened) throw new IOException("Could not open RTSP stream.");
                report("Connected");
                retry = TimeSpan.FromSeconds(1);
                using var frame = new Mat();
                while (!cancellationToken.IsCancellationRequested && capture.Read(frame))
                {
                    if (!frame.IsEmpty) frames.Publish(frame);
                }
                if (!cancellationToken.IsCancellationRequested)
                    throw new IOException("The RTSP stream ended.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                report($"Stream interrupted: {ex.Message}; reconnecting in {retry.TotalSeconds:0}s");
                try { await Task.Delay(retry, cancellationToken); }
                catch (OperationCanceledException) { break; }
                retry = TimeSpan.FromSeconds(Math.Min(retry.TotalSeconds * 2, 20));
            }
        }
    }
}
