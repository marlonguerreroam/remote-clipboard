using Microsoft.Extensions.Logging;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Logging;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Logging;

public class FileLoggerProviderTests
{
    [Fact]
    public void Writes_technical_events_to_a_daily_file()
    {
        using var dir = new TempDirectory();
        var id = DeviceId.New();
        using (var provider = new FileLoggerProvider(dir.Path))
        {
            var logger = provider.CreateLogger("RemoteClipboard.Core.Networking.ConnectionManager");
            Log.DeviceConnected(logger, id, "PC-B");
            Log.ClipboardEventDetected(logger, Core.Clipboard.ClipboardFormat.Text); // Debug: filtered out
        }

        var file = Assert.Single(Directory.GetFiles(dir.Path, "remoteclipboard-*.log"));
        var content = File.ReadAllText(file);
        Assert.Contains($"[INF] ConnectionManager: Device connected: {id} (PC-B)", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard event detected", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_only_the_most_recent_files()
    {
        using var dir = new TempDirectory();
        for (var day = 1; day <= 10; day++)
        {
            File.WriteAllText(Path.Combine(dir.Path, $"remoteclipboard-202601{day:00}.log"), "x");
        }

        using (var provider = new FileLoggerProvider(dir.Path, retainedFiles: 3))
        {
            Log.NetworkChanged(provider.CreateLogger("Test"));
        }

        Assert.Equal(3, Directory.GetFiles(dir.Path, "remoteclipboard-*.log").Length);
    }
}
