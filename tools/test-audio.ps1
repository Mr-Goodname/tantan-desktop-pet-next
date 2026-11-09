param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& $Dotnet run --project (Join-Path $projectRoot 'Tests\Audio\AudioCheck.csproj') -c Release -- (Join-Path $projectRoot 'Tests\audio-result.json')
exit $LASTEXITCODE
