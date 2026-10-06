// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.Versioning;
using RemoteClipboard.Windows.Platform;
using RemoteClipboard.Windows.Storage;

namespace RemoteClipboard.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class PackageInfoTests
{
    [Fact]
    public void A_classic_process_is_not_packaged()
    {
        // The test host runs outside any MSIX package: the real API must answer "no package".
        Assert.False(PackageInfo.IsPackaged);
        Assert.Null(PackageInfo.FamilyName);
        Assert.Equal(AppPaths.LogsDirectory, AppPaths.LogsDirectoryForShell);
    }
}
