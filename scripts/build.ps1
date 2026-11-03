param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$NanoCadManagedDir = 'C:\Program Files\Nanosoft AS\nanoCAD x64 26.0\bin',
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sdkExecutable = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdkExecutable)) { throw 'Install .NET SDK 10.0.400 in .tools/dotnet or run scripts/bootstrap.ps1.' }
foreach ($reference in @('hostmgd.dll','hostdbmgd.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $NanoCadManagedDir $reference))) { throw "Missing nanoCAD reference: $reference in $NanoCadManagedDir" }
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $projectRoot
try {
    $restoreArgs = @()
    if ($NoRestore) { $restoreArgs = @('--no-restore') }
    & $sdkExecutable build NanoCAD.Mcp.sln -c $Configuration "-p:NanoCadManagedDir=$NanoCadManagedDir" --nologo @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    & (Join-Path $PSScriptRoot 'build-visual-worker-pilot.ps1')
    $visualWorker = Join-Path $projectRoot 'artifacts\visual-observation-pilot\bin\VisualWorker.exe'
    Copy-Item -LiteralPath $visualWorker -Destination (Join-Path $projectRoot "src\NanoCAD.McpServer\bin\$Configuration\net10.0-windows\NanoCAD.VisualWorker.exe")
    if ($Configuration -eq 'Release') {
        & $sdkExecutable publish src/NanoCAD.McpServer/NanoCAD.McpServer.csproj -c Release --no-restore -o artifacts/server --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Server publish failed.' }
        Copy-Item -LiteralPath $visualWorker -Destination (Join-Path $projectRoot 'artifacts\server\NanoCAD.VisualWorker.exe')
        & $sdkExecutable build src/NanoCADBridge/NanoCADBridge.csproj -c Release --no-restore "-p:NanoCadManagedDir=$NanoCadManagedDir" -o artifacts/bridge --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Bridge package failed.' }
        & $sdkExecutable build tests/NanoCADBridge.Probe/NanoCADBridge.Probe.csproj -c Release --no-restore "-p:NanoCadManagedDir=$NanoCadManagedDir" -o artifacts/probe --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
        $hostCopies = Get-ChildItem -LiteralPath artifacts/bridge,artifacts/probe -Filter 'host*mgd.dll'
        if ($hostCopies) { throw 'Host assemblies must not be distributed alongside the plugin.' }
    }
    $manifest = [ordered]@{
        utc = (Get-Date).ToUniversalTime().ToString('o')
        configuration = $Configuration
        dotnet_sdk = (& $sdkExecutable --version)
        reference_directory = $NanoCadManagedDir
        references = @('hostmgd.dll','hostdbmgd.dll') | ForEach-Object {
            $item = Get-Item -LiteralPath (Join-Path $NanoCadManagedDir $_)
            [ordered]@{ name = $_; file_version = $item.VersionInfo.FileVersion; sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash }
        }
        cad_runtime_test = 'pending; do not infer NETLOAD success from compilation'
    }
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "artifacts/build-$Configuration.json" -Encoding utf8
}
finally { Pop-Location }
