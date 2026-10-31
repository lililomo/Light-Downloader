# Builds installer\LightDownloaderSetup.exe (app + browser extensions) with Inno Setup.
# Needs Inno Setup 6: https://jrsoftware.org/isdl.php
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (ISCC.exe)." }

$csproj = [xml](Get-Content "$root\src\LightDownloader.App\LightDownloader.App.csproj")
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)

Write-Host "1/3  Publishing app $version..."
Remove-Item "$root\publish\app" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\LightDownloader.App\LightDownloader.App.csproj" -c Release -o "$root\publish\app" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "2/3  Staging files (app + extensions)..."
$stage = "$root\publish\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item "$root\publish\app\*" $stage -Recurse -Force
Get-ChildItem $stage -Filter *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$stage\extension" | Out-Null
Copy-Item "$root\extension\chrome"  "$stage\extension\chrome"  -Recurse -Force
Copy-Item "$root\extension\firefox" "$stage\extension\firefox" -Recurse -Force

Write-Host "3/3  Compiling the setup wizard..."
& $iscc /Q "/DAppVersion=$version" @args "$root\setup\LightDownloader.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

Write-Host "Done: $root\installer\LightDownloaderSetup.exe ($version)"
