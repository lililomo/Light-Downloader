# Builds installer\LightDownloaderSetup.exe (app + browser extensions, single file).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "1/3  Publishing app..."
dotnet publish "$root\src\LightDownloader.App\LightDownloader.App.csproj" -c Release -o "$root\publish\app" | Out-Null

Write-Host "2/3  Staging payload (app + extensions)..."
$stage = "$root\publish\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item "$root\publish\app\*" $stage -Recurse -Force
Get-ChildItem $stage -Filter *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$stage\extension" | Out-Null
Copy-Item "$root\extension\chrome"  "$stage\extension\chrome"  -Recurse -Force
Copy-Item "$root\extension\firefox" "$stage\extension\firefox" -Recurse -Force
$zip = "$root\src\LightDownloader.Setup\payload.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$stage\*" -DestinationPath $zip -Force

Write-Host "3/3  Compiling setup.exe..."
dotnet publish "$root\src\LightDownloader.Setup\LightDownloader.Setup.csproj" `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false `
    -o "$root\installer" | Out-Null

Write-Host "Done: $root\installer\LightDownloaderSetup.exe"
