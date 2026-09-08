using System.Text;

namespace FMS.Mobile.Services;

/// <summary>
/// Captures unhandled exceptions (AppDomain, TaskScheduler, MAUI) to a local
/// rolling text file so that crashes can be inspected on-device from the
/// Settings page even without adb/logcat attached.
/// </summary>
public interface ICrashLogService
{
    string LogFilePath { get; }
    void LogCrash(string source, Exception exception);
    void LogEvent(string message);
    Task<string> ReadLogAsync();
    Task ClearLogAsync();
    Task ShareLogAsync();
}

public class CrashLogService : ICrashLogService
{
    private const int MaxLogBytes = 512 * 1024; // 512 KB rolling ceiling
    private static readonly object Gate = new();

    public string LogFilePath { get; }

    public CrashLogService()
    {
        var dir = Path.Combine(FileSystem.AppDataDirectory, "diagnostics");
        Directory.CreateDirectory(dir);
        LogFilePath = Path.Combine(dir, "crash-log.txt");
    }

    public void LogCrash(string source, Exception exception)
    {
        var sb = new StringBuilder();
        sb.AppendLine("============================================================");
        sb.AppendLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] FATAL [{source}]");
        AppendException(sb, exception, 0);
        sb.AppendLine();
        Write(sb.ToString());
    }

    public void LogEvent(string message)
    {
        Write($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] INFO  {message}\n");
    }

    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        var pad = new string(' ', depth * 2);
        sb.AppendLine($"{pad}Type: {ex.GetType().FullName}");
        sb.AppendLine($"{pad}Message: {ex.Message}");
        sb.AppendLine($"{pad}StackTrace:");
        sb.AppendLine(ex.StackTrace ?? "(none)");
        if (ex.InnerException != null)
        {
            sb.AppendLine($"{pad}--- Inner exception ---");
            AppendException(sb, ex.InnerException, depth + 1);
        }
        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions.Skip(1))
            {
                sb.AppendLine($"{pad}--- Aggregate inner exception ---");
                AppendException(sb, inner, depth + 1);
            }
        }
    }

    private void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                // Trim old content if the log grows past the ceiling.
                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogBytes)
                {
                    var tail = ReadTail(64 * 1024);
                    File.WriteAllText(LogFilePath, "--- (older entries trimmed) ---\n" + tail);
                }
                File.AppendAllText(LogFilePath, text);
            }
        }
        catch
        {
            // Never let the crash logger itself take the app down.
        }
    }

    private string ReadTail(int bytes)
    {
        try
        {
            using var fs = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length <= bytes)
                return File.ReadAllText(LogFilePath);
            fs.Seek(-bytes, SeekOrigin.End);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    public Task<string> ReadLogAsync()
    {
        try
        {
            lock (Gate)
                return Task.FromResult(File.Exists(LogFilePath) ? File.ReadAllText(LogFilePath) : "(log is empty)");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"(failed to read log: {ex.Message})");
        }
    }

    public Task ClearLogAsync()
    {
        try
        {
            lock (Gate)
                File.Delete(LogFilePath);
        }
        catch
        {
            // ignore
        }
        return Task.CompletedTask;
    }

    public async Task ShareLogAsync()
    {
        if (!File.Exists(LogFilePath))
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await Microsoft.Maui.Controls.Application.Current?.Windows[0].Page!
                    .DisplayAlert("Crash Log", "No crash log recorded yet.", "OK")!);
            return;
        }

        var request = new ShareFileRequest
        {
            Title = "FMS Crash Log",
            File = new ShareFile(LogFilePath)
        };
        await Share.Default.RequestAsync(request);
    }
}
