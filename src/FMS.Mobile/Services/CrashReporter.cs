using System.Diagnostics;
using System.Text;

namespace FMS.Mobile.Services;

public sealed class CrashReporter
{
    private readonly string _crashLogPath;
    private readonly string _latestCrashPath;

    public string LatestCrashPath => _latestCrashPath;

    public CrashReporter()
    {
        var dir = FileSystem.AppDataDirectory;
        _crashLogPath = Path.Combine(dir, "crashlog.txt");
        _latestCrashPath = Path.Combine(dir, "latest-crash.txt");

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void LogLocal(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(_crashLogPath, line, Encoding.UTF8);
        }
        catch { }
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteCrash("AppDomain.UnhandledException", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrash("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    private void WriteCrash(string source, Exception ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== CRASH [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] source={source} ===");
            sb.AppendLine(ex.ToString());
            sb.AppendLine();
            var text = sb.ToString();

            File.AppendAllText(_crashLogPath, text, Encoding.UTF8);
            File.WriteAllText(_latestCrashPath, text, Encoding.UTF8);

            Debug.WriteLine($"[FMS CrashReporter] {source}: {ex.Message}");
        }
        catch { }
    }
}