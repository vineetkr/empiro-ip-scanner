using Empiro.IpScanner.Core;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Empiro.IpScanner.Wpf;

public partial class MainWindow
{
    private readonly bool _demoMode;
    private bool _demoBannerAdded;

    private void LoadDemo()
    {
        Title = "Empiro IP Scanner — DEMO";
        Width = 1440; Height = 780;
        DeviceGrid.EnableRowVirtualization = false;
        DeviceGrid.EnableColumnVirtualization = false;
        _state = new AppState();
        _devices.Clear();
        var devices = DemoData.CreateDevices();
        foreach (var device in devices)
        {
            _devices.Add(device);
            if (device.IsFavorite) _state.Favorites.Add(new FavoriteDevice
            {
                IpAddress = device.IpAddress, Name = device.Name, MacAddress = device.MacAddress,
                Type = device.Type, Comment = device.Comment, LastSeen = device.LastSeen
            });
        }
        _state.History = DemoData.CreateHistory();
        RangeCombo.Text = "192.0.2.0/24";
        if (!_demoBannerAdded) { AddDemoBanner(this, "OFFICE NETWORK PREVIEW"); _demoBannerAdded = true; }
        ApplyTheme(); RefreshFavorites(); UpdateCounts();
        ScanButton.Content = "↻  Reload demo";
        StatusText.Text = "Demo loaded — no live scan performed";
        ScanProgress.Value = 0;
        ProgressText.Text = "12 sample devices";
    }

    private static void AddDemoBanner(Window window, string label)
    {
        var body = (UIElement)window.Content;
        window.Content = null;
        var panel = new DockPanel { Background = Brushes.White };
        var banner = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(230, 241, 250)),
            Padding = new Thickness(18, 10, 18, 10),
            Child = new TextBlock { Text = $"DEMO  •  {label}  •  Fictional data", Foreground = new SolidColorBrush(Color.FromRgb(24, 69, 103)), FontWeight = FontWeights.SemiBold }
        };
        DockPanel.SetDock(banner, Dock.Top);
        panel.Children.Add(banner); panel.Children.Add(body);
        window.Content = panel;
    }

    internal async Task RenderDemoAsync(string directory)
    {
        if (!_demoMode) throw new InvalidOperationException("Rendering requires demo mode.");
        Directory.CreateDirectory(directory);
        ResultsTabs.SelectedIndex = 0; SearchBox.Text = "";
        await CaptureAsync(this, Path.Combine(directory, "01-scan-results.png"));
        ResultsTabs.SelectedIndex = 1;
        await CaptureAsync(this, Path.Combine(directory, "02-favorites.png"));
        ResultsTabs.SelectedIndex = 0; SearchBox.Text = "Camera";
        await CaptureAsync(this, Path.Combine(directory, "03-camera-filter.png"));
        SearchBox.Text = "";
        var history = new HistoryWindow(_state.History) { Owner = this, Width = 1100, Height = 480 };
        AddDemoBanner(history, "SCAN HISTORY"); history.Show();
        await CaptureAsync(history, Path.Combine(directory, "04-scan-history.png")); history.Close();
        var settings = new SettingsWindow(_state.Settings) { Owner = this, Width = 620, Height = 740 };
        AddDemoBanner(settings, "SCANNER SETTINGS"); settings.Show();
        await CaptureAsync(settings, Path.Combine(directory, "05-settings.png")); settings.Close();
    }

    private static async Task CaptureAsync(Window window, string path)
    {
        await Task.Delay(250);
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        var body = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(body.ActualWidth), (int)Math.Ceiling(body.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(body);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
