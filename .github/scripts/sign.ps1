# Copyright (c) 2026 Marlon Andrés Guerrero Meriño
# SPDX-License-Identifier: GPL-3.0-only

# Authenticode-signs files when a code signing certificate is configured as repository secrets:
#   CODESIGN_PFX_BASE64 (the .pfx, base64) and CODESIGN_PFX_PASSWORD.
# Without them this script is never called. The certificate never touches the repository.
param([Parameter(Mandatory = $true)][string[]] $Files)
$ErrorActionPreference = 'Stop'

$pfx = Join-Path $env:RUNNER_TEMP 'codesign.pfx'
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:CODESIGN_PFX_BASE64))
try {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName | Select-Object -Last 1
    if (-not $signtool) { throw 'signtool.exe not found' }
    foreach ($file in $Files) {
        & $signtool.FullName sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f $pfx /p $env:CODESIGN_PFX_PASSWORD $file
        if ($LASTEXITCODE -ne 0) { throw "signtool failed for $file" }
    }
}
finally {
    Remove-Item $pfx -Force -ErrorAction SilentlyContinue
}
