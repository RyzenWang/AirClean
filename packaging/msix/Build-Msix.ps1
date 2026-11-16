#requires -Version 5.1
<#
.SYNOPSIS
    Packs the published Windows build into a Microsoft Store (.msix) package.

.DESCRIPTION
    The Store re-signs uploads, so this script never touches a certificate. It generates the
    tile assets from the app icon, writes the manifest from the template with the identity
    and a four-part version, and calls makeappx from the installed Windows SDK.

.PARAMETER PublishDir
    Directory produced by `dotnet publish` (contains AirClean.exe).

.PARAMETER Version
    Release version such as 0.6.0; converted to the four-part form the manifest requires.

.PARAMETER Output
    Path of the .msix to write.

.PARAMETER Identity
    Package identity name. Must match the name reserved in Partner Center for a Store
    submission.

.PARAMETER Publisher
    Publisher subject. Must match the Store identity; the default is a local placeholder.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $PublishDir,
    [Parameter(Mandatory = $true)][string] $Version,
    [Parameter(Mandatory = $true)][string] $Output,
    [string] $Identity = 'RyzenWang.AirClean',
    [string] $Publisher = 'CN=AirClean contributors'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Find-MakeAppx {
    $kits = 'C:\Program Files (x86)\Windows Kits\10\bin'
    $candidates = Get-ChildItem -Path $kits -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } |
        Where-Object { Test-Path $_ }

    $found = $candidates | Select-Object -First 1
    if (-not $found) {
        throw "makeappx.exe was not found under $kits. Install the Windows SDK packaging tools."
    }

    return $found
}

function ConvertTo-FourPartVersion([string] $value) {
    # The manifest wants major.minor.build.revision, each numeric and below 65536.
    $clean = ($value -split '[-+]')[0]
    $parts = @($clean.Split('.') | ForEach-Object { [int]$_ })
    while ($parts.Count -lt 4) { $parts += 0 }
    if ($parts.Count -gt 4) { $parts = $parts[0..3] }

    foreach ($part in $parts) {
        if ($part -lt 0 -or $part -gt 65535) {
            throw "Version part $part is outside the 0-65535 range the manifest allows."
        }
    }

    return ($parts -join '.')
}

function New-TileAsset([System.Drawing.Image] $source, [string] $path, [int] $width, [int] $height) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($source, 0, 0, $width, $height)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$publishPath = (Resolve-Path $PublishDir).Path
$exe = Join-Path $publishPath 'AirClean.exe'
if (-not (Test-Path $exe)) {
    throw "The publish directory has no AirClean.exe: $publishPath"
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("airclean-msix-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $staging | Out-Null

try {
    Copy-Item -Path (Join-Path $publishPath '*') -Destination $staging -Recurse

    $assets = Join-Path $staging 'Assets'
    New-Item -ItemType Directory -Path $assets | Out-Null

    $iconPath = Join-Path $PSScriptRoot '..\..\src\AirClean.App\Assets\airclean.png'
    $icon = [System.Drawing.Image]::FromFile((Resolve-Path $iconPath).Path)
    try {
        New-TileAsset $icon (Join-Path $assets 'Square44x44Logo.png') 44 44
        New-TileAsset $icon (Join-Path $assets 'Square150x150Logo.png') 150 150
        New-TileAsset $icon (Join-Path $assets 'StoreLogo.png') 50 50
        New-TileAsset $icon (Join-Path $assets 'Wide310x150Logo.png') 310 150
    }
    finally {
        $icon.Dispose()
    }

    $fourPart = ConvertTo-FourPartVersion $Version
    $manifest = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
    $manifest = $manifest.Replace('__IDENTITY__', $Identity)
    $manifest = $manifest.Replace('__PUBLISHER__', $Publisher)
    $manifest = $manifest.Replace('__VERSION__', $fourPart)
    Set-Content -Path (Join-Path $staging 'AppxManifest.xml') -Value $manifest -Encoding UTF8

    $outputPath = [System.IO.Path]::GetFullPath($Output)
    $outputDir = [System.IO.Path]::GetDirectoryName($outputPath)
    if ($outputDir -and -not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir | Out-Null
    }

    $makeappx = Find-MakeAppx
    & $makeappx pack /d $staging /p $outputPath /o | Write-Output
    if ($LASTEXITCODE -ne 0) {
        throw "makeappx failed with exit code $LASTEXITCODE."
    }

    $size = (Get-Item $outputPath).Length / 1MB
    Write-Output ("MSIX {0} ({1:N1} MB, identity {2}, version {3})" -f $outputPath, $size, $Identity, $fourPart)
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
