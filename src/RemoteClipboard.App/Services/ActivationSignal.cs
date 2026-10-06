// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Principal;

namespace RemoteClipboard.App.Services;

/// <summary>
/// Lets a second launch ask the running instance to show its window. "Local\" scopes the event to the
/// current logon session, so other users of a shared server cannot trigger it.
/// </summary>
internal sealed class ActivationSignal : IDisposable
{
    private static readonly string Name = $@"Local\RemoteClipboard-Activate-{WindowsIdentity.GetCurrent().User?.Value}";

    private readonly EventWaitHandle _event = new(false, EventResetMode.AutoReset, Name);
    private readonly RegisteredWaitHandle _registration;

    public ActivationSignal(Action onActivated) =>
        _registration = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onActivated(), null, Timeout.Infinite, executeOnlyOnce: false);

    /// <summary>Called by a second instance. Returns false if no instance listens in this session.</summary>
    public static bool TrySignalRunningInstance()
    {
        if (!EventWaitHandle.TryOpenExisting(Name, out var existing))
        {
            return false;
        }

        using (existing)
        {
            return existing.Set();
        }
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _event.Dispose();
    }
}
