using Empiro.IpScanner.Core;
using System.Windows;
using System.Windows.Controls;

namespace Empiro.IpScanner.Wpf;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            PingTimeoutMs = current.PingTimeoutMs, ConcurrentWorkers = current.ConcurrentWorkers,
            RetryCount = current.RetryCount, ResolveHostnames = current.ResolveHostnames,
            ResolveMacAddresses = current.ResolveMacAddresses, EnableArpDiscovery = current.EnableArpDiscovery,
            EnableCameraDiscovery = current.EnableCameraDiscovery, EnablePortScan = current.EnablePortScan,
            Ports = current.Ports.ToList(), RememberLastRange = current.RememberLastRange,
            ConfirmBeforeStop = current.ConfirmBeforeStop, LastRange = current.LastRange,
            RangeHistory = current.RangeHistory.ToList(), Theme = current.Theme,
            WindowWidth = current.WindowWidth, WindowHeight = current.WindowHeight
        };
        TimeoutBox.Text = Settings.PingTimeoutMs.ToString();
        WorkersBox.Text = Settings.ConcurrentWorkers.ToString();
        RetriesBox.Text = Settings.RetryCount.ToString();
        HostnamesCheck.IsChecked = Settings.ResolveHostnames;
        MacCheck.IsChecked = Settings.ResolveMacAddresses;
        ArpCheck.IsChecked = Settings.EnableArpDiscovery;
        CameraCheck.IsChecked = Settings.EnableCameraDiscovery;
        PortsCheck.IsChecked = Settings.EnablePortScan;
        PortsBox.Text = string.Join(", ", Settings.Ports);
        RememberCheck.IsChecked = Settings.RememberLastRange;
        ConfirmStopCheck.IsChecked = Settings.ConfirmBeforeStop;
        ThemeCombo.SelectedIndex = Settings.Theme.ToLowerInvariant() switch { "dark" => 1, "system" => 2, _ => 0 };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TimeoutBox.Text, out var timeout) || timeout is < 200 or > 5000 ||
            !int.TryParse(WorkersBox.Text, out var workers) || workers is < 1 or > 64 ||
            !int.TryParse(RetriesBox.Text, out var retries) || retries is < 0 or > 3)
        {
            MessageBox.Show(this, "Timeout must be 200–5000 ms, workers 1–64, and retries 0–3.", "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var ports = new List<int>();
        foreach (var item in PortsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(item, out var port) || port is < 1 or > 65535)
            {
                MessageBox.Show(this, $"'{item}' is not a valid TCP port (1–65535).", "Invalid ports", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ports.Add(port);
        }
        Settings.PingTimeoutMs = timeout;
        Settings.ConcurrentWorkers = workers;
        Settings.RetryCount = retries;
        Settings.ResolveHostnames = HostnamesCheck.IsChecked == true;
        Settings.ResolveMacAddresses = MacCheck.IsChecked == true;
        Settings.EnableArpDiscovery = ArpCheck.IsChecked == true;
        Settings.EnableCameraDiscovery = CameraCheck.IsChecked == true;
        Settings.EnablePortScan = PortsCheck.IsChecked == true;
        Settings.Ports = ports.Distinct().ToList();
        Settings.RememberLastRange = RememberCheck.IsChecked == true;
        Settings.ConfirmBeforeStop = ConfirmStopCheck.IsChecked == true;
        Settings.Theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Light";
        DialogResult = true;
    }
}
