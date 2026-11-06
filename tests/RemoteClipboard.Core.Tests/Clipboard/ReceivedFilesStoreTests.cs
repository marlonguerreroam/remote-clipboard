// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Clipboard;

public sealed class ReceivedFilesStoreTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);

    public void Dispose() => _root.Dispose();

    [Fact]
    public void Transfers_older_than_a_day_are_deleted()
    {
        var store = new ReceivedFilesStore(_root.Path, _time);
        var old = store.CreateTransferDirectory();
        File.WriteAllText(Path.Combine(old, "f.txt"), "x");

        _time.Advance(TimeSpan.FromHours(25));
        var fresh = store.CreateTransferDirectory();

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh));
    }

    [Fact]
    public void Only_the_newest_transfers_are_kept()
    {
        var store = new ReceivedFilesStore(_root.Path, _time);
        var created = new List<string>();
        for (var i = 0; i < ReceivedFilesStore.MaxTransfers + 3; i++)
        {
            created.Add(store.CreateTransferDirectory());
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        store.Cleanup();

        Assert.Equal(ReceivedFilesStore.MaxTransfers, Directory.GetDirectories(_root.Path).Length);
        Assert.True(Directory.Exists(created[^1]));
        Assert.False(Directory.Exists(created[0]));
    }
}
