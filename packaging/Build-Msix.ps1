param(
    [Parameter(Mandatory = $true)][string]$PublishedDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Publisher
)

$ErrorActionPreference = 'Stop'
$published = [IO.Path]::GetFullPath($PublishedDirectory)
$output = [IO.Path]::GetFullPath($OutputPath)
$template = Join-Path $PSScriptRoot 'AppxManifest.xml.template'
if ([string]::IsNullOrWhiteSpace($Publisher)) { throw 'MSIX Publisher cannot be empty.' }
if (-not (Test-Path -LiteralPath (Join-Path $published 'WinNotch.exe'))) {
    throw 'Published WinNotch.exe was not found.'
}
$parts = $Version.TrimStart('v', 'V').Split('.')
if ($parts.Count -gt 4 -or $parts.Count -lt 1 -or ($parts | Where-Object { $_ -notmatch '^\d+$' }).Count -gt 0) {
    throw "Invalid MSIX version: $Version"
}
while ($parts.Count -lt 4) { $parts += '0' }
$packageVersion = $parts -join '.'
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$layout = [IO.Path]::GetFullPath((Join-Path $tempBase ('winnotch-msix-' + [Guid]::NewGuid().ToString('N'))))
if (-not $layout.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe MSIX staging path.' }

try {
    New-Item -ItemType Directory -Path $layout | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $layout 'Assets') | Out-Null
    Get-ChildItem -LiteralPath $published -File |
        Where-Object { $_.Extension -notin @('.pdb', '.xml') } |
        Copy-Item -Destination $layout
    $manifest = [IO.File]::ReadAllText($template)
    $manifest = $manifest.Replace('__VERSION__', $packageVersion).Replace(
        '__PUBLISHER__', [Security.SecurityElement]::Escape($Publisher))
    [IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))

    Add-Type -AssemblyName System.Drawing.Common
    $icon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $layout 'WinNotch.exe'))
    if ($null -eq $icon) { throw 'Could not extract WinNotch icon.' }
    $source = $icon.ToBitmap()
    try {
        foreach ($asset in @(
            @{ Name = 'StoreLogo.png'; Width = 50; Height = 50 },
            @{ Name = 'Square44x44Logo.png'; Width = 44; Height = 44 },
            @{ Name = 'Square150x150Logo.png'; Width = 150; Height = 150 },
            @{ Name = 'Wide310x150Logo.png'; Width = 310; Height = 150 }
        )) {
            $bitmap = [Drawing.Bitmap]::new($asset.Width, $asset.Height)
            try {
                $graphics = [Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([Drawing.Color]::Transparent)
                    $side = [Math]::Min($asset.Width, $asset.Height)
                    $left = [int](($asset.Width - $side) / 2)
                    $graphics.DrawImage($source, $left, 0, $side, $side)
                } finally { $graphics.Dispose() }
                $bitmap.Save((Join-Path $layout ('Assets\' + $asset.Name)), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $bitmap.Dispose() }
        }
    } finally {
        $source.Dispose()
        $icon.Dispose()
    }

    $makeAppx = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if ($null -eq $makeAppx) {
        $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
        $kitRoot = Join-Path $programFilesX86 'Windows Kits\10\bin'
        $makeAppx = Get-ChildItem -LiteralPath $kitRoot -Filter makeappx.exe -Recurse |
            Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
            Sort-Object FullName -Descending | Select-Object -First 1
    }
    if ($null -eq $makeAppx) { throw 'MakeAppx.exe was not found in the Windows SDK.' }
    $makeAppxPath = if ($makeAppx -is [Management.Automation.CommandInfo]) { $makeAppx.Source } else { $makeAppx.FullName }
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($output)) -Force | Out-Null
    & $makeAppxPath pack /d $layout /p $output /o
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }
} finally {
    if (Test-Path -LiteralPath $layout) {
        $resolved = [IO.Path]::GetFullPath($layout)
        if (-not $resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Unsafe MSIX staging cleanup path.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
