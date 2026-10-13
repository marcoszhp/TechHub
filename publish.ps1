param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputPath = 'artifacts\portable',
    [switch]$SkipRestore,
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw 'O SDK do .NET 8 não foi encontrado no PATH.'
}

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$publishDirectory = Join-Path $projectRoot $OutputPath
$artifactRoot = Split-Path -Parent $publishDirectory
$cliDirectory = Join-Path $artifactRoot 'cli'
$agentDirectory = Join-Path $artifactRoot 'agent'

New-Item -ItemType Directory -Force -Path $publishDirectory, $cliDirectory, $agentDirectory | Out-Null

if (-not $SkipRestore) {
    & $dotnetCommand.Source restore (Join-Path $projectRoot 'TechHub.sln') --runtime win-x64 --configfile (Join-Path $projectRoot 'NuGet.Config') -p:NuGetAudit=false -p:PublishReadyToRun=true -m:1
    if ($LASTEXITCODE -ne 0) { throw "Restore falhou com código $LASTEXITCODE." }
}

foreach ($target in @(
    @{ Project = 'src\TechHub.App\TechHub.App.csproj'; Directory = $publishDirectory },
    @{ Project = 'src\TechHub.Cli\TechHub.Cli.csproj'; Directory = $cliDirectory },
    @{ Project = 'src\TechHub.Agent\TechHub.Agent.csproj'; Directory = $agentDirectory }
)) {
    & $dotnetCommand.Source publish (Join-Path $projectRoot $target.Project) `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        -m:1 `
        -p:UseSharedCompilation=false `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=true `
        --output $target.Directory
    if ($LASTEXITCODE -ne 0) { throw "Publish falhou para $($target.Project) com código $LASTEXITCODE." }
}

Set-Content -Path (Join-Path $publishDirectory 'portable.flag') -Value '' -NoNewline
Set-Content -Path (Join-Path $cliDirectory 'portable.flag') -Value '' -NoNewline
Write-Host "Aplicativo portátil criado em: $publishDirectory"
Write-Host "CLI criada em: $cliDirectory"
Write-Host "Agente criado em: $agentDirectory"

if ($Installer) {
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -eq $iscc) {
        Write-Warning 'Inno Setup não foi encontrado. Instale-o e execute novamente com -Installer.'
    }
    else {
        $installerDirectory = Join-Path $artifactRoot 'installer'
        New-Item -ItemType Directory -Force -Path $installerDirectory | Out-Null
        & $iscc.Source "/DSourceDir=$publishDirectory" "/DOutputDir=$installerDirectory" (Join-Path $projectRoot 'installer\TechHub.iss')
        if ($LASTEXITCODE -ne 0) { throw "Installer falhou com código $LASTEXITCODE." }
        Write-Host "Instalador criado em: $installerDirectory"
    }
}
