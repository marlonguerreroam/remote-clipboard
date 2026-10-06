# Copyright (c) 2026 Marlon Andrés Guerrero Meriño
# SPDX-License-Identifier: GPL-3.0-only

# Builds an MSIX package (Microsoft Store) from an already published app folder.
#   - Store upload: pass the identity from Partner Center (-IdentityName, -Publisher,
#     -PublisherDisplayName, -DisplayName). The Store signs the package itself: leave it unsigned.
#   - Test (sideload): -TestSign signs it with a throwaway self-signed certificate whose subject is
#     -Publisher, and writes the public .cer next to the package. The private key never leaves the run.
param(
    [Parameter(Mandatory = $true)][string] $Version,      # X.Y.Z
    [Parameter(Mandatory = $true)][string] $SourceDir,
    [Parameter(Mandatory = $true)][string] $OutputPath,   # ...\Name.msix
    [string] $IdentityName = 'RemoteClipboard.Test',
    [string] $Publisher = 'CN=Remote Clipboard Test',
    [string] $PublisherDisplayName = 'Marlon Andrés Guerrero Meriño',
    [string] $DisplayName = 'Remote Clipboard',
    [switch] $TestSign
)
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, got '$Version'" }
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$template = Join-Path $root 'installer\msix'

# Windows SDK tools (installed on GitHub's Windows runners): newest version that has makeappx.
$sdkBin = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\makeappx.exe" |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
if (-not $sdkBin) { throw 'Windows SDK (makeappx.exe) not found' }
$tools = $sdkBin.DirectoryName
Write-Host "Windows SDK tools: $tools"

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("msix-" + [guid]::NewGuid())
$staging = Join-Path $work 'package'
$priRoot = Join-Path $work 'pri'
New-Item -ItemType Directory -Force $staging, $priRoot | Out-Null
try {
    # Manifest with the identity filled in (values XML-escaped).
    $escape = { param($v) [System.Security.SecurityElement]::Escape($v) }
    $manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $template 'AppxManifest.xml')
    $manifest = $manifest.Replace('$(IdentityName)', (& $escape $IdentityName))
    $manifest = $manifest.Replace('$(Publisher)', (& $escape $Publisher))
    $manifest = $manifest.Replace('$(PublisherDisplayName)', (& $escape $PublisherDisplayName))
    $manifest = $manifest.Replace('$(DisplayName)', (& $escape $DisplayName))
    $manifest = $manifest.Replace('$(Version)', "$Version.0")
    if ($manifest -match '\$\([A-Za-z]+\)') { throw "Unreplaced token in manifest: $($Matches[0])" }

    Copy-Item -Recurse (Join-Path $SourceDir '*') $staging
    foreach ($dir in @($staging, $priRoot)) {
        [System.IO.File]::WriteAllText((Join-Path $dir 'AppxManifest.xml'), $manifest, [System.Text.UTF8Encoding]::new($false))
        Copy-Item -Recurse (Join-Path $template 'Assets') $dir
    }

    # Resource index for the logos (scale/target-size variants). Built from the manifest and the logos
    # only, so the app's own files are not indexed.
    $priConfig = Join-Path $work 'priconfig.xml'
    & "$tools\makepri.exe" createconfig /cf $priConfig /dq es /pv 10.0.0 /o
    if ($LASTEXITCODE -ne 0) { throw "makepri createconfig failed ($LASTEXITCODE)" }
    & "$tools\makepri.exe" new /pr $priRoot /cf $priConfig /mn (Join-Path $priRoot 'AppxManifest.xml') /of (Join-Path $staging 'resources.pri') /o
    if ($LASTEXITCODE -ne 0) { throw "makepri new failed ($LASTEXITCODE)" }

    # Pack (validates the manifest against the schema).
    New-Item -ItemType Directory -Force (Split-Path $OutputPath) | Out-Null
    & "$tools\makeappx.exe" pack /d $staging /p $OutputPath /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed ($LASTEXITCODE)" }

    if ($TestSign) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature -KeyExportPolicy Exportable `
            -FriendlyName 'Remote Clipboard test package signing' -CertStoreLocation 'Cert:\CurrentUser\My' `
            -NotAfter (Get-Date).AddMonths(6) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        try {
            $pfx = Join-Path $work 'test.pfx'
            $password = [guid]::NewGuid().ToString()
            Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString $password -AsPlainText -Force) | Out-Null
            & "$tools\signtool.exe" sign /fd SHA256 /f $pfx /p $password $OutputPath
            if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
            Export-Certificate -Cert $cert -FilePath ([System.IO.Path]::ChangeExtension($OutputPath, '.cer')) | Out-Null
        }
        finally {
            Remove-Item -Force "Cert:\CurrentUser\My\$($cert.Thumbprint)"
        }
    }

    Write-Host "Built $OutputPath ($IdentityName $Version.0, $Publisher)"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
