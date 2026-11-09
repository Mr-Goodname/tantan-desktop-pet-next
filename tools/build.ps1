param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $DotnetArguments
)

# Run from this project's pinned SDK. An incomplete machine SDK is repaired only
# in a local copy; no Program Files content or persistent environment is changed.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previousSdkPath = $env:MSBuildSDKsPath
$previousBundledVersions = $env:NETCoreSdkBundledVersionsProps
Push-Location -LiteralPath $projectRoot
try {
    $sdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the .NET SDK.' }
    $dotnetDirectory = Split-Path -Parent (Get-Command dotnet).Source
    $sdkDirectory = Join-Path $dotnetDirectory ('sdk\' + $sdkVersion)
    $sourceSdks = Join-Path $sdkDirectory 'Sdks'
    $optionalCacheTargets = Join-Path $sourceSdks 'Microsoft.NET.Sdk\targets\Microsoft.NET.NuGetOfflineCache.targets'
    if (!(Test-Path -LiteralPath $optionalCacheTargets)) {
        $shimName = if ($sdkVersion -eq '8.0.421') { 'sdk-shim8' } else { 'sdk-shim-' + $sdkVersion }
        $shimSdks = Join-Path (Join-Path $PSScriptRoot $shimName) 'Sdks'
        $shimCore = Join-Path $shimSdks 'Microsoft.NET.Sdk'
        $shimCacheTargets = Join-Path $shimCore 'targets\Microsoft.NET.NuGetOfflineCache.targets'
        if (!(Test-Path -LiteralPath $shimCacheTargets)) {
            New-Item -ItemType Directory -Force -Path $shimSdks | Out-Null
            Copy-Item -LiteralPath (Join-Path $sourceSdks 'Microsoft.NET.Sdk') -Destination $shimSdks -Recurse -Force
            foreach ($sourceSdk in Get-ChildItem -LiteralPath $sourceSdks -Directory) {
                if ($sourceSdk.Name -eq 'Microsoft.NET.Sdk') { continue }
                $shimSdk = Join-Path $shimSdks $sourceSdk.Name
                if (!(Test-Path -LiteralPath $shimSdk)) {
                    New-Item -ItemType Junction -Path $shimSdk -Target $sourceSdk.FullName | Out-Null
                }
            }
            # This legacy optional cache adds fallback NuGet sources only. Ordinary
            # online/cache package restore does not need those sources.
            Set-Content -LiteralPath $shimCacheTargets -Encoding utf8 -Value '<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003" />'
        }
        $env:MSBuildSDKsPath = $shimSdks
        $env:NETCoreSdkBundledVersionsProps = Join-Path $sdkDirectory 'Microsoft.NETCoreSdk.BundledVersions.props'
        Write-Host ('Using local SDK workaround for ' + $sdkVersion + ' (system files untouched).')
    }
    if (!$DotnetArguments -or $DotnetArguments.Count -eq 0) {
        $DotnetArguments = @('build', 'BonbonPet.csproj', '-c', 'Release')
    }
    & dotnet @DotnetArguments
    $buildExitCode = $LASTEXITCODE
}
finally {
    $env:MSBuildSDKsPath = $previousSdkPath
    $env:NETCoreSdkBundledVersionsProps = $previousBundledVersions
    Pop-Location
}
exit $buildExitCode
