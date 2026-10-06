// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Windows.Storage;

/// <summary>
/// Guarantees one running agent per Windows user, across all of that user's sessions (e.g. the same
/// user logged on twice through RDP), because identity and keys are per user profile.
/// Implemented as an exclusive lock on a file inside the user's own %LOCALAPPDATA%: other users cannot
/// see or squat it (unlike a Global\ named mutex), and the OS releases it if the process crashes.
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    private readonly FileStream _stream;

    private SingleInstanceLock(FileStream stream) => _stream = stream;

    public static bool TryAcquire(string directory, out SingleInstanceLock? instanceLock)
    {
        instanceLock = null;
        Directory.CreateDirectory(directory);
        try
        {
            var stream = new FileStream(
                Path.Combine(directory, "instance.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            instanceLock = new SingleInstanceLock(stream);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Dispose() => _stream.Dispose();
}
