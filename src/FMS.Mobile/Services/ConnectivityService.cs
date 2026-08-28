namespace FMS.Mobile.Services;

public class ConnectivityService : IConnectivityService, IDisposable
{
    private Timer? _timer;
    private bool _lastState;

    public bool IsConnected => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    public event EventHandler<bool>? ConnectivityChanged;

    public ConnectivityService()
    {
        _lastState = IsConnected;
        // Poll every 5 seconds for connectivity changes
        _timer = new Timer(_ =>
        {
            var current = IsConnected;
            if (current != _lastState)
            {
                _lastState = current;
                ConnectivityChanged?.Invoke(this, current);
            }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
