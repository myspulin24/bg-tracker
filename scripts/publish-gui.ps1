[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\Tracker.Desktop\Tracker.Desktop.csproj'
$output = Join-Path $repositoryRoot "artifacts\desktop\$Runtime"

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

# IncludeNativeLibrariesForSelfExtract zabalí i nativní knihovny WPF, jinak vedle .exe zůstane
# pět DLL a samotný .exe poslaný dál nefunguje. Komprese sráží velikost ze 155 MB na zhruba 69 MB.
dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none

$exe = Join-Path $output 'BattlegroundsTracker.exe'
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  BattlegroundsTracker.exe" | Out-File -FilePath "$exe.sha256" -Encoding ascii -NoNewline

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$version = (Get-Item $exe).VersionInfo.ProductVersion
Write-Host "Published Battlegrounds Tracker $version: $exe ($size MB)"
Write-Host "SHA256: $hash"
