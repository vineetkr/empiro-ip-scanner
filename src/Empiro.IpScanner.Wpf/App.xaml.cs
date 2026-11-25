using System.Windows;

namespace Empiro.IpScanner.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var renderIndex = Array.FindIndex(e.Args, arg => arg.Equals("--render-demo", StringComparison.OrdinalIgnoreCase));
        if (renderIndex >= 0 && (renderIndex + 1 >= e.Args.Length || e.Args[renderIndex + 1].StartsWith("--")))
        {
            MessageBox.Show("Usage: Empiro-IP-Scanner --render-demo <output-folder>");
            Shutdown(1);
            return;
        }
        if (renderIndex >= 0) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var demo = renderIndex >= 0 || e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        Window window = !demo && e.Args.Contains("--camera-monitor", StringComparer.OrdinalIgnoreCase)
            ? new CameraMonitorWindow() : new MainWindow(demo);
        MainWindow = window;
        if (renderIndex >= 0 && window is MainWindow main)
        {
            main.ContentRendered += async (_, _) =>
            {
                try { await main.RenderDemoAsync(e.Args[renderIndex + 1]); Shutdown(); }
                catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "empiro-demo-render-error.txt"), ex.ToString()); Shutdown(1); }
            };
        }
        window.Show();
    }
}
