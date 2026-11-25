using Empiro.IpScanner.Core;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Empiro.IpScanner.Wpf;

public partial class CameraMonitorWindow : Window
{
    private LatestFrameSlot _frames = new();
    private CancellationTokenSource? _streamStop;
    private Task? _captureTask;
    private Task? _processingTask;
    private YoloDetector? _detector;
    private WriteableBitmap? _bitmap;
    private bool _detectionEnabled;
    private float _confidence = 0.45f;
    private bool _closing;

    public CameraMonitorWindow() => InitializeComponent();

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanStatus.Text = "Discovering ONVIF / Hikvision SADP devices on local interfaces…";
        try
        {
            var results = await CameraDiscovery.DiscoverAsync(TimeSpan.FromSeconds(4), CancellationToken.None);
            CameraList.ItemsSource = results;
            ScanStatus.Text = results.Count == 0 ? "No discovery replies. Check the physical network, or enter a reachable RTSP URL." : $"Found {results.Count} device(s). Other-subnet devices may need a route or matching local IP before streaming.";
        }
        catch (Exception ex) { ScanStatus.Text = $"Discovery failed: {ex.Message}"; }
    }

    private void CameraList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CameraList.SelectedItem is DeviceResult camera)
        {
            HostInput.Text = camera.IpAddress;
            ScanStatus.Text = $"{camera.Name} · {camera.IpAddress}. Obtain its RTSP path from camera settings or vendor documentation.";
        }
    }

    private void BuildUrl_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(HostInput.Text) || !int.TryParse(PortInput.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Enter a camera host and a port from 1 to 65535.", "RTSP address", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var builder = new UriBuilder("rtsp", HostInput.Text.Trim(), port, PathInput.Text.StartsWith('/') ? PathInput.Text : "/" + PathInput.Text)
            {
                UserName = UsernameInput.Text,
                Password = PasswordInput.Password
            };
            RtspUrl.Text = builder.Uri.AbsoluteUri;
        }
        catch (UriFormatException ex) { MessageBox.Show(this, ex.Message, "RTSP address", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void ResolveOnvif_Click(object sender, RoutedEventArgs e)
    {
        if (CameraList.SelectedItem is not DeviceResult camera)
        {
            ScanStatus.Text = "Select a discovered ONVIF camera first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(camera.OnvifServiceUrl))
        {
            ScanStatus.Text = "This discovery reply did not include a device-service address.";
            return;
        }
        try
        {
            ScanStatus.Text = "Requesting authenticated ONVIF media profiles…";
            using var media = new OnvifMediaClient(UsernameInput.Text, PasswordInput.Password);
            RtspUrl.Text = await media.GetRtspUriAsync(camera.OnvifServiceUrl, CancellationToken.None);
            ScanStatus.Text = "RTSP URI resolved. Connect to start the stream.";
        }
        catch (Exception ex) { ScanStatus.Text = $"ONVIF media lookup failed: {ex.Message}"; }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var url = RtspUrl.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "rtsp")
        {
            MessageBox.Show(this, "Enter a valid RTSP URL, including the stream path.", "RTSP URL", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await StopPipelineAsync();
        if (!string.IsNullOrWhiteSpace(ModelPath.Text))
        {
            try
            {
                _detector = new YoloDetector(ModelPath.Text);
                ModelStatus.Text = _detector.UsingCuda ? "YOLO ready · CUDA GPU" : "YOLO ready · CPU fallback";
                DetectionToggle.IsEnabled = true;
            }
            catch (Exception ex)
            {
                _detector?.Dispose(); _detector = null;
                ModelStatus.Text = $"Model unavailable: {ex.Message}";
                DetectionToggle.IsEnabled = false;
            }
        }
        else
        {
            ModelStatus.Text = "No model loaded · video-only mode";
            DetectionToggle.IsEnabled = false;
        }

        _streamStop = new CancellationTokenSource();
        var token = _streamStop.Token;
        _detectionEnabled = DetectionToggle.IsChecked == true;
        _captureTask = Task.Run(() => new CameraStream(_frames,
            text => Dispatcher.BeginInvoke(() => ConnectionStatus.Text = text)).RunAsync(url, token), token);
        _processingTask = Task.Run(() => ProcessFramesAsync(token), token);
        StopButton.IsEnabled = true;
        ConnectButton.IsEnabled = false;
        VideoPlaceholder.Visibility = Visibility.Collapsed;
    }

    private async Task ProcessFramesAsync(CancellationToken cancellationToken)
    {
        var fpsWatch = Stopwatch.StartNew();
        var frameCount = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var frame = _frames.Take(cancellationToken);
            if (frame is null) continue;
            var settings = await Dispatcher.InvokeAsync(() => (_detectionEnabled, _confidence));
            var timer = Stopwatch.StartNew();
            IReadOnlyList<Detection> detections = Array.Empty<Detection>();
            if (settings._detectionEnabled && _detector is not null)
            {
                try { detections = _detector.Detect(frame, settings._confidence); }
                catch (Exception ex)
                {
                    var message = ex.Message;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _detectionEnabled = false;
                        DetectionToggle.IsChecked = false;
                        DetectionToggle.IsEnabled = false;
                        ModelStatus.Text = $"Detection stopped: {message}";
                    });
                    _detector.Dispose();
                    _detector = null;
                }
            }
            var latency = timer.ElapsedMilliseconds;
            using var rendered = frame.Clone();
            foreach (var detection in detections)
            {
                var rect = new System.Drawing.Rectangle((int)detection.X, (int)detection.Y,
                    Math.Max(1, (int)detection.Width), Math.Max(1, (int)detection.Height));
                CvInvoke.Rectangle(rendered, rect, new MCvScalar(40, 220, 90), 2);
                CvInvoke.PutText(rendered, $"{detection.Label} {detection.Confidence:P0}",
                    new System.Drawing.Point(rect.X, Math.Max(18, rect.Y - 5)), FontFace.HersheySimplex,
                    0.6, new MCvScalar(40, 220, 90), 2);
            }
            var width = rendered.Cols; var height = rendered.Rows; var stride = width * 3;
            var pixels = new byte[stride * height];
            for (var y = 0; y < height; y++)
                Marshal.Copy(IntPtr.Add(rendered.DataPointer, checked((int)(y * rendered.Step))), pixels, y * stride, stride);
            frameCount++;
            var fps = frameCount / Math.Max(fpsWatch.Elapsed.TotalSeconds, 0.001);
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateBitmap(width, height, pixels);
                PerformanceStatus.Text = $"Preview: {fps:F1} FPS   Detection: {latency} ms   Objects: {detections.Count}";
            });
        }
    }

    private void UpdateBitmap(int width, int height, byte[] pixels)
    {
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
            VideoImage.Source = _bitmap;
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 3, 0);
    }

    private void BrowseModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ONNX model (*.onnx)|*.onnx", Title = "Select YOLOv8 / YOLO11 detection model" };
        if (dialog.ShowDialog(this) != true) return;
        ModelPath.Text = dialog.FileName;
        ModelStatus.Text = "Model selected. Connect to load and initialize inference.";
    }

    private void DetectionSetting_Changed(object sender, RoutedEventArgs e) => _detectionEnabled = DetectionToggle.IsChecked == true;
    private void ConfidenceSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _confidence = (float)e.NewValue;
        if (ConfidenceText is not null) ConfidenceText.Text = _confidence.ToString("0.00");
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopPipelineAsync();

    private async Task StopPipelineAsync()
    {
        _streamStop?.Cancel();
        _frames.Dispose();
        try { await Task.WhenAll(_captureTask ?? Task.CompletedTask, _processingTask ?? Task.CompletedTask); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { if (!_closing) ConnectionStatus.Text = $"Pipeline stopped: {ex.Message}"; }
        _streamStop?.Dispose(); _streamStop = null;
        _frames = new LatestFrameSlot();
        _captureTask = _processingTask = null;
        _detector?.Dispose(); _detector = null;
        if (!_closing)
        {
            ConnectButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            DetectionToggle.IsEnabled = false;
            ConnectionStatus.Text = "Disconnected";
            VideoPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _closing = true;
        await StopPipelineAsync();
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
