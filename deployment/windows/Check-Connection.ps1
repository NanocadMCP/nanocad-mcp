param([string]$Server=(Join-Path $PSScriptRoot 'server\NanoCAD.McpServer.exe'),[switch]$Gui)
. (Join-Path $PSScriptRoot 'Install.Common.ps1')
$ErrorActionPreference='Stop'
$process=$null
try {
    if(!(Test-Path -LiteralPath $Server)){throw 'Installed server executable is missing.'}
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$Server;$start.WorkingDirectory=Split-Path -Parent $Server
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process;$process.StartInfo=$start;[void]$process.Start()
    $stderr=$process.StandardError.ReadToEndAsync()
    function Rpc([int]$Id,[string]$Method,$Parameters){
        $request=@{jsonrpc='2.0';id=$Id;method=$Method;params=$Parameters}|ConvertTo-Json -Depth 15 -Compress
        $process.StandardInput.WriteLine($request);$process.StandardInput.Flush()
        for($n=0;$n -lt 16;$n++){
            $line=$process.StandardOutput.ReadLineAsync()
            if(!$line.Wait(15000)){throw 'MCP response timed out.'}
            if(!$line.Result){throw 'MCP server stopped before replying.'}
            $response=$line.Result|ConvertFrom-Json
            if($response.PSObject.Properties['id'] -and $response.id -eq $Id){
                if($response.PSObject.Properties['error']){throw ('MCP error: '+($response.error|ConvertTo-Json -Compress))}
                return $response.result
            }
        }
        throw 'Too many unsolicited MCP messages.'
    }
    $hello=Rpc 1 'initialize' @{protocolVersion='2025-11-25';capabilities=@{};clientInfo=@{name='nanocad-local-setup';version='284'}}
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}');$process.StandardInput.Flush()
    $result=Rpc 2 'tools/call' @{name='nanocad.get_status';arguments=@{}}
    $status=$result.structuredContent
    if(!$status.success){throw $status.error.message}
    $live=@($status.data.instances|Where-Object {$_.reachable -and $_.status.ready_at_last_idle})
    $report=[ordered]@{server_started=$true;server_version=$status.data.server_version;ready_bridges=$live.Count;cad_changed=$false;client_connection_proven=$false;message='nanoCAD is not ready to connect. Open nanoCAD and a drawing, finish any active command and close any message boxes. Then try Check connection again.'}
    if($live.Count){$report.message='nanoCAD is available. Open your assistant and ask it to show the current drawing to check the connection from there.'}
    if($Gui){Add-Type -AssemblyName System.Windows.Forms;[void][Windows.Forms.MessageBox]::Show($report.message,'nanoCAD MCP connection')}
    $report|ConvertTo-Json -Depth 10
}catch{
    if($Gui){Add-Type -AssemblyName System.Windows.Forms;[void][Windows.Forms.MessageBox]::Show((Get-SetupErrorText $_),'Connection check')}
    if(!$Gui){Write-Error $_;exit 1}
}finally{
    if($process){if(!$process.HasExited){$process.StandardInput.Close();if(!$process.WaitForExit(2500)){$process.Kill();$process.WaitForExit()}};$process.Dispose()}
}
