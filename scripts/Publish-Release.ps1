param(
    [Parameter(Mandatory)][string]$AssetDirectory,
    [string]$Tag = 'v1.1.0',
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$repository = 'marcoszhp/TechHub'
$api = "https://api.github.com/repos/$repository"
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'
$credentialMap = @{}
$headers = @{}
try {
    $credentialLines = "protocol=https`nhost=github.com`n`n" | git credential fill
    if ($LASTEXITCODE -ne 0) { throw 'Credencial GitHub indisponível.' }
    foreach ($line in $credentialLines) {
        $parts = $line -split '=', 2
        if ($parts.Count -eq 2) { $credentialMap[$parts[0]] = $parts[1] }
    }
    $headers = @{ Authorization="Bearer $($credentialMap['password'])"; Accept='application/vnd.github+json'; 'User-Agent'='TechHub-release' }
    $releases = @(Invoke-RestMethod -Uri "$api/releases" -Headers $headers)
    $release = $releases | Where-Object tag_name -eq $Tag | Select-Object -First 1
    if ($Publish) {
        if (!$release -or !$release.draft) { throw 'É necessário um rascunho existente para publicar.' }
        $names = @($release.assets | ForEach-Object name)
        foreach ($expected in @('TechHub-Setup-1.1.0.exe','TechHub-Portable-1.1.0-win-x64.zip','SHA256SUMS.txt')) {
            if ($expected -notin $names) { throw "Pacote ausente: $expected" }
        }
        $release = Invoke-RestMethod -Uri "$api/releases/$($release.id)" -Headers $headers -Method Patch -ContentType 'application/json' -Body '{"draft":false}'
        Write-Output $release.html_url
        return
    }
    if ($release) { throw 'A versão já existe. Inspecione o rascunho antes de retentar uploads.' }
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Commit inválido.' }
    $assetNames = @('TechHub-Setup-1.1.0.exe','TechHub-Portable-1.1.0-win-x64.zip','SHA256SUMS.txt')
    foreach ($name in $assetNames) {
        if (!(Test-Path -LiteralPath (Join-Path $AssetDirectory $name) -PathType Leaf)) { throw "Arquivo ausente: $name" }
    }
    $notes = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\docs\RELEASE-1.1.md') -Raw
    $body = @{ tag_name=$Tag; target_commitish=$commit.Trim(); name='TechHub 1.1 — central de suporte e mapa de rede'; body=$notes; draft=$true; prerelease=$false } | ConvertTo-Json
    $release = Invoke-RestMethod -Uri "$api/releases" -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    $uploadBase = $release.upload_url -replace '\{.*$',''
    foreach ($name in $assetNames) {
        $assetPath = Join-Path $AssetDirectory $name
        $uploaded = Invoke-RestMethod -Uri ($uploadBase + '?name=' + [Uri]::EscapeDataString($name)) -Headers $headers -Method Post -ContentType 'application/octet-stream' -InFile $assetPath -TimeoutSec 600
        if ($uploaded.size -ne (Get-Item -LiteralPath $assetPath).Length) { throw "Tamanho divergente: $name" }
        Write-Output "Upload verificado: $($uploaded.name) ($($uploaded.size) bytes)"
    }
    Write-Output "Rascunho criado: release $($release.id). Publicação exige -Publish após revisão."
}
finally {
    $credentialMap.Clear()
    $headers.Clear()
    $credentialLines = $null
}
