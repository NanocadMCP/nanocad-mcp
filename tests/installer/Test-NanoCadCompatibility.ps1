param([string]$EvidenceRoot)
Set-StrictMode -Version 2
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'deployment\windows\Install.Common.ps1')
if(!$EvidenceRoot){$EvidenceRoot=Join-Path $root ('artifacts\installer-compatibility-'+[guid]::NewGuid().ToString('N'))}
$evidence=[IO.Path]::GetFullPath($EvidenceRoot)
if(Test-Path -LiteralPath $evidence){throw 'Use a new evidence directory.'}
[void][IO.Directory]::CreateDirectory($evidence)
$checks=New-Object 'Collections.Generic.List[string]'
function Check([bool]$Value,[string]$Message){if(!$Value){throw $Message};[void]$checks.Add($Message)}
function New-VersionFixture([string]$Name,[string]$Version){
    $source=Join-Path $evidence ($Name+'.cs');$output=Join-Path $evidence ($Name+'.dll')
    # Real PE resources, read without loading or executing the resulting fixture.
    $code='using System.Reflection; [assembly: AssemblyVersion("1.0.0.0")] [assembly: AssemblyFileVersion("'+$Version+'")] public static class Fixture {}'
    [IO.File]::WriteAllText($source,$code)
    $compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $log=& $compiler /nologo /target:library ('/out:'+$output) $source 2>&1
    if($LASTEXITCODE -ne 0){throw ('Fixture compiler failed: '+($log -join "`n"))}
    return $output
}
$current=New-VersionFixture 'current' '26.0.7476.4950'
$managed=New-VersionFixture 'managed' '26.0.7476.3867'
$old=New-VersionFixture 'old' '26.0.7476.4949'
$wrong=New-VersionFixture 'wrong-major' '27.0.7476.4950'
$minor=New-VersionFixture 'wrong-minor' '26.1.7476.4950'
$newer=New-VersionFixture 'newer' '26.0.9000.1'
$zero=New-VersionFixture 'zero' '0.0.0.0'
$unknown=Join-Path $evidence 'no-version-resource.dll'
$bytes=[IO.File]::ReadAllBytes($current)
# Remove only the PE resource-directory entry from a valid compiled PE fixture.
$pe=[BitConverter]::ToInt32($bytes,60);$optional=$pe+24
$magic=[BitConverter]::ToUInt16($bytes,$optional)
$directory=$optional+96;if($magic -eq 523){$directory=$optional+112}elseif($magic -ne 267){throw 'Unexpected fixture PE format.'}
for($i=0;$i -lt 8;$i++){$bytes[$directory+16+$i]=0}
[IO.File]::WriteAllBytes($unknown,$bytes)
Check ([string]::IsNullOrEmpty([Diagnostics.FileVersionInfo]::GetVersionInfo($unknown).FileVersion)) 'Compiled PE without version resource is genuinely unknown'
$cad=Join-Path $evidence 'CAD fixture'
[void][IO.Directory]::CreateDirectory($cad)
# Read the real production policy; no test overrides or fake version-reader API.
$initial=Get-NanoCadCompatibility $cad
Check ($initial.files.Count -eq 16) 'Policy covers executable, fourteen required runtime DLLs and INI'
foreach($required in $initial.files){
    $dest=Join-Path $cad $required.path;[void][IO.Directory]::CreateDirectory((Split-Path -Parent $dest))
    if($required.path -eq 'nCad.ini'){
        [IO.File]::WriteAllText($dest,"; fixture`r`n[\NetModules]`r`nC:\Other.dll`r`n")
    }else{
        $fixture=$current
        if($required.path -match '^bin\\host'){$fixture=$managed}
        if($required.path -eq 'bin_nPlat\MapiBaseTypes2.dll'){$fixture=$zero}
        [IO.File]::Copy($fixture,$dest)
    }
}
$baseline=Assert-NanoCadCompatibility $cad
Check $baseline.compatible 'Tested split3867/4950 and explicit-zero vendor resource composition passes'
Check ((@($baseline.files|Where-Object status -eq 'known_unversioned_resource')).Count -eq 1) 'Only the declared unversioned runtime is excepted'
Check ((@($baseline.files|Where-Object path -eq 'bin\hostdbmgd.dll'))[0].found_version -eq '26.0.7476.3867') 'Reports numeric resource version rather than assembly version1.0'
$exe=Join-Path $cad 'nCad.exe'
foreach($test in @(@($old,'older_version'),@($wrong,'unsupported_series'),@($minor,'unsupported_series'),@($zero,'unknown_version'),@($unknown,'unknown_version'))){
    [IO.File]::Copy($test[0],$exe,$true)
    $report=Get-NanoCadCompatibility $cad
    Check (!$report.compatible -and $report.files[0].status -eq $test[1]) ('Reject executable '+$test[1]+' from '+[IO.Path]::GetFileName($test[0]))
    Check ($report.message.Contains('found ') -and $report.message.Contains('required ') -and $report.message.Contains('manually')) 'Failure includes found, required and manual-update action'
}
[IO.File]::Copy($newer,$exe,$true)
Check (Assert-NanoCadCompatibility $cad).compatible 'Later26.0 maintenance build passes numeric floor'
[IO.File]::Copy($current,$exe,$true)
foreach($required in @($baseline.files|Where-Object {$_.path -ne 'nCad.ini'})){
    $file=Join-Path $cad $required.path;$saved=[IO.File]::ReadAllBytes($file)
    [IO.File]::Move($file,$file+'.held')
    try{Check (!(Get-NanoCadCompatibility $cad).compatible) ('Missing mandatory file rejected: '+$required.path)}finally{[IO.File]::Move($file+'.held',$file)}
    [IO.File]::Copy($unknown,$file,$true)
    try{Check (!(Get-NanoCadCompatibility $cad).compatible) ('Missing version resource rejected: '+$required.path)}finally{[IO.File]::WriteAllBytes($file,$saved)}
}
$runtime=Join-Path $cad 'bin_nPlat\M3D.dll'
[IO.File]::Copy($old,$runtime,$true)
Check (!(Get-NanoCadCompatibility $cad).compatible) 'New executable cannot hide an older modeling runtime'
[IO.File]::Copy($current,$runtime,$true)
$base2=Join-Path $cad 'bin_nPlat\MapiBaseTypes2.dll'
[IO.File]::Copy($current,$base2,$true)
Check (Assert-NanoCadCompatibility $cad).compatible 'Future versioned26.0 replacement of the unversioned runtime passes'
[IO.File]::Copy($zero,$base2,$true)
$bridge=Join-Path $evidence 'NanoCADBridge.dll';[IO.File]::Copy($current,$bridge)
$ini=Join-Path $cad 'nCad.ini';$originalIni=[IO.File]::ReadAllText($ini)
[IO.File]::Copy($old,$exe,$true)
$before=@(Get-ChildItem -LiteralPath $cad -File -Recurse).Count
$rejected=$false
try{Set-BridgeRegistration $ini $bridge $false}catch{$rejected=$_.Exception.Message.StartsWith('nanoCAD compatibility check failed.')}
Check ($rejected -and [IO.File]::ReadAllText($ini) -ceq $originalIni -and @(Get-ChildItem -LiteralPath $cad -File -Recurse).Count -eq $before) 'Direct Add rejects before INI backup or mutation'
# An already-disconnected registration still exercises the real Remove entry
# point without retesting the unrelated atomic INI replacement mechanism.
Set-BridgeRegistration $ini $bridge $true
Check ([IO.File]::ReadAllText($ini) -ceq $originalIni) 'Remove is admitted with an incompatible CAD version'

