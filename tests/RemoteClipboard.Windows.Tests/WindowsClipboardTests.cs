// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

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
        Assert.Equal(text, RawClipboard.GetText());
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Local_text_is_read_exactly(string text)
    {
        RawClipboard.SetText(text);

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
        _clipboard.ClipboardChanged += (_, change) =>
        {
            if (change.Content?.GetText() == "evento")
            {
                received.TrySetResult(change);
            }
        };

        RawClipboard.SetText("evento");

        var change = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("evento", change.Content?.GetText());
    }

    [Fact]
    public async Task Content_marked_private_by_its_owner_is_not_read()
    {
        RawClipboard.SetText("contraseña123", "ExcludeClipboardContentFromMonitorProcessing");

        var snapshot = await _clipboard.ReadAsync(Ct);

        Assert.True(snapshot!.IsExcludedByOwner);
        Assert.Null(snapshot.Content);
    }
}
