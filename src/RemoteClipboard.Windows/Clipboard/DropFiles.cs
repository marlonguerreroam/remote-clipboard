// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static RemoteClipboard.Windows.Interop.NativeMethods;

namespace RemoteClipboard.Windows.Clipboard;

/// <summary>CF_HDROP: the file list Explorer puts on the clipboard when files are copied.</summary>
[SupportedOSPlatform("windows")]
internal static class DropFiles
{
    private const int StructSize = 20; // DROPFILES: pFiles, POINT pt, fNC, fWide

    /// <summary>Paths in a CF_HDROP handle (the clipboard must be open).</summary>
    public static unsafe IReadOnlyList<string> Read(IntPtr hDrop)
    {
        var count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
        var paths = new List<string>((int)Math.Min(count, 10_000));
        for (uint i = 0; i < count && i < 10_000; i++)
        {
            var length = DragQueryFile(hDrop, i, null, 0);
            if (length == 0)
            {
                continue;
            }

            var buffer = new char[length + 1];
            fixed (char* pointer = buffer)
            {
                var copied = DragQueryFile(hDrop, i, pointer, length + 1);
                paths.Add(new string(buffer, 0, (int)copied));
            }
        }

        return paths;
    }

    /// <summary>DROPFILES structure followed by the wide, double-null-terminated path list.</summary>
    public static byte[] Build(IReadOnlyList<string> paths)
    {
        var list = string.Concat(paths.Select(p => p + '\0')) + '\0';
        var bytes = new byte[StructSize + (list.Length * sizeof(char))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, StructSize); // pFiles: offset of the list
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1); // fWide: UTF-16 paths
        MemoryMarshal.AsBytes(list.AsSpan()).CopyTo(bytes.AsSpan(StructSize));
        return bytes;
    }
}
