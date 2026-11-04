$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This launcher requires a disposable GitHub-hosted Windows runner.'
}

$workspace = (Resolve-Path -LiteralPath $env:GITHUB_WORKSPACE).Path
$assembly = (Resolve-Path -LiteralPath (Join-Path $workspace 'tests/NanoCAD.Tests/bin/Release/net10.0-windows/NanoCAD.Tests.dll')).Path
$dotnet = (Resolve-Path -LiteralPath (Join-Path $env:DOTNET_ROOT 'dotnet.exe')).Path
Push-Location -LiteralPath $workspace
try {
    $resolvedSdk = & $dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'The setup-dotnet host could not resolve the SDK required by global.json.' }
} finally {
    Pop-Location
}
Write-Host "Managed tests use .NET SDK $resolvedSdk from $dotnet."
$artifacts = (New-Item -ItemType Directory -Path (Join-Path $workspace 'artifacts') -Force).FullName
$stdout = Join-Path $artifacts 'managed-tests.stdout.log'
$stderr = Join-Path $artifacts 'managed-tests.stderr.log'

function Grant-TestAccess($directory, $sid, $rights) {
    $acl = Get-Acl -LiteralPath $directory
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $sid, $rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $directory -AclObject $acl
    return $rule
}

function Remove-TestAccess($directory, $rule) {
    if ($null -eq $rule) { return }
    $acl = Get-Acl -LiteralPath $directory
    $acl.RemoveAccessRuleSpecific($rule)
    Set-Acl -LiteralPath $directory -AclObject $acl
}

# Hosted administrators can have a group as the token owner. Exercise the real
# same-user pipe checks with a standard account and its own user token.
$name = 'NanoCadCi_' + [guid]::NewGuid().ToString('N').Substring(0, 10)
$password = ConvertTo-SecureString ([guid]::NewGuid().ToString('N') + '!aA9') -AsPlainText -Force
$account = $null
$workspaceRule = $null
$artifactRule = $null
try {
    $account = New-LocalUser -Name $name -Password $password
    $users = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')
    if (-not (Get-LocalGroupMember -SID $users | Where-Object { $_.SID -eq $account.SID })) {
        Add-LocalGroupMember -SID $users -Member "$env:COMPUTERNAME\$name"
    }
    $workspaceRule = Grant-TestAccess $workspace $account.SID 'ReadAndExecute'
    $artifactRule = Grant-TestAccess $artifacts $account.SID 'Modify'
    $start = @{
        FilePath = $dotnet
        Credential = [pscredential]::new("$env:COMPUTERNAME\$name", $password)
        LoadUserProfile = $true
        UseNewEnvironment = $true
        ArgumentList = @(
            ('"{0}"' -f $assembly), '--suite', 'managed',
            '--junit', 'artifacts/managed-tests.xml', '--json', 'artifacts/managed-tests.json',
            '--require-standard-windows-user', $account.SID.Value
        )
        WorkingDirectory = $workspace
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
        WindowStyle = 'Hidden'
        PassThru = $true
        Wait = $true
    }
    $process = Start-Process @start
    Get-Content -LiteralPath $stdout
    Get-Content -LiteralPath $stderr
    if ($process.ExitCode -ne 0) {
        throw "Managed tests exited with code $($process.ExitCode)."
    }
} finally {
    try {
        try { Remove-TestAccess $artifacts $artifactRule }
        finally { Remove-TestAccess $workspace $workspaceRule }
    } finally {
        try { if ($null -ne $account) { Remove-LocalUser -SID $account.SID } }
        finally { $password.Dispose() }
    }
}
