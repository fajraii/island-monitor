param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) { & (Join-Path $root 'build.ps1') }
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain X.Y.Z' }
$exe = Join-Path $root 'bin\IslandMonitor.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the application first' }
$actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
if ($actualVersion -ne "$version.0") { throw 'AssemblyInfo.cs and VERSION do not match' }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$archive = Join-Path $dist "IslandMonitor-$version-windows.zip"
Compress-Archive -LiteralPath @($exe, (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'CHANGELOG.md'), (Join-Path $root 'CONTRIBUTING.md'), (Join-Path $root 'SECURITY.md'), (Join-Path $root 'docs')) -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Value "$hash  $([IO.Path]::GetFileName($archive))" -Encoding Ascii
Write-Output "Packaged $archive"
