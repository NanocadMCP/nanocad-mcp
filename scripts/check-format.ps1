param(
    [string]$BaseRevision = $env:FORMAT_BASE,
    [string]$Dotnet = 'dotnet',
    [switch]$Fix
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    git rev-parse --show-toplevel | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Formatting requires a Git checkout.' }
    function Read-GitPaths([string[]]$GitArguments) {
        $paths = @(git @GitArguments)
        if ($LASTEXITCODE -ne 0) { throw 'Could not determine changed files.' }
        return $paths
    }
    if ($BaseRevision -and $BaseRevision -notmatch '^0+$') {
        $changed = @(Read-GitPaths -GitArguments @('diff', '--name-only', "$BaseRevision...HEAD", '--', 'src', 'tests'))
    } else {
        $changed = @(Read-GitPaths -GitArguments @('diff', '--name-only', 'HEAD', '--', 'src', 'tests'))
        $changed += @(Read-GitPaths -GitArguments @('ls-files', '--others', '--exclude-standard', '--', 'src', 'tests'))
        # An initial import establishes the formatting baseline. Subsequent commits
        # check their diff when no pull-request or push base was supplied.
        $history = @(Read-GitPaths -GitArguments @('rev-list', '--parents', '-n', '1', 'HEAD'))
        $parents = $history[0] -split ' '
        if ($parents.Count -gt 1) { $changed += @(Read-GitPaths -GitArguments @('diff', '--name-only', $parents[1], 'HEAD', '--', 'src', 'tests')) }
    }
    $changed = @($changed | Where-Object { $_ -like '*.cs' -and (Test-Path -LiteralPath $_) } | Sort-Object -Unique)
    foreach ($group in ($changed | Group-Object { ($_ -split '/')[0..1] -join '/' })) {
        # Folder workspaces format text without loading SDK-dependent projects.
        $paths = @($group.Group | ForEach-Object { Join-Path $repoRoot $_ })
        $formatArgs = @('format', 'whitespace', $group.Name, '--folder', '--include') + $paths
        if (-not $Fix) { $formatArgs += '--verify-no-changes' }
        & $Dotnet @formatArgs
        if ($LASTEXITCODE -ne 0) { throw "Formatting failed for $($group.Name)." }
    }
    Write-Output "Formatting checked for $($changed.Count) changed C# files."
} finally { Pop-Location }
