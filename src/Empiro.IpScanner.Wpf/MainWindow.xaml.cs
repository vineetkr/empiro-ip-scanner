using Empiro.IpScanner.Core;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Empiro.IpScanner.Wpf;

public partial class MainWindow : Window
{
    private readonly AppStateStore _store = new();
    private readonly NetworkScanner _scanner = new();
    private readonly ObservableCollection<DeviceResult> _devices = [];
    private readonly ObservableCollection<DeviceResult> _favorites = [];
    private readonly List<string> _rangeHistory = [];
    private AppState _state = new();
    private ICollectionView? _deviceView;
    private CancellationTokenSource? _scanCancellation;
    private ScanControl? _scanControl;
    private Task? _scanTask;
    private bool _closing;
    private bool _isScanning;

    public ICollectionView? DeviceView
    {
        get => _deviceView;
        private set
        {
            _deviceView = value;
            if (value is not null) value.Filter = FilterDevice;
            DataGrid? grid = DeviceGrid;
            if (grid is not null) grid.ItemsSource = value;
        }
    }

    public MainWindow(bool demoMode = false)
    {
        _demoMode = demoMode;
        InitializeComponent();
        DataContext = this;
        DeviceGrid.ItemsSource = CollectionViewSource.GetDefaultView(_devices);
        _deviceView = CollectionViewSource.GetDefaultView(_devices);
        _deviceView.Filter = FilterDevice;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_demoMode) { LoadDemo(); return; }
        _state = await _store.LoadAsync();
        Width = Math.Clamp(_state.Settings.WindowWidth, MinWidth, 1800);
        Height = Math.Clamp(_state.Settings.WindowHeight, MinHeight, 1200);
        foreach (var range in _state.Settings.RangeHistory) AddRangeHistory(range);
        if (_state.Settings.RememberLastRange && !string.IsNullOrWhiteSpace(_state.Settings.LastRange))
            RangeCombo.Text = _state.Settings.LastRange;
        else
            DetectNetwork(setRange: true, showDialog: false);
        ApplyTheme();
        RefreshFavorites();
        UpdateCounts();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        if (_isScanning && _state.Settings.ConfirmBeforeStop &&
            MessageBox.Show(this, "A scan is still running. Stop it and close Empiro IP Scanner?", "Stop scan", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _closing = true;
        _scanCancellation?.Cancel();
        _scanControl?.Resume();
        _state.Settings.WindowWidth = Width;
        _state.Settings.WindowHeight = Height;
        await SaveStateAsync();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunScanAsync(RangeCombo.Text, clearResults: true);

    private async Task RunScanAsync(string rawRange, bool clearResults)
    {
        if (_demoMode) { LoadDemo(); return; }
        if (_isScanning) return;
        IReadOnlyList<IPAddress> addresses;
        try { addresses = IpRangeParser.Parse(rawRange); }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid IP range", MessageBoxButton.OK, MessageBoxImage.Warning);
            RangeCombo.Focus();
            return;
        }

        if (addresses.Any(ip => !IsPrivateOrLocal(ip)) &&
            MessageBox.Show(this,
                $"This range includes public Internet addresses ({addresses.Count:N0} addresses). Only continue if you own these systems or are authorized to scan them.",
                "Confirm authorized scan", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (clearResults) _devices.Clear();
        AddRangeHistory(rawRange.Trim());
        _state.Settings.LastRange = rawRange.Trim();
        _isScanning = true;
        _scanCancellation = new CancellationTokenSource();
        _scanControl = new ScanControl();
        var cancellationToken = _scanCancellation.Token;
        var options = ToScanOptions();
        var stopwatch = Stopwatch.StartNew();
        ScanProgress.Maximum = 100;
        ScanProgress.Value = 0;
        ProgressText.Text = $"0 / {addresses.Count:N0}";
        StatusText.Text = $"Scanning {rawRange.Trim()}…";
        ScanButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        PauseButton.Content = "Ⅱ  Pause";
        UpdateCounts();

        var progress = new Progress<ScanProgress>(update =>
        {
            UpsertDevice(update.Device);
            ScanProgress.Value = update.Total == 0 ? 0 : update.Completed * 100.0 / update.Total;
            ProgressText.Text = $"{update.Completed:N0} / {update.Total:N0}";
            UpdateCounts();
        });

        _scanTask = _scanner.ScanAsync(addresses, options, _scanControl, progress, cancellationToken);
        try
        {
            await _scanTask;
            stopwatch.Stop();
            StatusText.Text = "Scan completed";
            ProgressText.Text = $"{addresses.Count:N0} / {addresses.Count:N0}";
            ScanProgress.Value = 100;
            var online = _devices.Count(d => d.Status == "Online");
            _state.History.Insert(0, new ScanHistoryItem
            {
                StartedAt = DateTimeOffset.Now, IpRange = rawRange.Trim(), ScannedAddresses = addresses.Count, DevicesFound = online,
                OnlineDevices = online, DurationSeconds = stopwatch.Elapsed.TotalSeconds
            });
            if (_state.History.Count > 100) _state.History.RemoveRange(100, _state.History.Count - 100);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            StatusText.Text = "Scan stopped";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Scan error";
            MessageBox.Show(this, $"The scan could not be completed.\n\n{ex.Message}", "Scan error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isScanning = false;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            _scanControl = null;
            ScanButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            PauseButton.Content = "Ⅱ  Pause";
            UpdateCounts();
            await SaveStateAsync();
        }
    }

    private ScanOptions ToScanOptions() => new(
        _state.Settings.PingTimeoutMs, _state.Settings.ConcurrentWorkers, _state.Settings.RetryCount,
        _state.Settings.ResolveHostnames, _state.Settings.ResolveMacAddresses,
        _state.Settings.EnablePortScan, _state.Settings.Ports,
        _state.Settings.EnableArpDiscovery, _state.Settings.EnableCameraDiscovery);

    private void UpsertDevice(DeviceResult found)
    {
        var ip = found.IpAddress;
        if (_state.RenamedDevices.TryGetValue(ip, out var renamed)) found.Name = renamed;
        if (_state.Comments.TryGetValue(ip, out var comment)) found.Comment = comment;
        var favorite = _state.Favorites.FirstOrDefault(f => f.IpAddress == ip);
        if (favorite is not null)
        {
            found.IsFavorite = true;
            favorite.LastSeen = found.Status == "Online" ? DateTimeOffset.Now : favorite.LastSeen;
            if (!string.IsNullOrWhiteSpace(favorite.Name)) found.Name = favorite.Name;
            if (!string.IsNullOrWhiteSpace(favorite.Comment)) found.Comment = favorite.Comment;
            var favoriteIndex = _favorites.ToList().FindIndex(d => d.IpAddress == ip);
            if (favoriteIndex >= 0) _favorites[favoriteIndex] = found;
        }
        var existing = _devices.FirstOrDefault(d => d.IpAddress == ip);
        if (existing is null) _devices.Add(found);
        else
        {
            var index = _devices.IndexOf(existing);
            _devices[index] = found;
        }
        RefreshCurrentView();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_scanControl is null) return;
        if (_scanControl.IsPaused)
        {
            _scanControl.Resume();
            PauseButton.Content = "Ⅱ  Pause";
            StatusText.Text = "Scan resumed";
        }
        else
        {
            _scanControl.Pause();
            PauseButton.Content = "▶  Resume";
            StatusText.Text = "Scan paused";
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!_isScanning) return;
        if (_state.Settings.ConfirmBeforeStop &&
            MessageBox.Show(this, "Stop the current scan?", "Stop scan", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _scanCancellation?.Cancel();
        _scanControl?.Resume();
        StatusText.Text = "Stopping scan…";
    }

    private void DetectNetwork_Click(object sender, RoutedEventArgs e) => DetectNetwork(setRange: true, showDialog: true);

    private void DetectNetwork(bool setRange, bool showDialog)
    {
        if (_demoMode) { RangeCombo.Text = "192.0.2.0/24"; StatusText.Text = "Demo network — fictional data; no network traffic"; return; }
        var info = NetworkDetector.DetectLocalNetwork();
        if (info is null)
        {
            if (showDialog) MessageBox.Show(this, "No active IPv4 network adapter was found.", "Network information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (setRange)
        {
            var range = info.CidrNetwork;
            try { _ = IpRangeParser.Parse(range); }
            catch (FormatException) { range = $"{info.Ipv4Address}/24"; }
            RangeCombo.Text = range;
        }
        if (showDialog) new NetworkInfoWindow(info) { Owner = this }.ShowDialog();
    }

    private void RangeCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; _ = RunScanAsync(RangeCombo.Text, clearResults: true); }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshCurrentView();

    private void ResultsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ResultsTabs)) return;
        DeviceView = CollectionViewSource.GetDefaultView(ResultsTabs.SelectedIndex == 1 ? _favorites : _devices);
        RefreshCurrentView();
    }

    private void RefreshCurrentView()
    {
        DeviceView?.Refresh();
        if (DeviceGrid.SelectedItem is DeviceResult item) FavoriteMenuItem.Header = item.IsFavorite ? "Remove from Favorites" : "Add to Favorites";
    }

    private void DeviceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is DeviceResult item) FavoriteMenuItem.Header = item.IsFavorite ? "Remove from Favorites" : "Add to Favorites";
    }

    private void DeviceGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = ItemsControl.ContainerFromElement(DeviceGrid, e.OriginalSource as DependencyObject) as DataGridRow;
        if (row is not null && !row.IsSelected)
        {
            DeviceGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private bool FilterDevice(object value)
    {
        if (value is not DeviceResult device) return false;
        // Keep the Results tab focused on reachable devices. Favorites remain
        // visible even when they are offline so saved devices are not hidden.
        if (ResultsTabs.SelectedIndex == 0 && device.Status != "Online") return false;
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return true;
        return new[] { device.IpAddress, device.Name, device.MacAddress, device.Manufacturer, device.Status, device.Type, device.Comment }
            .Any(field => field.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateCounts()
    {
        var online = _devices.Count(d => d.Status == "Online");
        CountsText.Text = $"Online devices: {online:N0}";
    }

    private void AddRangeHistory(string range)
    {
        if (string.IsNullOrWhiteSpace(range)) return;
        _rangeHistory.RemoveAll(r => string.Equals(r, range, StringComparison.OrdinalIgnoreCase));
        _rangeHistory.Insert(0, range);
        if (_rangeHistory.Count > 12) _rangeHistory.RemoveAt(_rangeHistory.Count - 1);
        RangeCombo.ItemsSource = null;
        RangeCombo.ItemsSource = _rangeHistory;
    }

    private void RefreshFavorites()
    {
        _favorites.Clear();
        foreach (var favorite in _state.Favorites)
        {
            var current = _devices.FirstOrDefault(d => d.IpAddress == favorite.IpAddress);
            _favorites.Add(current ?? new DeviceResult
            {
                IpAddress = favorite.IpAddress, Name = string.IsNullOrWhiteSpace(favorite.Name) ? favorite.IpAddress : favorite.Name,
                MacAddress = string.IsNullOrWhiteSpace(favorite.MacAddress) ? "-" : favorite.MacAddress,
                Comment = favorite.Comment, Type = favorite.Type, IsFavorite = true,
                LastSeen = favorite.LastSeen, Status = "Offline"
            });
        }
        RefreshCurrentView();
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is not { } device) return;
        var favorite = _state.Favorites.FirstOrDefault(f => f.IpAddress == device.IpAddress);
        if (favorite is null)
        {
            device.IsFavorite = true;
            _state.Favorites.Add(new FavoriteDevice
            {
                IpAddress = device.IpAddress, Name = device.Name, MacAddress = device.MacAddress,
                Comment = device.Comment, Type = device.Type, LastSeen = device.LastSeen
            });
        }
        else
        {
            _state.Favorites.Remove(favorite);
            device.IsFavorite = false;
        }
        RefreshFavorites();
        RefreshCurrentView();
        await SaveStateAsync();
    }

    private DeviceResult? SelectedDevice => DeviceGrid.SelectedItem as DeviceResult;

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is not { } device) return;
        var name = TextPrompt.Show(this, "Rename device", "Device name:", device.Name);
        if (name is null) return;
        _state.RenamedDevices[device.IpAddress] = name.Trim();
        device.Name = name.Trim();
        var favorite = _state.Favorites.FirstOrDefault(f => f.IpAddress == device.IpAddress);
        if (favorite is not null) favorite.Name = name.Trim();
        RefreshFavorites();
        RefreshCurrentView();
        await SaveStateAsync();
    }

    private async void Comment_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is not { } device) return;
        var comment = TextPrompt.Show(this, "Edit comment", "Comment:", device.Comment);
        if (comment is null) return;
        _state.Comments[device.IpAddress] = comment.Trim();
        device.Comment = comment.Trim();
        var favorite = _state.Favorites.FirstOrDefault(f => f.IpAddress == device.IpAddress);
        if (favorite is not null) favorite.Comment = comment.Trim();
        RefreshCurrentView();
        await SaveStateAsync();
    }

    private async void RescanSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is { } device) await RunScanAsync(device.IpAddress, clearResults: false);
    }

    private void CopyField_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is not { } device || sender is not MenuItem item) return;
        var value = item.Tag?.ToString() switch
        {
            "ip" => device.IpAddress, "name" => device.Name, "mac" => device.MacAddress,
            _ => $"Status: {device.Status}\nName: {device.Name}\nIP address: {device.IpAddress}\nManufacturer: {device.Manufacturer}\nMAC address: {device.MacAddress}\nResponse: {device.ResponseTime}\nPorts: {device.Ports}\nType: {device.Type}\nComment: {device.Comment}"
        };
        Clipboard.SetText(value);
    }

    private async void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is not { } device || sender is not MenuItem item) return;
        if (_demoMode) { StatusText.Text = "Network tools are disabled in demo mode"; return; }
        var ip = device.IpAddress;
        try
        {
            switch (item.Tag?.ToString())
            {
                case "http": OpenUrl($"http://{ip}"); break;
                case "https": OpenUrl($"https://{ip}"); break;
                case "ftp": OpenUrl($"ftp://{ip}"); break;
                case "rdp": Process.Start(new ProcessStartInfo("mstsc.exe", $"/v:{ip}") { UseShellExecute = true }); break;
                case "ssh": Process.Start(new ProcessStartInfo("ssh.exe", ip) { UseShellExecute = true }); break;
                case "telnet": Process.Start(new ProcessStartInfo("telnet.exe", ip) { UseShellExecute = true }); break;
                case "ping": await ShowCommandOutputAsync("ping.exe", $"-n 4 {ip}", "Ping"); break;
                case "tracert": await ShowCommandOutputAsync("tracert.exe", $"-d -h 12 -w 800 {ip}", "Traceroute"); break;
                case "wol": await SendWakeOnLanAsync(device); break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or SocketException)
        {
            MessageBox.Show(this, ex.Message, "Network tool", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private async Task ShowCommandOutputAsync(string file, string args, string title)
    {
        var start = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = Process.Start(start);
        if (process is null) return;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);
        var output = outputTask.Result;
        var error = errorTask.Result;
        var content = string.IsNullOrWhiteSpace(output) ? error : output;
        new OutputWindow(title, content).ShowDialog();
    }

    private async Task SendWakeOnLanAsync(DeviceResult device)
    {
        var mac = Convert.FromHexString(string.Concat(device.MacAddress.Where(Uri.IsHexDigit)));
        if (mac.Length != 6)
        {
            MessageBox.Show(this, "A valid MAC address is not available for this device.", "Wake-On-LAN", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var packet = new byte[102];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var i = 0; i < 16; i++) Array.Copy(mac, 0, packet, 6 + i * 6, 6);
        using var udp = new UdpClient { EnableBroadcast = true };
        await udp.SendAsync(packet, new IPEndPoint(IPAddress.Broadcast, 9));
        StatusText.Text = $"Wake-On-LAN packet sent to {device.IpAddress}";
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Export scan results", FileName = "Empiro-scan", Filter = "CSV file (*.csv)|*.csv|JSON file (*.json)|*.json|Text file (*.txt)|*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        var devices = ResultsTabs.SelectedIndex == 1 ? _favorites.ToList() : _devices.ToList();
        var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var content = extension switch
        {
            ".json" => JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true }),
            ".txt" => string.Join(Environment.NewLine, devices.Select(d => $"{d.Status}\t{d.Name}\t{d.IpAddress}\t{d.Manufacturer}\t{d.MacAddress}\t{d.ResponseTime}\t{d.Ports}\t{d.Type}")),
            _ => MakeCsv(devices)
        };
        await File.WriteAllTextAsync(dialog.FileName, content, new UTF8Encoding(true));
        StatusText.Text = $"Exported {devices.Count:N0} devices";
    }

    private static string MakeCsv(IEnumerable<DeviceResult> devices)
    {
        static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var rows = devices.Select(d => string.Join(',', new[] { d.IpAddress, d.Name, d.MacAddress, d.Manufacturer, d.Status, d.ResponseTime, d.Ports, d.Type, d.Comment }.Select(Q)));
        return "IP,Hostname,MAC,Manufacturer,Status,ResponseTime,Ports,Type,Comment\r\n" + string.Join("\r\n", rows);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_state.Settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _state.Settings = dialog.Settings;
            ApplyTheme();
            _ = SaveStateAsync();
        }
    }

    private void ApplyTheme()
    {
        var dark = string.Equals(_state.Settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
        Application.Current.Resources["WindowBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dark ? "#202832" : "#F3F5F8"));
        Application.Current.Resources["PanelBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dark ? "#2A3540" : "#FFFFFF"));
        Application.Current.Resources["InkBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dark ? "#E5EDF5" : "#223142"));
        Application.Current.Resources["MutedBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dark ? "#AAB6C2" : "#6B7B8B"));
        DeviceGrid.Background = (System.Windows.Media.Brush)Application.Current.Resources["PanelBrush"];
        DeviceGrid.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["InkBrush"];
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HistoryWindow(_state.History) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedRange)) RangeCombo.Text = dialog.SelectedRange;
    }

    private void NetworkInfo_Click(object sender, RoutedEventArgs e) => DetectNetwork(setRange: false, showDialog: true);
    private void CameraMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (_demoMode) { StatusText.Text = "Live camera connections are disabled in demo mode"; return; }
        new CameraMonitorWindow { Owner = this }.Show();
    }
    private void ShowResults_Click(object sender, RoutedEventArgs e) => ResultsTabs.SelectedIndex = 0;
    private void ShowFavorites_Click(object sender, RoutedEventArgs e) => ResultsTabs.SelectedIndex = 1;
    private void AuthorizedUse_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, "Use Empiro IP Scanner only on networks and devices that you own or are authorized to test. The default scan sends ordinary ICMP echo requests; optional port checks only test whether configured TCP ports accept a connection.", "Authorized use", MessageBoxButton.OK, MessageBoxImage.Information);
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, "Empiro IP Scanner\nWindows network inventory utility\nVersion 1.1.2", "About", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Refresh_Executed(object sender, ExecutedRoutedEventArgs e) => _ = RunScanAsync(RangeCombo.Text, clearResults: true);
    private void Stop_Executed(object sender, ExecutedRoutedEventArgs e) => Stop_Click(sender, e);
    private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (SelectedDevice is not { } device) return;
        Clipboard.SetText($"Status: {device.Status}\nName: {device.Name}\nIP address: {device.IpAddress}\nManufacturer: {device.Manufacturer}\nMAC address: {device.MacAddress}\nResponse: {device.ResponseTime}\nPorts: {device.Ports}\nType: {device.Type}\nComment: {device.Comment}");
    }
    private void DeviceGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (!_demoMode && SelectedDevice is { } item) OpenUrl($"http://{item.IpAddress}"); }

    private async Task SaveStateAsync()
    {
        if (_demoMode) return;
        _state.Settings.RangeHistory = _rangeHistory.ToList();
        try { await _store.SaveAsync(_state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Could not save local settings";
        }
    }

    private static bool IsPrivateOrLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return IPAddress.IsLoopback(ip) || b[0] == 10 || b[0] == 127 || b[0] == 169 && b[1] == 254 ||
               b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 100 && b[1] is >= 64 and <= 127;
    }
}
