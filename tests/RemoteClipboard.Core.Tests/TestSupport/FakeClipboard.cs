// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Clipboard;

namespace RemoteClipboard.Core.Tests.TestSupport;

/// <summary>
/// Behaves like the Windows clipboard: writing raises a change event on the same device. With
/// <paramref name="markerSurvives"/> false it simulates an app that strips our origin marker.
/// </summary>
internal sealed class FakeClipboard(bool markerSurvives = true) : IClipboardMonitor, IClipboardWriter
{
    private readonly Lock _gate = new();
    private string? _text;

    public event EventHandler<ClipboardChange>? ClipboardChanged;

    public int RemoteWrites { get; private set; }

    public string? Text
    {
        get
        {
            lock (_gate)
            {
                return _text;
            }
        }
    }

    public void Start()
    {
    }

    public void UserCopies(string text)
    {
        lock (_gate)
        {
            _text = text;
        }

        ClipboardChanged?.Invoke(this, new ClipboardChange(ClipboardContent.FromText(text), false, false));
    }

    public Task WriteRemoteContentAsync(ClipboardContent content, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _text = content.GetText();
            RemoteWrites++;
        }

        // Windows posts WM_CLIPBOARDUPDATE asynchronously after our own write.
        _ = Task.Run(() => ClipboardChanged?.Invoke(this, markerSurvives
            ? new ClipboardChange(null, HasRemoteOriginMarker: true, IsExcludedByOwner: false)
            : new ClipboardChange(content, false, false)), cancellationToken);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
