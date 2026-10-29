# Packages the browser extensions into dist/ for store submission.
# Builds ZIPs with forward-slash entry paths (required by AMO / Chrome Web Store);
# PowerShell's Compress-Archive uses backslashes, which breaks icon paths.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

function New-ExtZip([string]$srcDir, [string]$zipPath) {
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    $base = (Resolve-Path $srcDir).Path.TrimEnd('\')
    $fs = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
    $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $srcDir -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($base.Length + 1).Replace('\', '/')
            $entry = $zip.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::Optimal)
            $es = $entry.Open()
            $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
            $es.Write($bytes, 0, $bytes.Length)
            $es.Dispose()
        }
    }
    finally { $zip.Dispose(); $fs.Dispose() }
}

$chromeZip  = Join-Path $dist 'light-downloader-chrome.zip'
$firefoxZip = Join-Path $dist 'light-downloader-firefox.zip'
New-ExtZip "$root\extension\chrome"  $chromeZip
New-ExtZip "$root\extension\firefox" $firefoxZip

Write-Host ("Chrome : {0} ({1:N0} bytes)" -f $chromeZip, (Get-Item $chromeZip).Length)
Write-Host ("Firefox: {0} ({1:N0} bytes)" -f $firefoxZip, (Get-Item $firefoxZip).Length)
