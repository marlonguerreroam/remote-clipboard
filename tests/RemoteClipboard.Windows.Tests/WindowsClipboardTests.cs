using System.Runtime.Versioning;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Windows.Clipboard;

namespace RemoteClipboard.Windows.Tests;

/// <summary>
/// Exercises the real Windows clipboard. Tests share the session clipboard, so they run sequentially.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("Clipboard")]
public sealed class WindowsClipboardTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly WindowsClipboard _clipboard = new();

    public WindowsClipboardTests() => _clipboard.Start();

    public void Dispose() => _clipboard.Dispose();

    public static TheoryData<string> Samples => new(
        "Hola, este es un texto de prueba.",
        "Canción, pingüino, ÁÉÍÓÚ ñÑ ¿¡",
        "Listo ✅ 🚀 👨‍👩‍👧‍👦",
        "línea 1\r\nlínea 2\r\n\r\nlínea 4");

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Remote_write_is_readable_and_tagged_as_remote_origin(string text)
    {
        await _clipboard.WriteRemoteContentAsync(ClipboardContent.FromText(text), Ct);

        var snapshot = await _clipboard.ReadAsync(Ct);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.HasRemoteOriginMarker);
        // Content is deliberately not read for remote-origin changes.
        Assert.Null(snapshot.Content);
        Assert.Equal(text, System.Windows.Forms.Clipboard.GetText());
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Local_text_is_read_exactly(string text)
    {
        await RunStaAsync(() => System.Windows.Forms.Clipboard.SetText(text));

        var snapshot = await _clipboard.ReadAsync(Ct);

        Assert.NotNull(snapshot);
        Assert.False(snapshot.HasRemoteOriginMarker);
        Assert.False(snapshot.IsExcludedByOwner);
        Assert.Equal(text, snapshot.Content!.GetText());
    }

    [Fact]
    public async Task Local_copy_raises_change_event_without_polling()
    {
        var received = new TaskCompletionSource<ClipboardChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        _clipboard.ClipboardChanged += (_, change) => received.TrySetResult(change);

        await RunStaAsync(() => System.Windows.Forms.Clipboard.SetText("evento"));

        var change = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("evento", change.Content?.GetText());
    }

    [Fact]
    public async Task Content_marked_private_by_its_owner_is_not_read()
    {
        await RunStaAsync(() =>
        {
            var data = new System.Windows.Forms.DataObject();
            data.SetText("contraseña123");
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream([0]));
            System.Windows.Forms.Clipboard.SetDataObject(data, copy: true);
        });

        var snapshot = await _clipboard.ReadAsync(Ct);

        Assert.True(snapshot!.IsExcludedByOwner);
        Assert.Null(snapshot.Content);
    }

    private static Task RunStaAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
