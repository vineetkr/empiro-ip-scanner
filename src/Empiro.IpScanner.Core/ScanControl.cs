namespace Empiro.IpScanner.Core;

public sealed class ScanControl
{
    private readonly object _sync = new();
    private TaskCompletionSource _resume = CompletedSource();
    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    public bool IsPaused { get { lock (_sync) return !_resume.Task.IsCompleted; } }

    public void Pause()
    {
        lock (_sync)
            if (_resume.Task.IsCompleted)
                _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        lock (_sync) _resume.TrySetResult();
    }

    public async ValueTask WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task wait;
        lock (_sync) wait = _resume.Task;
        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
