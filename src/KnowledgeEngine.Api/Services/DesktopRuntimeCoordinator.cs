namespace KnowledgeEngine.Api.Services;

public sealed class DesktopRuntimeCoordinator : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource _modeChanged = new();
    public long Generation { get; private set; }

    public CancellationToken ModeChangedToken
    {
        get { lock (_gate) return _modeChanged.Token; }
    }

    public void Advance()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            previous = _modeChanged;
            _modeChanged = new CancellationTokenSource();
            Generation++;
        }
        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        lock (_gate) _modeChanged.Dispose();
    }
}
