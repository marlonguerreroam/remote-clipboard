// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Logging;
using RemoteClipboard.Core.Protocol;
using static RemoteClipboard.Windows.Interop.NativeMethods;

namespace RemoteClipboard.Windows.Clipboard;

/// <summary>
/// Win32 clipboard integration. A dedicated STA thread owns a message-only window registered with
/// AddClipboardFormatListener, so changes arrive as WM_CLIPBOARDUPDATE events (no polling).
/// All clipboard reads and writes run on that thread; slow work (archiving files, converting images)
/// runs on the thread pool so the clipboard is never held while it happens.
/// </summary>
/// <remarks>
/// <see cref="ClipboardChanged"/> is raised on the clipboard thread (text) or a pool thread (images,
/// files): handlers must return quickly. Content is not even read when the owner marked it as private
/// or when it was written by us. Priority when several formats are present: files, text, image.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboard : IClipboardMonitor, IClipboardWriter
{
    /// <summary>Private format added to everything written by Remote Clipboard (loop prevention).</summary>
    public const string RemoteOriginFormatName = "RemoteClipboard.RemoteOrigin";

    // Formats documented by Microsoft ("Cloud Clipboard and Clipboard History Formats") and the legacy
    // "Clipboard Viewer Ignore" convention, used by password managers to keep content private.
    private const string ExcludeFormatName = "ExcludeClipboardContentFromMonitorProcessing";
    private const string CanIncludeInHistoryFormatName = "CanIncludeInClipboardHistory";
    private const string CanUploadToCloudFormatName = "CanUploadToCloud";
    private const string ViewerIgnoreFormatName = "Clipboard Viewer Ignore";

    private const string PngFormatName = "PNG";
    private const string PreferredDropEffectFormatName = "Preferred DropEffect";
    private const int DropEffectCopy = 1;

    /// <summary>Raw bitmaps can be much larger than their PNG (a 4K screenshot is ~33 MB as CF_DIB).</summary>
    private const int MaxDibBytes = 256 * 1024 * 1024;

    private const int OpenAttempts = 6;

    private readonly ILogger _logger;
    private readonly int _maxContentBytes;
    private readonly uint _remoteOriginFormat;
    private readonly uint _excludeFormat;
    private readonly uint _canIncludeInHistoryFormat;
    private readonly uint _canUploadToCloudFormat;
    private readonly uint _viewerIgnoreFormat;
    private readonly uint _pngFormat;
    private readonly uint _preferredDropEffectFormat;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private SynchronizationContext? _context;
    private ListenerWindow? _window;
    private uint _lastSequence;
    private bool _disposed;

    public WindowsClipboard(ILogger<WindowsClipboard>? logger = null, int maxContentBytes = ProtocolLimits.MaxClipboardBytes)
    {
        _logger = logger ?? NullLogger<WindowsClipboard>.Instance;
        _maxContentBytes = maxContentBytes;
        _remoteOriginFormat = Register(RemoteOriginFormatName);
        _excludeFormat = Register(ExcludeFormatName);
        _canIncludeInHistoryFormat = Register(CanIncludeInHistoryFormatName);
        _canUploadToCloudFormat = Register(CanUploadToCloudFormatName);
        _viewerIgnoreFormat = Register(ViewerIgnoreFormatName);
        _pngFormat = Register(PngFormatName);
        _preferredDropEffectFormat = Register(PreferredDropEffectFormatName);
        ReceivedFiles = new ReceivedFilesStore(Path.Combine(Path.GetTempPath(), "RemoteClipboard-received"));
    }

    /// <summary>Send copied images (Settings). When off, images are not even read.</summary>
    public bool ReadImages { get; set; } = true;

    /// <summary>Send copied files and folders (Settings). When off, they are not even read.</summary>
    public bool ReadFiles { get; set; } = true;

    /// <summary>Where received files are extracted (they must exist on disk to be pasted).</summary>
    public ReceivedFilesStore ReceivedFiles { get; set; }

    public event EventHandler<ClipboardChange>? ClipboardChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "RemoteClipboard.Clipboard" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Task.GetAwaiter().GetResult();
    }

    public async Task WriteRemoteContentAsync(ClipboardContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        // Extraction and image conversion happen before the clipboard is opened.
        var formats = await Task.Run(() => Prepare(content), cancellationToken).ConfigureAwait(false);
        await InvokeAsync(() => WriteCore(formats), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the current clipboard (used by tests and diagnostics), including slow conversions.</summary>
    public async Task<ClipboardChange?> ReadAsync(CancellationToken cancellationToken)
    {
        var read = await InvokeAsync(ReadSnapshot, cancellationToken).ConfigureAwait(false);
        return read.Deferred is { } deferred
            ? await Task.Run(deferred, cancellationToken).ConfigureAwait(false)
            : read.Change;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_thread is not null && _context is not null)
        {
            _context.Post(_ => Application.ExitThread(), null);
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private void Run()
    {
        try
        {
            using var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            _context = context;
            _window = new ListenerWindow(this);
            if (!AddClipboardFormatListener(_window.Handle))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "AddClipboardFormatListener failed.");
            }

            _lastSequence = GetClipboardSequenceNumber();
            _ready.TrySetResult();
            Application.Run();
        }
#pragma warning disable CA1031 // Startup failure is surfaced to Start(); never crash the thread silently.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _ready.TrySetException(ex);
        }
        finally
        {
            if (_window is not null)
            {
                RemoveClipboardFormatListener(_window.Handle);
                _window.DestroyHandle();
            }
        }
    }

    private void OnClipboardUpdate()
    {
        var sequence = GetClipboardSequenceNumber();
        if (sequence == _lastSequence)
        {
            return;
        }

        _lastSequence = sequence;
        var read = ReadSnapshot();
        if (read.Deferred is { } deferred)
        {
            _ = Task.Run(() =>
            {
                var change = SafeBuild(deferred);
                // The user copied something else meanwhile: that newer change wins.
                if (change is not null && GetClipboardSequenceNumber() == sequence)
                {
                    Raise(change);
                }
            });
            return;
        }

        if (read.Change is { } immediate)
        {
            Raise(immediate);
        }
    }

    private ClipboardChange? SafeBuild(Func<ClipboardChange?> build)
    {
        try
        {
            return build();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Log.UnexpectedError(_logger, "clipboard read", ex);
            return null;
        }
    }

    private void Raise(ClipboardChange change)
    {
        Log.ClipboardEventDetected(_logger, change.Content?.Format ?? change.TooLargeFormat);
        try
        {
            ClipboardChanged?.Invoke(this, change);
        }
#pragma warning disable CA1031 // A faulty handler must not kill the clipboard thread.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.UnexpectedError(_logger, "clipboard change handler", ex);
        }
    }

    /// <summary>
    /// What the clipboard holds. Copies raw data only while the clipboard is open; <c>Deferred</c> does
    /// the slow part (archiving, image conversion) afterwards, off the clipboard thread.
    /// </summary>
    private (ClipboardChange? Change, Func<ClipboardChange?>? Deferred) ReadSnapshot()
    {
        if (!TryOpenClipboard())
        {
            Log.ClipboardBusy(_logger);
            return (null, null);
        }

        try
        {
            var hasMarker = IsClipboardFormatAvailable(_remoteOriginFormat);
            var excluded = IsClipboardFormatAvailable(_excludeFormat)
                || IsClipboardFormatAvailable(_viewerIgnoreFormat)
                || DwordFormatIsZero(_canIncludeInHistoryFormat)
                || DwordFormatIsZero(_canUploadToCloudFormat);
            if (hasMarker || excluded)
            {
                // Never read content we are not going to send.
                return (new ClipboardChange(null, hasMarker, excluded), null);
            }

            if (ReadFiles && IsClipboardFormatAvailable(CF_HDROP))
            {
                return ReadFileList();
            }

            if (IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                return (ReadText(), null);
            }

            if (ReadImages && IsClipboardFormatAvailable(_pngFormat) && ReadGlobal(_pngFormat, _maxContentBytes) is { } png
                && ImageCodec.IsPng(png))
            {
                return (new ClipboardChange(ClipboardContent.FromBytes(ClipboardFormat.Image, png), false, false), null);
            }

            if (ReadImages && IsClipboardFormatAvailable(CF_DIB))
            {
                return ReadDib();
            }

            return (new ClipboardChange(null, false, false), null);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private ClipboardChange ReadText()
    {
        var handle = GetClipboardData(CF_UNICODETEXT);
        if (handle == IntPtr.Zero)
        {
            return new ClipboardChange(null, false, false);
        }

        var maxChars = (long)(GlobalSize(handle) / 2);
        // Every UTF-16 char is at least one UTF-8 byte: more chars than the byte limit can never fit.
        if (maxChars - 1 > _maxContentBytes)
        {
            return new ClipboardChange(null, false, false, IsTooLarge: true);
        }

        var text = ReadUnicodeText(handle, (int)maxChars);
        var content = ClipboardContent.FromText(text);
        return content.Length > _maxContentBytes
            ? new ClipboardChange(null, false, false, IsTooLarge: true)
            : new ClipboardChange(content, false, false);
    }

    private static (ClipboardChange?, Func<ClipboardChange?>?) ReadFileList()
    {
        var handle = GetClipboardData(CF_HDROP);
        var paths = handle == IntPtr.Zero ? [] : DropFiles.Read(handle);
        if (paths.Count == 0)
        {
            return (new ClipboardChange(null, false, false), null);
        }

        return (null, () =>
        {
            if (!paths.Any(p => File.Exists(p) || Directory.Exists(p)))
            {
                return new ClipboardChange(null, false, false);
            }

            var archive = FileArchive.Create(paths, ProtocolLimits.MaxFileListBytes);
            return archive is null
                ? new ClipboardChange(null, false, false, IsTooLarge: true, TooLargeFormat: ClipboardFormat.FileList)
                : new ClipboardChange(ClipboardContent.FromBytes(ClipboardFormat.FileList, archive), false, false);
        });
    }

    private (ClipboardChange?, Func<ClipboardChange?>?) ReadDib()
    {
        var dib = ReadGlobal(CF_DIB, MaxDibBytes);
        if (dib is null)
        {
            return (new ClipboardChange(null, false, false, IsTooLarge: true, TooLargeFormat: ClipboardFormat.Image), null);
        }

        return (null, () =>
        {
            var png = ImageCodec.DibToPng(dib);
            return png.Length > _maxContentBytes
                ? new ClipboardChange(null, false, false, IsTooLarge: true, TooLargeFormat: ClipboardFormat.Image)
                : new ClipboardChange(ClipboardContent.FromBytes(ClipboardFormat.Image, png), false, false);
        });
    }

    /// <summary>Copy of a clipboard memory block, or null when it is larger than <paramref name="maxBytes"/>.</summary>
    private static unsafe byte[]? ReadGlobal(uint format, int maxBytes)
    {
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero)
        {
            return [];
        }

        var size = GlobalSize(handle);
        if (size > (nuint)maxBytes)
        {
            return null;
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            return new ReadOnlySpan<byte>((void*)pointer, (int)size).ToArray();
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>The clipboard formats to write for received content (slow work happens here, off-thread).</summary>
    private List<(uint Format, byte[] Data, bool AppendNullChar)> Prepare(ClipboardContent content)
    {
        switch (content.Format)
        {
            case ClipboardFormat.Text:
                return [(CF_UNICODETEXT, MemoryMarshal.AsBytes(content.GetText().AsSpan()).ToArray(), true)];

            case ClipboardFormat.Image:
                var png = content.Data.ToArray();
                // PNG keeps transparency for apps that read it; CF_DIB is what every app understands.
                return [(CF_DIB, ImageCodec.PngToDib(png), false), (_pngFormat, png, false)];

            case ClipboardFormat.FileList:
                var directory = ReceivedFiles.CreateTransferDirectory();
                try
                {
                    var paths = FileArchive.Extract(content.Data, directory, ProtocolLimits.MaxFileListBytes);
                    MarkOfTheWeb.Apply(paths);
                    return
                    [
                        (CF_HDROP, DropFiles.Build(paths), false),
                        // Paste copies (never moves) out of the received-files folder.
                        (_preferredDropEffectFormat, BitConverter.GetBytes(DropEffectCopy), false),
                    ];
                }
                catch
                {
                    ReceivedFilesStore.TryDelete(new DirectoryInfo(directory));
                    throw;
                }

            default:
                throw new InvalidOperationException($"Unsupported clipboard format {content.Format}.");
        }
    }

    private void WriteCore(List<(uint Format, byte[] Data, bool AppendNullChar)> formats)
    {
        if (!TryOpenClipboard())
        {
            throw new IOException("The clipboard is in use by another application.");
        }

        try
        {
            if (!EmptyClipboard())
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "EmptyClipboard failed.");
            }

            foreach (var (format, data, appendNullChar) in formats)
            {
                SetGlobalData(format, data, appendNullChar);
            }

            SetGlobalData(_remoteOriginFormat, BitConverter.GetBytes(1), appendNullChar: false);
            // Privacy: content received from another device never goes to Windows cloud clipboard or history.
            SetGlobalData(_canUploadToCloudFormat, BitConverter.GetBytes(0), appendNullChar: false);
            SetGlobalData(_canIncludeInHistoryFormat, BitConverter.GetBytes(0), appendNullChar: false);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static unsafe string ReadUnicodeText(IntPtr handle, int maxChars)
    {
        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var span = new ReadOnlySpan<char>((void*)pointer, maxChars);
            var end = span.IndexOf('\0');
            return new string(end >= 0 ? span[..end] : span);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static unsafe bool DwordFormatIsZero(uint format)
    {
        if (!IsClipboardFormatAvailable(format))
        {
            return false;
        }

        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero || GlobalSize(handle) < sizeof(int))
        {
            return false;
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return *(int*)pointer == 0;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static unsafe void SetGlobalData(uint format, ReadOnlySpan<byte> data, bool appendNullChar)
    {
        var size = data.Length + (appendNullChar ? sizeof(char) : 0);
        var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)Math.Max(size, 1));
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GlobalAlloc failed.");
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GlobalLock failed.");
        }

        try
        {
            var target = new Span<byte>((void*)pointer, size);
            data.CopyTo(target);
            target[data.Length..].Clear();
        }
        finally
        {
            GlobalUnlock(handle);
        }

        // On success the system owns the memory; on failure we must free it.
        if (SetClipboardData(format, handle) == IntPtr.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            GlobalFree(handle);
            throw new Win32Exception(error, "SetClipboardData failed.");
        }
    }

    private bool TryOpenClipboard()
    {
        // Another application may hold the clipboard briefly. Bounded retries, only after an event.
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(_window!.Handle))
            {
                return true;
            }

            Thread.Sleep(10 << attempt);
        }

        return false;
    }

    private Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var context = _context ?? throw new InvalidOperationException("Call Start() first.");
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        context.Post(_ =>
        {
            try
            {
                tcs.TrySetResult(action());
            }
#pragma warning disable CA1031 // Marshalled to the caller.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    private Task<bool> InvokeAsync(Action action, CancellationToken cancellationToken) =>
        InvokeAsync(() =>
        {
            action();
            return true;
        }, cancellationToken);

    private static uint Register(string name)
    {
        var format = RegisterClipboardFormat(name);
        return format != 0 ? format : throw new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterClipboardFormat failed.");
    }

    private sealed class ListenerWindow : NativeWindow
    {
        private readonly WindowsClipboard _owner;

        public ListenerWindow(WindowsClipboard owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE, Caption = "RemoteClipboard.Listener" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                _owner.OnClipboardUpdate();
            }

            base.WndProc(ref m);
        }
    }
}
