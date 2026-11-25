using Empiro.IpScanner.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Empiro.IpScanner.Wpf;

public sealed class NetworkInfoWindow : Window
{
    public NetworkInfoWindow(NetworkInformation info)
    {
        Title = "Network information"; Width = 520; Height = 430; MinWidth = 450; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(18) };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        var stack = new StackPanel();
        Add("Network adapter", info.AdapterName); Add("Local IPv4", info.Ipv4Address); Add("Subnet mask", info.SubnetMask);
        Add("CIDR network", info.CidrNetwork); Add("Adapter MAC", info.MacAddress);
        Add("Default gateway", info.Gateways.Count == 0 ? "Not available" : string.Join(", ", info.Gateways));
        Add("DNS servers", info.DnsServers.Count == 0 ? "Not available" : string.Join(", ", info.DnsServers));
        var view = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(view); Content = root;

        void Add(string label, string value)
        {
            stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 1) });
            stack.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        }
    }
}

public sealed class HistoryWindow : Window
{
    public string? SelectedRange { get; private set; }
    public HistoryWindow(IEnumerable<ScanHistoryItem> history)
    {
        Title = "Scan history"; Width = 760; Height = 430; MinWidth = 600; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(14) };
        DataGrid grid = null!;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var use = new Button { Content = "Use selected range", Padding = new Thickness(12, 6, 12, 6), IsDefault = true };
        use.Click += (_, _) => { if (grid.SelectedItem is ScanHistoryItem item) { SelectedRange = item.IpRange; DialogResult = true; } };
        footer.Children.Add(use); footer.Children.Add(new Button { Content = "Close", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) });
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        grid = new DataGrid { ItemsSource = history, AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false };
        root.Children.Add(grid); Content = root;
    }
}

public sealed class OutputWindow : Window
{
    public OutputWindow(string title, string output)
    {
        Title = title; Width = 680; Height = 480; MinWidth = 450; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(12) };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        root.Children.Add(new TextBox { Text = output, IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") });
        Content = root;
    }
}

public static class TextPrompt
{
    public static string? Show(Window owner, string title, string label, string initialValue)
    {
        var window = new Window
        {
            Title = title, Owner = owner, Width = 390, Height = 160, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 5, 12, 5) };
        buttons.Children.Add(save); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var entry = new TextBox { Text = initialValue, Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(5) };
        var body = new StackPanel(); body.Children.Add(new TextBlock { Text = label }); body.Children.Add(entry); root.Children.Add(body);
        save.Click += (_, _) => window.DialogResult = true;
        window.Content = root; window.Loaded += (_, _) => { entry.Focus(); entry.SelectAll(); };
        return window.ShowDialog() == true ? entry.Text : null;
    }
}
