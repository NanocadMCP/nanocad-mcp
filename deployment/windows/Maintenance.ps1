function Register-Uninstaller {
    $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $script=Join-Path $productRoot 'Maintenance\Setup.ps1'
    $command='"'+$powershell+'" -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$script+'" -Mode UninstallGui'
    New-Item -Path $RegistrationKey -Force|Out-Null
    foreach($pair in @{
        DisplayName='nanoCAD MCP';DisplayVersion=[string]$version;InstallLocation=$productRoot;
        UninstallString=$command;DisplayIcon=$server
    }.GetEnumerator()) {New-ItemProperty -LiteralPath $RegistrationKey -Name $pair.Key -Value $pair.Value -PropertyType String -Force|Out-Null}
    foreach($name in @('NoModify','NoRepair')){New-ItemProperty -LiteralPath $RegistrationKey -Name $name -Value 1 -PropertyType DWord -Force|Out-Null}
}

function Get-OwnedPaths($State) {
    if(!$State.PSObject.Properties['owned_files']){throw 'Run the latest installer once before uninstalling.'}
    $root=[IO.Path]::GetFullPath($productRoot).TrimEnd('\')
    # Never follow junctions or accept a path outside this user's installed product.
    foreach($file in $State.owned_files){
        $path=[IO.Path]::GetFullPath([string]$file)
        if(!$path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Uninstall path validation failed.'}
        $cursor=$path
        while($cursor){
            if(Test-Path -LiteralPath $cursor){
                if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Uninstall path validation failed.'}
            }
            $cursor=[IO.Path]::GetDirectoryName($cursor)
        }
        $path
    }
}

function Assert-UninstallFiles($State) {
    foreach($path in @(Get-OwnedPaths $State)){
        if(Test-Path -LiteralPath $path){
            try {$stream=[IO.File]::Open($path,'Open','ReadWrite','None');$stream.Dispose()}
            catch {throw 'Close nanoCAD and your AI assistants before uninstalling. Save your drawings first, then try again.'}
        }
    }
}

function Remove-InstalledFiles($State) {
    $files=@(Get-OwnedPaths $State)
    # Exact owned files only. No recursive deletion; user-added files are preserved.
    foreach($path in $files){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}
    $directories=@($files|ForEach-Object {
        $directory=[IO.Path]::GetDirectoryName($_)
        while($directory.Length -gt $productRoot.Length){$directory;$directory=[IO.Path]::GetDirectoryName($directory)}
    }|Sort-Object -Unique|Sort-Object Length -Descending)
    foreach($directory in $directories){
        if((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0){Remove-Item -LiteralPath $directory}
    }
    if(Test-Path -LiteralPath $RegistrationKey){Remove-Item -LiteralPath $RegistrationKey}
    Remove-Item -LiteralPath $statePath -Force
    if(@(Get-ChildItem -LiteralPath $productRoot -Force).Count -eq 0){Remove-Item -LiteralPath $productRoot}
}

function Show-Uninstaller {
    Add-Type -AssemblyName System.Windows.Forms
    $answer=[Windows.Forms.MessageBox]::Show('Remove nanoCAD MCP? Save your drawings and close nanoCAD, Codex and Claude Desktop before continuing. Your drawings will be kept.','Uninstall nanoCAD MCP','OKCancel','Question')
    if($answer -ne 'OK'){return}
    try {$message=Remove-LocalPackage;[void][Windows.Forms.MessageBox]::Show($message,'nanoCAD MCP')}
    catch {[void][Windows.Forms.MessageBox]::Show((Get-SetupErrorText $_),'Uninstall could not finish')}
}
