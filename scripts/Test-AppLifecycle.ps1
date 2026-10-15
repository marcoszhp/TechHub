[CmdletBinding()]
param(
    [string]$DotnetPath = 'C:\Users\marco\.codex\dotnet-sdk-8\dotnet.exe',
    [string]$ExpectedVersion = '1.3.0'
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$fixtureProject = Join-Path $repository 'tests/TechHub.LifecycleFixture/TechHub.LifecycleFixture.csproj'
$lifecycleRoot = Join-Path $repository 'artifacts/lifecycle'
$runDirectory = Join-Path $lifecycleRoot ([guid]::NewGuid().ToString('N'))
$buildDirectory = Join-Path $runDirectory 'build'
$results = [Collections.Generic.List[object]]::new()

function Assert-NoReparseAncestor([string]$Path) {
    $directory = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Path))
    while ($null -ne $directory) {
        if ($directory.Exists -and ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Fixture path traverses a reparse point: $($directory.FullName)"
        }
        $directory = $directory.Parent
    }
}

function Save-Results {
    ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5 |
        Set-Content -LiteralPath (Join-Path $runDirectory 'process-results.json') -Encoding UTF8
}

if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'ExpectedVersion must be X.Y.Z.' }
$DotnetPath = (Resolve-Path -LiteralPath $DotnetPath).Path
Assert-NoReparseAncestor $runDirectory
New-Item -ItemType Directory -Path $runDirectory | Out-Null
Write-Output "Lifecycle evidence: $runDirectory"

$savedEnvironment = @{}
foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_ROOT')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:DOTNET_CLI_HOME = Join-Path $repository '.dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $repository '.nuget/packages'
    $env:DOTNET_ROOT = Split-Path -Parent $DotnetPath

    # The coordinator must serialize this build and prepare restore assets beforehand.
    # Never publish this project or copy this output into a release/installation.
    & $DotnetPath build $fixtureProject -c Release --no-restore -m:1 -p:UseSharedCompilation=false -o $buildDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Lifecycle fixture build failed. Restore assets must already exist; no restore/network is attempted by this script.' }

    foreach ($mode in @('normal-close', 'update-shutdown')) {
        $caseDirectory = Join-Path $runDirectory $mode
        New-Item -ItemType Directory -Path $caseDirectory | Out-Null
        Get-ChildItem -LiteralPath $buildDirectory -Force | Copy-Item -Destination $caseDirectory -Recurse
        $executable = Join-Path $caseDirectory 'TechHub.LifecycleFixture.exe'
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Fixture executable missing: $executable" }
        Assert-NoReparseAncestor $caseDirectory
        Write-Output "Executing: $executable ($mode)"
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $executable -ArgumentList @($mode, ('"{0}"' -f $repository), $ExpectedVersion) `
            -WorkingDirectory $caseDirectory -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $caseDirectory 'stdout.txt') `
            -RedirectStandardError (Join-Path $caseDirectory 'stderr.txt')
        try {
            $remaining = [Math]::Max(0, 25000 - [int]$watch.ElapsedMilliseconds)
            $exited = $process.WaitForExit($remaining)
            $elapsed = $watch.ElapsedMilliseconds
            $result = [ordered]@{
                Mode = $mode; Executable = $executable; ProcessId = $process.Id
                Exited = $exited; ElapsedMilliseconds = $elapsed; ExitCode = $null
                Passed = $false; Error = $null
            }
            if (-not $exited) {
                $result.Error = 'Timeout (25 seconds). Process left running; no kill was attempted. No next case launched.'
                $results.Add([pscustomobject]$result)
                Save-Results
                throw "Lifecycle timeout: PID $($process.Id), executable $executable. Evidence: $caseDirectory. Process was NOT killed."
            }
            $process.Refresh()
            $result.ExitCode = $process.ExitCode
            try {
                if ($elapsed -gt 25000) { throw 'Process exceeded the 25-second lifecycle budget.' }
                $evidence = Get-Content -LiteralPath (Join-Path $caseDirectory 'lifecycle-evidence.json') -Raw | ConvertFrom-Json
                if ($process.ExitCode -ne 0) { throw "Fixture exit code $($process.ExitCode): $($evidence.Error)" }
                if ($evidence.ProcessId -ne $process.Id -or $evidence.Mode -ne $mode -or
                    $evidence.BaseDirectory -ne $caseDirectory -or $evidence.Version -ne $ExpectedVersion -or
                    -not $evidence.RunStarted -or -not $evidence.Ready -or -not $evidence.ShutdownRequested -or
                    -not $evidence.RunReturned -or $evidence.RunEvidence -ne 'Run retornou' -or
                    $evidence.RunExitCode -ne 0 -or -not $evidence.DatabaseValidated -or $evidence.Error) {
                    throw 'Lifecycle evidence is missing or does not match this process, version and case.'
                }
                $database = Join-Path $caseDirectory 'Data/techhub.db'
                if ($evidence.DatabasePath -ne $database -or -not (Test-Path -LiteralPath $database -PathType Leaf) -or
                    (Get-Item -LiteralPath $database).Length -eq 0 -or
                    -not (Test-Path -LiteralPath (Join-Path $caseDirectory 'portable.flag') -PathType Leaf)) {
                    throw 'Isolated database/portable marker validation failed.'
                }
                $result.Passed = $true
            }
            catch { $result.Error = $_.Exception.Message }
            $results.Add([pscustomobject]$result)
            Save-Results
        }
        finally { $process.Dispose() }
    }
    if (@($results | Where-Object { -not $_.Passed }).Count -ne 0) {
        throw "Lifecycle failed. Inspect process-results.json and case evidence in $runDirectory"
    }
    Write-Output "PASS: both real-App processes exited within 25 seconds. Evidence: $runDirectory"
}
finally {
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
}
