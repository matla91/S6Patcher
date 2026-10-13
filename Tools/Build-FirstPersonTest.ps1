param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $commit = (& git -c "safe.directory=$repoRoot" rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Git commit.' }
    $dirty = & git -c "safe.directory=$repoRoot" status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Commit tracked changes before building a test delivery.' }
    $archiveRoot = Join-Path $repoRoot 'Source/Archive'
    $archiveCommit = (& git -c "safe.directory=$archiveRoot" -C $archiveRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $archiveCommit -ne 'ec1424232f56a7b5c44d5c5acb4a200925573c45') {
        throw 'Initialize Source/Archive at its pinned revision before building.'
    }
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Install the SDK selected by global.json.' }
    $output = Join-Path $repoRoot "artifacts/first-person-test/$commit"
    if (Test-Path -LiteralPath $output) { throw "Delivery already exists: $output" }
    New-Item -ItemType Directory -Path $output | Out-Null

    & dotnet build S6Patcher.csproj -c Debug -warnaserror -p:EnableNETAnalyzers=true
    if ($LASTEXITCODE -ne 0) { throw 'Debug build failed.' }
    & dotnet run --project Tests/S6Patcher.Tests.csproj -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'C# regression tests failed.' }
    & dotnet publish S6Patcher.csproj -c Release -r win-x64 -p:PublishTrimmed=false -o (Join-Path $output 'publish')
    if ($LASTEXITCODE -ne 0) { throw 'Windows test publish failed.' }
    $runtime = Get-Content 'bin/Release/net10.0/win-x64/S6Patcher.runtimeconfig.json' -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.configProperties.'System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault' -eq $false) {
        throw 'JSON reflection is disabled: this package cannot exercise HE session recovery.'
    }

    $package = Join-Path $output 'package'
    $resources = Join-Path $package 'Resources'
    New-Item -ItemType Directory -Path $resources -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $output 'publish/S6Patcher.exe') -Destination $package
    foreach ($name in @('UserScriptLocal.lua', 'UserScriptGlobal.lua', 'EMXBinData.s6patcher')) {
        Copy-Item -LiteralPath (Join-Path 'Resources/Scripts' $name) -Destination $resources
    }
    foreach ($name in @('FIRST_PERSON_REMOTE_START.md', 'FIRST_PERSON_WINDOWS_VALIDATION.md')) {
        Copy-Item -LiteralPath (Join-Path 'docs' $name) -Destination $package
    }
    $hashes = @('S6Patcher.exe', 'Resources/UserScriptLocal.lua', 'Resources/UserScriptGlobal.lua', 'Resources/EMXBinData.s6patcher') |
        ForEach-Object { [pscustomobject]@{ File = $_; SHA256 = (Get-FileHash -LiteralPath (Join-Path $package $_) -Algorithm SHA256).Hash } }
    [ordered]@{
        Repository = 'https://github.com/matla91/S6Patcher'
        Commit = $commit
        ArchiveRevision = $archiveCommit
        SDK = $sdk
        PublishTrimmed = $false
        EngineTested = $false
        Files = @($hashes)
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $package 'DELIVERY.json') -Encoding utf8
    Write-Host "Test delivery: $package"
} finally {
    Pop-Location
}
