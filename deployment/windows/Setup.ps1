param([ValidateSet('Gui','Install','Remove','UninstallGui','Plan')][string]$Mode='Gui',[string]$NanoCadPath,[string[]]$Clients=@('Codex'),[switch]$NoElevation,[string]$RegistrationKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NanoCADMCP')
. (Join-Path $PSScriptRoot 'Install.Common.ps1')
$productRoot=Join-Path $env:LOCALAPPDATA 'Programs\NanoCADMCP'
$statePath=Join-Path $productRoot 'installation.json'
$utf8=New-Object Text.UTF8Encoding($false)
. (Join-Path $PSScriptRoot 'Maintenance.ps1')

function Register-OwnedBridge([string]$Ini,[string]$Bridge,[bool]$Remove) {
    try {Set-BridgeRegistration $Ini $Bridge $Remove;return}
    catch [UnauthorizedAccessException] {if($NoElevation){throw}}
    if($Ini -match '["\r\n]' -or $Bridge -match '["\r\n]' -or $PSScriptRoot -match '["\r\n]'){throw 'Invalid registration path.'}
    $action='Add';if($Remove){$action='Remove'}
    $helper=Join-Path $PSScriptRoot 'Register-Bridge.ps1'
    $arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$helper+'" -IniPath "'+$Ini+'" -BridgePath "'+$Bridge+'" -Mode '+$action
    # Elevate only the INI helper; client configuration stays in the initiating user's profile.
    $proc=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if($proc.ExitCode -ne 0){throw 'nanoCAD registration was not completed. No client configuration was changed.'}
}
function Install-LocalPackage {
    $manifest=Assert-Package $PSScriptRoot
    if(![Environment]::Is64BitOperatingSystem){throw 'Windows x64 required.'}
    if(!$NanoCadPath){$found=@(Find-NanoCad);if($found.Count -ne 1){throw 'Choose the nanoCAD26 installation folder.'};$script:NanoCadPath=$found[0]}
    [void](Assert-NanoCadCompatibility $NanoCadPath)
    $ini=Join-Path $NanoCadPath 'nCad.ini'
    foreach($client in $Clients){if($client -notin @('Codex','ClaudeDesktop')){throw ('Unsupported client: '+$client)}}
    if(!$Clients.Count){throw 'Choose at least one client.'}
    $version=[string]$manifest.package_build
    if($version -notmatch '^\d+$'){throw 'Invalid release ID.'}
    $target=Join-Path $productRoot ('versions\'+$version)
    $bridge=Join-Path $target 'bridge\NanoCADBridge.dll';$server=Join-Path $target 'server\NanoCAD.McpServer.exe'
    $old=$null;if(Test-Path -LiteralPath $statePath){$old=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json}
    if($old -and $old.ini_path -ine $ini){throw 'An installation for another nanoCAD exists. Remove it first to change the CAD target.'}
    # Validate all client documents before changing either CAD registration or configs.
    $changes=New-Object 'Collections.Generic.List[object]'
    foreach($client in $Clients){
        $path=Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
        if($client -eq 'Codex'){$codexRoot=Join-Path $env:USERPROFILE '.codex';if($env:CODEX_HOME){$codexRoot=$env:CODEX_HOME};$path=Join-Path $codexRoot 'config.toml'}
        $before=Read-PreservedText $path;$prior=$null
        if($client -eq 'Codex'){
            $prior=Get-CodexEntry $before.Text;$entry=New-CodexEntry $server
            $text=Set-CodexEntry $before.Text $entry
        }else{
            if($before.Text.Trim()){$config=$before.Text|ConvertFrom-Json;if($config.PSObject.Properties['mcpServers'] -and $config.mcpServers.PSObject.Properties['nanocad']){$prior=$config.mcpServers.nanocad}}
            $entry=[pscustomobject]@{command=$server;args=@()};$text=Set-ClaudeEntry $before.Text $entry
        }
        $baseline=$prior
        if($old){$previous=@($old.clients|Where-Object name -eq $client);if($previous.Count){$baseline=$previous[0].previous_entry}}
        [void]$changes.Add([pscustomobject]@{name=$client;path=$path;before=$before;after=$text;installed_entry=$entry;previous_entry=$baseline})
    }
    if($old){foreach($entry in $old.clients){if($entry.name -notin $Clients){throw 'For an update, include all previously connected clients, or remove the old installation first.'}}}
    if(Test-Path -LiteralPath $target){
        foreach($file in $manifest.files|Where-Object {$_.path -match '^(server|bridge)/'}){
            if((Get-FileHash -LiteralPath (Join-Path $target $file.path) -Algorithm SHA256).Hash -ne $file.sha256){throw 'Existing version differs; it will not be overwritten.'}
        }
    }else{
        $staging=$target+'.staging-'+[guid]::NewGuid().ToString('N')
        [IO.Directory]::CreateDirectory($staging)|Out-Null
        foreach($folder in @('server','bridge')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) -Destination $staging -Recurse}
        foreach($file in $manifest.files|Where-Object {$_.path -match '^(server|bridge)/'}){
            if((Get-FileHash -LiteralPath (Join-Path $staging $file.path) -Algorithm SHA256).Hash -ne $file.sha256){throw 'Staged installation differs from package.'}
        }
        [IO.Directory]::Move($staging,$target)
    }
    $undo=New-Object 'Collections.Generic.List[object]';$registered=$false
    try{
        Register-OwnedBridge $ini $bridge $false;$registered=$true
        foreach($change in $changes){
            if(Test-Path -LiteralPath $change.path){[IO.File]::Copy($change.path,$change.path+'.nanocad-mcp-'+[guid]::NewGuid().ToString('N')+'.bak')}
            Write-AtomicText $change.path $change.after $change.before.Encoding;[void]$undo.Add($change)
        }
        if($old -and $old.bridge_path -ine $bridge){Register-OwnedBridge $ini $old.bridge_path $true}
        $state=[ordered]@{package_build=$version;ini_path=$ini;bridge_path=$bridge;server_path=$server;installed_at_utc=[DateTime]::UtcNow.ToString('o');clients=@($changes|Select-Object name,path,previous_entry,installed_entry)}
        $maintenance=Join-Path $productRoot 'Maintenance'
        [IO.Directory]::CreateDirectory($maintenance)|Out-Null
        foreach($name in @('Setup.ps1','Install.Common.ps1','Maintenance.ps1','Register-Bridge.ps1')){
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $maintenance $name) -Force
        }
        $owned=@($manifest.files|Where-Object {$_.path -match '^(server|bridge)/'}|ForEach-Object {Join-Path $target $_.path})
        $owned+=@(Get-ChildItem -LiteralPath $maintenance -File|Select-Object -ExpandProperty FullName)
        if($old -and $old.PSObject.Properties['owned_files']){$owned+=@($old.owned_files)}
        $state.owned_files=@($owned|Sort-Object -Unique)
        $state.disconnected=$false
        Register-Uninstaller
        Write-AtomicText $statePath ($state|ConvertTo-Json -Depth 60) $utf8
    }catch{
        foreach($change in $undo){Write-AtomicText $change.path $change.before.Text $change.before.Encoding}
        if($registered -and (!$old -or $old.bridge_path -ine $bridge)){Register-OwnedBridge $ini $bridge $true}
        if($old){Register-OwnedBridge $ini $old.bridge_path $false}
        throw
    }
    $apps=(@($Clients|ForEach-Object {if($_ -eq 'ClaudeDesktop'){'Claude Desktop'}else{'Codex'}}) -join ' and ')
    return "Installation complete. You can close this window.`r`n`r`n1. Close and reopen $apps.`r`n2. Open nanoCAD and a drawing.`r`n3. Ask your assistant to show the drawing.`r`n`r`nIf nanoCAD is already open, save your work and reopen it first."
}
function Remove-LocalPackage {
    if(!(Test-Path -LiteralPath $statePath)){return 'No managed installation found.'}
    $state=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
    Assert-UninstallFiles $state
    if(!$state.PSObject.Properties['disconnected'] -or !$state.disconnected){
    foreach($client in $state.clients){
        $current=Read-PreservedText $client.path
        if($client.name -eq 'Codex'){
            if((Get-CodexEntry $current.Text).Trim() -cne ([string]$client.installed_entry).Trim()){throw 'Codex nanoCAD settings changed after installation; preserved. Resolve before removal.'}
        }else{
            $config=$current.Text|ConvertFrom-Json
            if(!$config.PSObject.Properties['mcpServers'] -or !$config.mcpServers.PSObject.Properties['nanocad'] -or ($config.mcpServers.nanocad|ConvertTo-Json -Depth 50 -Compress) -cne ($client.installed_entry|ConvertTo-Json -Depth 50 -Compress)){throw 'Claude nanoCAD settings changed after installation; preserved. Resolve before removal.'}
        }
    }
    $restore=New-Object 'Collections.Generic.List[object]'
    foreach($client in $state.clients){
        $current=Read-PreservedText $client.path
        if($client.name -eq 'Codex'){$text=Set-CodexEntry $current.Text ([string]$client.previous_entry)}else{$text=Set-ClaudeEntry $current.Text $client.previous_entry}
        [void]$restore.Add([pscustomobject]@{path=$client.path;before=$current;after=$text})
    }
    $changed=New-Object 'Collections.Generic.List[object]';$detached=$false
    try {
        Register-OwnedBridge $state.ini_path $state.bridge_path $true;$detached=$true
        foreach($entry in $restore){
            Write-AtomicText $entry.path $entry.after $entry.before.Encoding
            [void]$changed.Add($entry)
        }
        $state|Add-Member -NotePropertyName disconnected -NotePropertyValue $true -Force
        Write-AtomicText $statePath ($state|ConvertTo-Json -Depth 60) $utf8
    } catch {
        foreach($entry in $changed){Write-AtomicText $entry.path $entry.before.Text $entry.before.Encoding}
        if($detached){Register-OwnedBridge $state.ini_path $state.bridge_path $false}
        throw
    }
    }
    Remove-InstalledFiles $state
    return 'nanoCAD MCP has been removed. Your drawings and other applications have not been deleted.' 
}
function Show-Setup {
    Add-Type -AssemblyName System.Windows.Forms
    $form=New-Object Windows.Forms.Form;$form.Text='nanoCAD MCP — Setup';$form.Width=590;$form.Height=315;$form.StartPosition='CenterScreen';$form.FormBorderStyle='FixedDialog';$form.MaximizeBox=$false
    $label=New-Object Windows.Forms.Label;$label.Text='Where is nanoCAD 26 installed?';$label.SetBounds(18,20,540,24)
    $path=New-Object Windows.Forms.TextBox;$path.SetBounds(18,48,445,25);$found=@(Find-NanoCad);if($found.Count -eq 1){$path.Text=$found[0]}
    $browse=New-Object Windows.Forms.Button;$browse.Text='Browse';$browse.SetBounds(470,47,85,27);$browse.Add_Click({$dialog=New-Object Windows.Forms.FolderBrowserDialog;if($dialog.ShowDialog() -eq 'OK'){$path.Text=$dialog.SelectedPath}})
    $codex=New-Object Windows.Forms.CheckBox;$codex.Text='Codex';$codex.Checked=$true;$codex.SetBounds(18,90,170,24)
    $claude=New-Object Windows.Forms.CheckBox;$claude.Text='Claude Desktop';$claude.SetBounds(200,90,200,24)
    $info=New-Object Windows.Forms.Label;$info.Text='Connect your assistant to nanoCAD to create, review and edit drawings. Choose the assistant you use above. Save your work and close nanoCAD before clicking Install.';$info.SetBounds(18,125,535,60)
    $install=New-Object Windows.Forms.Button;$install.Text='Install';$install.SetBounds(18,200,170,36)
    $close=New-Object Windows.Forms.Button;$close.Text='Close';$close.SetBounds(380,200,170,36);$close.Add_Click({$form.Close()})
    $install.Add_Click({
        try {
            $script:NanoCadPath=$path.Text;$script:Clients=@()
            if($codex.Checked){$script:Clients+='Codex'}
            if($claude.Checked){$script:Clients+='ClaudeDesktop'}
            $form.Enabled=$false
            $message=Install-LocalPackage
            foreach($control in @($label,$path,$browse,$codex,$claude,$install)){$control.Visible=$false}
            $info.SetBounds(22,22,530,166)
            $info.Text=$message
            $close.Text='Finish'
            $form.AcceptButton=$close
        } catch {
            [Windows.Forms.MessageBox]::Show((Get-SetupErrorText $_),'Installation incomplete')|Out-Null
        } finally {$form.Enabled=$true}
    })
    $form.AcceptButton=$install;$form.CancelButton=$close
    $form.Controls.AddRange(@($label,$path,$browse,$codex,$claude,$info,$install,$close));[void]$form.ShowDialog()
}
try{
    switch($Mode){
        'Gui'{Show-Setup}
        'Install'{Install-LocalPackage}
        'Remove'{Remove-LocalPackage}
        'UninstallGui'{Show-Uninstaller}
        'Plan'{$manifest=Assert-Package $PSScriptRoot;$candidates=@(Find-NanoCad);if($NanoCadPath){$candidates=@($NanoCadPath)};[pscustomobject]@{package=$manifest.package_build;cad_candidates=$candidates;cad_compatibility=@($candidates|ForEach-Object {Get-NanoCadCompatibility $_});install_root=$productRoot;clients=$Clients;changes_applied=$false}|ConvertTo-Json -Depth 7}
    }
}catch{Write-Error $_;exit 1}
