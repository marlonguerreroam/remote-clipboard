# Copyright (c) 2026 Marlon Andrés Guerrero Meriño
# SPDX-License-Identifier: GPL-3.0-only

# Builds installer\Output\RemoteClipboardSetup-v<Version>.exe from an already published app folder.
param(
    [Parameter(Mandatory = $true)][string] $Version,
    [Parameter(Mandatory = $true)][string] $SourceDir
)
$ErrorActionPreference = 'Stop'

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
    choco install innosetup -y --no-progress | Out-Host
}
if (-not (Test-Path $iscc)) { throw "Inno Setup compiler not found at $iscc" }

& $iscc "/DAppVersion=$Version" "/DSourceDir=$((Resolve-Path $SourceDir).Path)" (Join-Path $PSScriptRoot '..\..\installer\RemoteClipboard.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
