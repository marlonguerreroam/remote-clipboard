// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Pairing;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.EndToEnd;

/// <summary>Images and files between real agents, and what devices that do not accept them get.</summary>
public class FormatTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task PairAsync(TestAgent host, TestAgent joiner)
    {
        var window = host.Agent.OpenPairing();
        var outcome = await joiner.Agent.PairWithAsync("127.0.0.1", host.Port, window.Code, Ct);
        Assert.Equal(PairingStatus.Success, outcome.Status);
        await Eventually.TrueAsync(() => host.IsConnectedTo(joiner) && joiner.IsConnectedTo(host), "both devices are connected");
    }

    [Theory]
    [InlineData(ClipboardFormat.Image, 3 * 1024 * 1024)]
    [InlineData(ClipboardFormat.FileList, 40 * 1024 * 1024)] // above the 32 MiB text/image limit
    public async Task Images_and_files_arrive_intact(ClipboardFormat format, int size)
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);
        var data = RandomNumberGenerator.GetBytes(size);

        a.Clipboard.UserCopies(ClipboardContent.FromBytes(format, data));

        await Eventually.TrueAsync(() => b.Clipboard.Content?.Format == format, $"{format} arrives", TimeSpan.FromSeconds(60));
        Assert.True(b.Clipboard.Content!.Data.Span.SequenceEqual(data));
    }

    [Fact]
    public async Task A_device_on_the_previous_version_only_receives_text_and_stays_connected()
    {
        await using var a = TestAgent.Start("PC-NUEVO");
        await using var old = TestAgent.Start("PC-VIEJO", accepts: [ClipboardFormat.Text]);
        await PairAsync(a, old);

        a.Clipboard.UserCopies(ClipboardContent.FromBytes(ClipboardFormat.Image, RandomNumberGenerator.GetBytes(1000)));
        a.Clipboard.UserCopies(ClipboardContent.FromBytes(ClipboardFormat.FileList, RandomNumberGenerator.GetBytes(1000)));
        a.Clipboard.UserCopies("solo texto");

        await Eventually.TrueAsync(() => old.Clipboard.Text == "solo texto", "text still arrives");
        Assert.Equal(1, old.Clipboard.RemoteWrites); // never an image or files
        Assert.True(a.IsConnectedTo(old));
    }

    [Fact]
    public async Task Too_large_copies_are_reported_and_not_sent()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);
        ClipboardFormat? reported = null;
        a.Agent.ContentTooLarge += (_, format) => reported = format;

        a.Clipboard.UserCopiesTooLarge(ClipboardFormat.FileList);

        await Eventually.TrueAsync(() => reported == ClipboardFormat.FileList, "the user is told");
        Assert.Equal(0, b.Clipboard.RemoteWrites);
    }

    [Fact]
    public void Limits_are_per_format()
    {
        Assert.Equal(ProtocolLimits.MaxClipboardBytes, ProtocolLimits.MaxContentBytes(ClipboardFormat.Image));
        Assert.Equal(ProtocolLimits.MaxFileListBytes, ProtocolLimits.MaxContentBytes(ClipboardFormat.FileList));
    }
}
