Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

function Read-PreservedText([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return @{Text=''; Encoding=(New-Object Text.UTF8Encoding($false))} }
    $bytes=[IO.File]::ReadAllBytes($Path)
    $encoding=New-Object Text.UTF8Encoding($false,$true); $skip=0
    if($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191){$encoding=New-Object Text.UTF8Encoding($true);$skip=3}
    elseif($bytes.Length -ge 2 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254){$encoding=[Text.Encoding]::Unicode;$skip=2}
    elseif($bytes.Length -ge 2 -and $bytes[0] -eq 254 -and $bytes[1] -eq 255){$encoding=[Text.Encoding]::BigEndianUnicode;$skip=2}
    try {$text=$encoding.GetString($bytes,$skip,$bytes.Length-$skip)}
    catch [Text.DecoderFallbackException] {$encoding=[Text.Encoding]::Default;$text=$encoding.GetString($bytes)}
    return @{Text=$text;Encoding=$encoding}
}
function Write-AtomicText([string]$Path,[string]$Text,[Text.Encoding]$Encoding) {
    $parent=Split-Path -Parent $Path
    [IO.Directory]::CreateDirectory($parent)|Out-Null
    $temp=Join-Path $parent ('.nanocad-mcp-'+[guid]::NewGuid().ToString('N')+'.tmp')
    try {
        [IO.File]::WriteAllText($temp,$Text,$Encoding)
        if([IO.File]::Exists($Path)){[IO.File]::Replace($temp,$Path,[NullString]::Value)}else{[IO.File]::Move($temp,$Path)}
    } finally {if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Edit-NetModules([string]$Text,[string]$BridgePath,[bool]$Remove) {
    if($BridgePath -match '[\r\n\[\]"]' -or ![IO.Path]::IsPathRooted($BridgePath)){throw 'Invalid bridge path.'}
    $newline="`r`n";if($Text.Contains("`n") -and !$Text.Contains("`r`n")){$newline="`n"}
    $lines=New-Object 'Collections.Generic.List[string]'
    foreach($line in [regex]::Split($Text,'\r\n|\n|\r')){[void]$lines.Add($line)}
    $inside=$false;$insert=-1;$sectionCount=0
    for($i=0;$i -lt $lines.Count;$i++){
        if($lines[$i] -match '^\s*\[([^\]]+)\]\s*$'){
            $inside=$Matches[1] -ieq '\NetModules'
            if($inside){$sectionCount++;if($insert -lt 0){$insert=$i+1}}
        } elseif($inside -and $lines[$i].Trim() -ieq $BridgePath){
            if(!$Remove){return $Text}
            $lines.RemoveAt($i);$i--
        }
    }
    if(!$Remove){
        if($sectionCount -gt 1){throw 'Multiple NetModules sections: preserve configuration for manual diagnosis.'}
        if($insert -ge 0){$lines.Insert($insert,$BridgePath)}
        else{[void]$lines.Add('[\NetModules]');[void]$lines.Add($BridgePath)}
    }
    return [string]::Join($newline,$lines)
}
function Set-BridgeRegistration([string]$IniPath,[string]$BridgePath,[bool]$Remove) {
    if(!(Test-Path -LiteralPath $IniPath -PathType Leaf) -or [IO.Path]::GetFileName($IniPath) -ine 'ncad.ini'){throw 'Existing nCad.ini required.'}
    if(!$Remove){[void](Assert-NanoCadCompatibility (Split-Path -Parent ([IO.Path]::GetFullPath($IniPath))))}
    if(!$Remove -and !(Test-Path -LiteralPath $BridgePath -PathType Leaf)){throw 'Bridge DLL missing.'}
    $current=Read-PreservedText $IniPath
    $changed=Edit-NetModules $current.Text $BridgePath $Remove
    if($changed -ceq $current.Text){return}
    $backup=$IniPath+'.nanocad-mcp-'+[guid]::NewGuid().ToString('N')+'.bak'
    [IO.File]::Copy($IniPath,$backup,$false)
    Write-AtomicText $IniPath $changed $current.Encoding
}
function Get-CodexEntry([string]$Text) {
    $pattern='(?ms)^\[mcp_servers\.nanocad\][ \t]*(?:#[^\r\n]*)?(?:\r?\n|\z).*?(?=^\[|\z)'
    $found=[regex]::Matches($Text,$pattern)
    if($found.Count -gt 1){throw 'Duplicate Codex nanoCAD entries.'}
    if($found.Count -eq 1){return $found[0].Value};return ''
}
function Set-CodexEntry([string]$Text,[string]$Entry) {
    # Nested policy/env sections are preserved: only this exact table changes.
    $old=Get-CodexEntry $Text
    if($old){$at=$Text.IndexOf($old,[StringComparison]::Ordinal);return $Text.Substring(0,$at)+$Entry+$Text.Substring($at+$old.Length)}
    if(!$Entry){return $Text};return $Text.TrimEnd()+"`r`n`r`n"+$Entry
}
function New-CodexEntry([string]$Server) {
    if($Server -match "['\r\n]"){throw 'Path cannot be represented in a TOML literal.'}
    return "[mcp_servers.nanocad]`r`ncommand = '$Server'`r`nargs = []`r`nenabled = true`r`n`r`n"
}
function Set-ClaudeEntry([string]$Text,$Entry) {
    if([string]::IsNullOrWhiteSpace($Text)){$config=New-Object psobject}else{$config=$Text|ConvertFrom-Json}
    if(!$config.PSObject.Properties['mcpServers']){$config|Add-Member NoteProperty mcpServers (New-Object psobject)}
    if($null -eq $config.mcpServers -or $config.mcpServers -isnot [pscustomobject]){throw 'Invalid Claude mcpServers object.'}
    if($config.mcpServers.PSObject.Properties['nanocad']){$config.mcpServers.PSObject.Properties.Remove('nanocad')}
    if($null -ne $Entry){$config.mcpServers|Add-Member NoteProperty nanocad $Entry}
    return ($config|ConvertTo-Json -Depth 60)
}
function Assert-Package([string]$Root) {
    $manifest=Get-Content -LiteralPath (Join-Path $Root 'manifest.json') -Raw | ConvertFrom-Json
    if($manifest.platform -ne 'win-x64'){throw 'Unsupported package platform.'}
    $base=[IO.Path]::GetFullPath($Root).TrimEnd('\')+'\'
    foreach($item in $manifest.files){
        $path=[IO.Path]::GetFullPath((Join-Path $Root $item.path))
        if(!$path.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw 'Package path escapes root.'}
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.sha256){throw ('Package file checksum mismatch: '+$item.path)}
    }
    return $manifest
}
function Get-NanoCadCompatibility([string]$Path) {
    # Minimum tested file-resource composition. Managed host files legitimately
    # use an earlier build than the executable and the MultiCAD/native runtime.
    $requirements=@(
        @{path='nCad.exe';minimum='26.0.7476.4950'},
        @{path='bin\hostmgd.dll';minimum='26.0.7476.3867'},
        @{path='bin\hostdbmgd.dll';minimum='26.0.7476.3867'},
        @{path='bin\hostPointCloudsMgd.dll';minimum='26.0.7476.3867'},
        @{path='bin\NrxDbGate.dll';minimum='26.0.7476.4950'},
        @{path='bin_nPlat\mapimgd.dll';minimum='26.0.7476.4950'},
        @{path='bin_nPlat\MapiBaseTypes.dll';minimum='26.0.7476.4950'},
        @{path='bin_nPlat\MapiBaseTypes2.dll';minimum='26.0.7476.4950';explicit_zero_resource=$true},
        @{path='bin_nPlat\mapinet.dll';minimum='26.0.7476.4950'},
        @{path='bin_nPlat\M3D.dll';minimum='26.0.7476.4950'},
        @{path='bin_nPlat\mt.dll';minimum='26.0.7476.4950'},
        @{path='bin_nPlat\McTyp.dll';minimum='26.0.7476.4950'},
        @{path='bin_nPlat\McGeL.dll';minimum='26.0.7476.4950'},
        @{path='bin_nPlat\MapiSwigCpp.dll';minimum='26.0.7476.4950'},
        @{path='bin_nPlat\McDDGate3.dll';minimum='26.0.7476.4950'}
    )
    $root=[IO.Path]::GetFullPath($Path)
    $files=New-Object 'Collections.Generic.List[object]'
    foreach($required in $requirements){
        $file=Join-Path $root $required.path
        $found=$null;$status='missing';$supported=$false
        if(Test-Path -LiteralPath $file -PathType Leaf){
            try {
                $info=[Diagnostics.FileVersionInfo]::GetVersionInfo($file)
                # Never parse display/product/assembly version text or load a DLL.
                $version=[version]('{0}.{1}.{2}.{3}' -f $info.FileMajorPart,$info.FileMinorPart,$info.FileBuildPart,$info.FilePrivatePart)
                $found=$version.ToString()
                if([string]::IsNullOrWhiteSpace($info.FileVersion)){$status='unknown_version'}
                elseif($version -eq [version]'0.0.0.0'){
                    # This mandatory SWIG assembly has an explicitly zero-valued
                    # version resource in the tested vendor installation.
                    $supported=$required.ContainsKey('explicit_zero_resource') -and $required.explicit_zero_resource
                    $status='unknown_version';if($supported){$status='known_unversioned_resource'}
                }
                elseif($version.Major -ne 26 -or $version.Minor -ne 0){$status='unsupported_series'}
                elseif($version -lt [version]$required.minimum){$status='older_version'}
                else{$status='supported';$supported=$true}
            } catch {$status='unreadable_version'}
        }
        $minimum=[string]$required.minimum
        if($required.ContainsKey('explicit_zero_resource')){$minimum+=' or the vendor explicit 0.0.0.0 resource'}
        [void]$files.Add([pscustomobject]@{path=$required.path;found_version=$found;required_version=$minimum;status=$status;compatible=$supported})
    }
    $iniExists=Test-Path -LiteralPath (Join-Path $root 'nCad.ini') -PathType Leaf
    [void]$files.Add([pscustomobject]@{path='nCad.ini';found_version=$null;required_version='existing configuration';status=$(if($iniExists){'present'}else{'missing'});compatible=$iniExists})
    $failed=@($files|Where-Object {!$_.compatible})
    $message='nanoCAD compatibility check passed.'
    if($failed.Count){
        $details=@($failed|ForEach-Object {
            $found=$_.found_version;if(!$found){$found=$_.status}
            $_.path+': found '+$found+'; required '+$_.required_version+' ('+$_.status+').'
        }) -join "`r`n"
        $message="nanoCAD compatibility check failed.`r`nFolder: $root`r`n$details`r`n`r`nRequired: nanoCAD 26.0 with nCad.exe 26.0.7476.4950 or later and the matching updated runtime files listed above. Earlier builds have not been supported by this installer. Install the latest nanoCAD 26.0 updates manually from Nanosoft (automatic updates may be disabled), or select an updated installation. Close nanoCAD after saving your work, then run setup again."
    }
    return [pscustomobject]@{cad_path=$root;compatible=($failed.Count -eq 0);supported_series='26.0';minimum_executable_version='26.0.7476.4950';files=$files.ToArray();message=$message}
}
function Assert-NanoCadCompatibility([string]$Path) {
    $report=Get-NanoCadCompatibility $Path
    if(!$report.compatible){throw $report.message}
    return $report
}
function Find-NanoCad {
    $paths=New-Object 'Collections.Generic.List[string]'
    foreach($root in @('HKLM:\SOFTWARE\Nanosoft','HKLM:\SOFTWARE\Nanosoft AS','HKCU:\SOFTWARE\Nanosoft')){
        if(Test-Path $root){Get-ChildItem $root -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            $v=Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            if($v -and $v.PSObject.Properties['InstallLocation']){[void]$paths.Add([string]$v.InstallLocation)}
        }}
    }
    foreach($vendor in @('Nanosoft AS','Nanosoft')){
        $root=Join-Path $env:ProgramFiles $vendor
        if(Test-Path -LiteralPath $root){Get-ChildItem -LiteralPath $root -Directory | Where-Object Name -Like '*26*' | ForEach-Object{[void]$paths.Add($_.FullName)}}
    }
    return @($paths|ForEach-Object{[IO.Path]::GetFullPath($_).TrimEnd('\')}|Sort-Object -Unique|Where-Object{
        $exe=Join-Path $_ 'nCad.exe';$ini=Join-Path $_ 'nCad.ini'
        (Test-Path -LiteralPath $exe -PathType Leaf) -and (Test-Path -LiteralPath $ini -PathType Leaf)
    })
}

function Get-SetupErrorText($Failure) {
    # Keep diagnostic details available without exposing implementation errors in the UI.
    $raw=[string]$Failure.Exception.Message
    $message=switch -Regex ($raw) {
        '^nanoCAD compatibility check failed' {$raw;break}
        '^Close nanoCAD|^Run the latest installer' {$raw;break}
        'Uninstall path validation' {'The installation location has changed. Contact support to remove it safely.';break}
        'Choose.*nanoCAD|Select nanoCAD|Existing nCad.ini' {'Select the folder where nanoCAD 26 is installed, then try again.';break}
        'Choose at least one client' {'Select Codex, Claude Desktop, or both before installing.';break}
        'another nanoCAD exists' {'This connection uses another nanoCAD installation. Disconnect it before choosing a different installation.';break}
        'include all previously connected' {'Select all the assistants you previously connected, then try again.';break}
        'settings changed after installation' {'Your connection settings have changed since installation. Setup could not disconnect automatically. Contact support for help.';break}
        'Duplicate Codex|Invalid Claude|Multiple NetModules' {'Existing application settings need attention before setup can continue. Contact support for help.';break}
        'Existing version differs' {'The installed files differ from this installer. Contact support before replacing them.';break}
        'checksum|Staged installation|missing|damaged' {'Some required files are missing or damaged. Obtain a fresh copy of the installer and try again.';break}
        'Windows x64|Unsupported package platform' {'This installer requires 64-bit Windows.';break}
        'registration was not completed' {'Windows did not complete the installation. Try again and approve the Windows permission request, or ask your administrator for help.';break}
        'timed out|stopped before replying|MCP error|unsolicited' {'The connection check could not finish. Try again. If the problem continues, contact support.';break}
        default {'This action could not be completed. Try again. If the problem continues, contact support.'}
    }
    try {
        $logs=Join-Path $env:LOCALAPPDATA 'NanoCADMCP\Logs'
        [IO.Directory]::CreateDirectory($logs)|Out-Null
        $log=Join-Path $logs ('setup-'+[guid]::NewGuid().ToString('N')+'.txt')
        [IO.File]::WriteAllText($log,($Failure|Out-String),[Text.Encoding]::UTF8)
        $message+="`r`n`r`nDetails for support: $log"
    } catch { }
    return $message
}