# Exercise actual CLI/elevated-helper entry points in isolated subprocess profiles.
# No HOME/CODEX_HOME changes, registry operations, CAD launch or successful install.
$package=Join-Path $evidence 'package';[void][IO.Directory]::CreateDirectory($package)
foreach($name in @('Setup.ps1','Install.Common.ps1','Maintenance.ps1','Register-Bridge.ps1')){[IO.File]::Copy((Join-Path $root ('deployment\windows\'+$name)),(Join-Path $package $name))}
[IO.File]::WriteAllText((Join-Path $package 'manifest.json'),'{"platform":"win-x64","package_build":"fixture","files":[]}')
$fixtureProfile=Join-Path $evidence 'isolated-profile';$local=Join-Path $fixtureProfile 'Local';$roaming=Join-Path $fixtureProfile 'Roaming'
[void][IO.Directory]::CreateDirectory((Join-Path $roaming 'Claude'))
$claude=Join-Path $roaming 'Claude\claude_desktop_config.json';[IO.File]::WriteAllText($claude,'malformed-client-config-must-not-be-read')
function Run-Isolated([string]$Script,[string]$Arguments){
    $engine=Join-Path $PSHOME 'powershell.exe';if($PSVersionTable.PSEdition -eq 'Core'){$engine=Join-Path $PSHOME 'pwsh.exe'}
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$engine;$start.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$Script+'" '+$Arguments
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $start.EnvironmentVariables['LOCALAPPDATA']=$local;$start.EnvironmentVariables['APPDATA']=$roaming
    $process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEnd();$stderr=$process.StandardError.ReadToEnd();$process.WaitForExit()
    $result=[pscustomobject]@{exit_code=$process.ExitCode;stdout=$stdout;stderr=$stderr};$process.Dispose();return $result
}
$cli=Run-Isolated (Join-Path $package 'Setup.ps1') ('-Mode Install -NanoCadPath "'+$cad+'" -Clients ClaudeDesktop -NoElevation')
[IO.File]::WriteAllText((Join-Path $evidence 'cli-install.json'),($cli|ConvertTo-Json -Depth 4))
Check ($cli.exit_code -ne 0 -and ($cli.stdout+$cli.stderr) -match 'nanoCAD\s+compatibility\s+check\s+failed\.') 'CLI Install rejects before malformed client config is read'
Check (!(Test-Path -LiteralPath (Join-Path $local 'Programs\NanoCADMCP')) -and [IO.File]::ReadAllText($claude) -ceq 'malformed-client-config-must-not-be-read') 'Rejected CLI creates no installed files and preserves client config'
$helper=Run-Isolated (Join-Path $package 'Register-Bridge.ps1') ('-Mode Add -IniPath "'+$ini+'" -BridgePath "'+$bridge+'"')
[IO.File]::WriteAllText((Join-Path $evidence 'registration-helper.json'),($helper|ConvertTo-Json -Depth 4))
Check ($helper.exit_code -ne 0 -and ($helper.stdout+$helper.stderr) -match 'nanoCAD\s+compatibility\s+check\s+failed\.') 'Standalone/elevated Add helper shares the same rejection'
Check ([IO.File]::ReadAllText($ini) -ceq $originalIni) 'Rejected helper preserves INI'
$remove=Run-Isolated (Join-Path $package 'Setup.ps1') ('-Mode Remove -NanoCadPath "'+$cad+'" -NoElevation')
Check ($remove.exit_code -eq 0 -and $remove.stdout.Contains('No managed installation found.')) 'CLI removal does not require a supported CAD installation'
$plan=Run-Isolated (Join-Path $package 'Setup.ps1') '-Mode Plan'
[IO.File]::WriteAllText((Join-Path $evidence 'plan.json'),($plan|ConvertTo-Json -Depth 4))
Check ($plan.exit_code -eq 0) 'Package verification Plan remains read-only and does not require compatible CAD'
$planData=$plan.stdout|ConvertFrom-Json
Check (!$planData.changes_applied -and $null -ne $planData.PSObject.Properties['cad_compatibility']) 'Plan exposes compatibility observations without applying changes'
$selectedPlan=Run-Isolated (Join-Path $package 'Setup.ps1') ('-Mode Plan -NanoCadPath "'+$cad+'"')
[IO.File]::WriteAllText((Join-Path $evidence 'selected-plan.json'),($selectedPlan|ConvertTo-Json -Depth 4))
$selectedData=$selectedPlan.stdout|ConvertFrom-Json
Check ($selectedPlan.exit_code -eq 0 -and @($selectedData.cad_compatibility).Count -eq 1 -and $selectedData.cad_compatibility[0].cad_path -eq $cad -and !$selectedData.cad_compatibility[0].compatible) 'Plan reports the explicitly selected incompatible target rather than another discovered installation'
$uiScript=Join-Path $evidence 'ui-error-text.ps1'
[IO.File]::WriteAllText($uiScript,('. "'+(Join-Path $package 'Install.Common.ps1')+'"; try { Assert-NanoCadCompatibility "'+$cad+'" } catch { Get-SetupErrorText $_ }'))
$ui=Run-Isolated $uiScript ''
Check ($ui.exit_code -eq 0 -and $ui.stdout.Contains('26.0.7476.4949') -and $ui.stdout.Contains('26.0.7476.4950') -and $ui.stdout.Contains('manually')) 'GUI error formatter preserves actual and required versions plus manual-update action'
foreach($file in @('Setup.ps1','Install.Common.ps1')){
    $tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $package $file),[ref]$tokens,[ref]$errors)
    Check ($errors.Count -eq 0) ('Current PowerShell parser accepts '+$file)
}
$report=[ordered]@{passed=$true;power_shell_version=$PSVersionTable.PSVersion.ToString();checks=$checks.ToArray();native_sdk_or_cad_loaded=$false;installation_performed=$false;registry_changed=$false;cli=$cli;registration_helper=$helper;remove=$remove}
[IO.File]::WriteAllText((Join-Path $evidence 'report.json'),($report|ConvertTo-Json -Depth 8))
Write-Output ($checks.Count.ToString()+' checks passed. Evidence: '+$evidence)
