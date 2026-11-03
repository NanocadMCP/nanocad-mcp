$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsDir = Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
$sdkVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$metadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$release = $metadata.releases | ForEach-Object { $_.sdks } | Where-Object version -eq $sdkVersion | Select-Object -First 1
if (-not $release) { throw "Official release metadata does not contain SDK $sdkVersion." }
$archive = $release.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name.EndsWith('.zip') } | Select-Object -First 1
$zipPath = Join-Path $toolsDir 'dotnet-sdk.zip'
Invoke-WebRequest -Uri $archive.url -OutFile $zipPath
if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA512).Hash -ne $archive.hash) { throw 'Official SDK SHA-512 verification failed.' }
Expand-Archive -LiteralPath $zipPath -DestinationPath (Join-Path $toolsDir 'dotnet') -Force
& (Join-Path $toolsDir 'dotnet\dotnet.exe') --info
