param([ValidateSet('Debug','Release')][string]$Configuration='Debug')
# Compile only. Never load the resulting DLL or control nanoCAD here.
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $root 'src\NanoCAD.NativeParameters\generate-sdk-slice412.ps1')
$out=Join-Path $root ('artifacts\native-parameters412\'+$Configuration)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$vs=& 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe' -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 tools are required to build the native parameter reader.'}
$msvc=(Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$windowsSdk='C:\Program Files (x86)\Windows Kits\10'
$version=(Get-ChildItem -LiteralPath (Join-Path $windowsSdk 'Include') -Directory | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h')} | Sort-Object Name -Descending | Select-Object -First 1).Name
if(!$version){throw 'Windows SDK headers are unavailable.'}
$includes=@('/I'+(Join-Path $msvc 'include'))
$includes+=('/I'+(Join-Path $root 'src\NanoCAD.NativeParameters\sdk-compat'))
$includes+=('/I'+(Join-Path $root 'NCadSDK_260_EN_7355.4942.259\include\MAPI'))
foreach($part in @('ucrt','shared','um')){$includes+=('/I'+(Join-Path $windowsSdk "Include\$version\$part"))}
$libs=@(('/LIBPATH:'+(Join-Path $msvc 'lib\x64')),('/LIBPATH:'+(Join-Path $windowsSdk "Lib\$version\um\x64")),('/LIBPATH:'+(Join-Path $windowsSdk "Lib\$version\ucrt\x64")),('/LIBPATH:'+(Join-Path $root 'NCadSDK_260_EN_7355.4942.259\lib-x64')))
$compiler=Join-Path $msvc 'bin\Hostx64\x64\cl.exe'
& $compiler /nologo /std:c++17 /utf-8 /EHsc /O2 /MT /W3 /LD /DUNICODE /D_UNICODE /DNDEBUG @includes (Join-Path $root 'src\NanoCAD.NativeParameters\NativeParameters.cpp') ('/Fo'+(Join-Path $out 'NativeParameters.obj')) /link @libs User32.lib ('/OUT:'+(Join-Path $out 'NanoCAD.NativeParameters412.dll')) ('/IMPLIB:'+(Join-Path $out 'NanoCAD.NativeParameters412.lib'))
if($LASTEXITCODE -ne 0){throw 'Native parameter reader compilation failed; no CAD action performed.'}
