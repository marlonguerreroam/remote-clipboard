// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using static RemoteClipboard.Windows.Interop.NativeMethods;

namespace RemoteClipboard.Windows.Tests;

/// <summary>
/// Test-side clipboard access through raw Win32 calls (no OLE, so no STA requirement), playing the role of
/// "another application" that copies or reads text.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RawClipboard
{
    private static readonly Lock Gate = new();
    private static NativeWindow? _owner;

    public static unsafe void SetText(string text, params string[] markerFormats)
    {
        lock (Gate)
        {
            // SetClipboardData fails when the clipboard was opened without an owner window.
            _owner ??= CreateOwner();
            Open(_owner.Handle);
            try
            {
                Check(EmptyClipboard(), "EmptyClipboard");
                var bytes = new byte[(text.Length + 1) * 2];
                MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
                SetGlobal(CF_UNICODETEXT, bytes);
                foreach (var format in markerFormats)
                {
                    SetGlobal(RegisterClipboardFormat(format), [0, 0, 0, 0]);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
    }

    public static unsafe string? GetText()
    {
        lock (Gate)
        {
            Open(IntPtr.Zero);
            try
            {
                var handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero)
                {
                    return null;
                }

                var pointer = GlobalLock(handle);
                try
                {
                    var span = new ReadOnlySpan<char>((void*)pointer, (int)(GlobalSize(handle) / 2));
                    var end = span.IndexOf('\0');
                    return new string(end >= 0 ? span[..end] : span);
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
    }

    private static NativeWindow CreateOwner()
    {
        var window = new NativeWindow();
        window.CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        return window;
    }

    private static void Open(IntPtr owner)
    {
        // The product's listener may be reading right after each change: retry briefly.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (OpenClipboard(owner))
            {
                return;
            }

            Thread.Sleep(25);
        }

        throw new IOException("Clipboard busy.");
    }

    private static unsafe void SetGlobal(uint format, byte[] data)
    {
        var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
        var pointer = GlobalLock(handle);
        data.CopyTo(new Span<byte>((void*)pointer, data.Length));
        GlobalUnlock(handle);
        if (SetClipboardData(format, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new IOException("SetClipboardData failed.");
        }
    }

    private static void Check(bool ok, string api)
    {
        if (!ok)
        {
            throw new IOException($"{api} failed ({Marshal.GetLastPInvokeError()}).");
        }
    }
}
