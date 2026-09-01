[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\Tracker.App\Tracker.App.csproj'
$output = Join-Path $repositoryRoot "artifacts\publish\$Runtime"

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true

Write-Host "Published application: $output\HearthstoneBattlegroundsTracker.exe"
