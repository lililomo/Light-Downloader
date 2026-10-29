# Regenerates every icon asset (app .ico, extension PNGs, store icons/promo tiles)
# from the vector source in art/IconGen.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
dotnet run --project "$root\art\IconGen\IconGen.csproj" -c Release -- "$root"
