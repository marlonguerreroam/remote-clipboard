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
    private ClipboardContent? _content;

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

    /// <summary>Last content on this clipboard (copied locally or received), any format.</summary>
    public ClipboardContent? Content
    {
        get
        {
            lock (_gate)
            {
                return _content;
            }
        }
    }

    public void Start()
    {
    }

    public void UserCopies(string text)
    {
        UserCopies(ClipboardContent.FromText(text));
    }

    public void UserCopies(ClipboardContent content)
    {
        lock (_gate)
        {
            _content = content;
            _text = content.Format == ClipboardFormat.Text ? content.GetText() : null;
        }

        ClipboardChanged?.Invoke(this, new ClipboardChange(content, false, false));
    }

    /// <summary>The user copied something larger than the limit for its kind (it is never read).</summary>
    public void UserCopiesTooLarge(ClipboardFormat format) =>
        ClipboardChanged?.Invoke(this, new ClipboardChange(null, false, false, IsTooLarge: true, TooLargeFormat: format));

    public Task WriteRemoteContentAsync(ClipboardContent content, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _content = content;
            _text = content.Format == ClipboardFormat.Text ? content.GetText() : null;
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
