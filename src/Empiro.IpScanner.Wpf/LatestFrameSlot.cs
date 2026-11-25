using Emgu.CV;

namespace Empiro.IpScanner.Wpf;

/// <summary>Bounded handoff: a slow detector replaces stale frames instead of buffering them.</summary>
internal sealed class LatestFrameSlot : IDisposable
{
    private readonly object _gate = new();
    private Mat? _frame;
    private bool _completed;

    public void Publish(Mat frame)
    {
        lock (_gate)
        {
            if (_completed) return;
            _frame?.Dispose();
            _frame = frame.Clone();
            Monitor.Pulse(_gate);
        }
    }

    public Mat? Take(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            while (_frame is null && !_completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Monitor.Wait(_gate, 100);
            }
            var result = _frame;
            _frame = null;
            return result;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _completed = true;
            _frame?.Dispose();
            _frame = null;
            Monitor.PulseAll(_gate);
        }
    }
}
