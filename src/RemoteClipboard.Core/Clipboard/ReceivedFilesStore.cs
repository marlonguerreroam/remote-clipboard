// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Clipboard;

/// <summary>
/// Private folder for files received from other devices. They must exist on disk to be pasted, but they
/// are not kept: each transfer gets its own subfolder, and transfers older than <see cref="MaxAge"/>
/// (or beyond the newest <see cref="MaxTransfers"/>) are deleted. Nothing else ever goes in this folder.
/// </summary>
public sealed class ReceivedFilesStore(string root, TimeProvider? time = null)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    public const int MaxTransfers = 10;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>Removes old transfers, then creates an empty folder for a new one.</summary>
    public string CreateTransferDirectory()
    {
        Cleanup();
        var directory = Path.Combine(Root, $"{_time.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24]);
        Directory.CreateDirectory(directory);
        Directory.SetLastWriteTimeUtc(directory, _time.GetUtcNow().UtcDateTime);
        return directory;
    }

    /// <summary>Best effort: a file still open in another program is retried next time.</summary>
    public void Cleanup()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var transfers = new DirectoryInfo(Root).GetDirectories()
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .ToList();
        for (var i = 0; i < transfers.Count; i++)
        {
            if (i >= MaxTransfers || now - transfers[i].LastWriteTimeUtc > MaxAge)
            {
                TryDelete(transfers[i]);
            }
        }
    }

    /// <summary>Deletes one transfer folder (e.g. after a failed extraction).</summary>
    public static void TryDelete(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        try
        {
            directory.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use; the next cleanup removes it.
        }
    }
}
