param(
    [string]$DotnetPath = 'C:\Users\marco\.codex\dotnet-sdk-8\dotnet.exe',
    [string]$InnoPath = 'C:\Users\marco\AppData\Local\Programs\Inno Setup 6\ISCC.exe'
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $project '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $project '.nuget/packages'
$env:DOTNET_ROOT = Split-Path -Parent $DotnetPath
$caseDirectory = Join-Path $project ('artifacts/update-integration/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $caseDirectory -Force | Out-Null
$fixture = Join-Path $project 'tests/fixtures/UpdateApp/UpdateApp.csproj'
$innoScript = Join-Path $project 'tests/fixtures/UpdateFixture.iss'
foreach ($version in @('1.2.1', '1.3.0')) {
    $output = Join-Path $caseDirectory $version
    & $DotnetPath publish $fixture -c Release -r win-x64 --self-contained false -p:Version=$version -p:UseSharedCompilation=false -m:1 -o $output --configfile (Join-Path $project 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Fixture publish failed.' }
    $name = if ($version -eq '1.2.1') { 'Baseline-1.2.1' } else { 'TechHub-User-Setup-1.3.0' }
    & $InnoPath "/DFixtureVersion=$version" "/DFixtureDirectory=$(Join-Path $caseDirectory 'Install')" "/DSourceDir=$output" "/DOutputDir=$caseDirectory" "/DOutputName=$name" $innoScript
    if ($LASTEXITCODE -ne 0) { throw 'Fixture installer compile failed.' }
}
$harness = Join-Path $project 'tests/TechHub.UpdateIntegration/TechHub.UpdateIntegration.csproj'
& $DotnetPath restore $harness --configfile (Join-Path $project 'NuGet.Config') -m:1
if ($LASTEXITCODE -ne 0) { throw 'Integration restore failed.' }
& $DotnetPath run --project $harness -c Release --no-restore -p:UseSharedCompilation=false -- $caseDirectory (Join-Path $caseDirectory 'Baseline-1.2.1.exe')
if ($LASTEXITCODE -ne 0) { throw "Integration failed; preserve evidence in $caseDirectory" }
Write-Output "Evidence: $caseDirectory"
