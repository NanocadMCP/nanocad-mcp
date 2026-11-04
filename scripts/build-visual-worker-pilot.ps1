$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'artifacts\visual-observation-pilot\bin'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$vs=& 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe' -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'C++ build tools unavailable'}
$msvc=(Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdk='C:\Program Files (x86)\Windows Kits\10'
$version='10.0.26100.0'
$includes=@('/I'+(Join-Path $msvc 'include'))
foreach($part in @('ucrt','shared','um','winrt','cppwinrt')){$includes+=('/I'+(Join-Path $sdk "Include\$version\$part"))}
$libs=@(('/LIBPATH:'+(Join-Path $msvc 'lib\x64')),('/LIBPATH:'+(Join-Path $sdk "Lib\$version\um\x64")),('/LIBPATH:'+(Join-Path $sdk "Lib\$version\ucrt\x64")))
$compiler=Join-Path $msvc 'bin\Hostx64\x64\cl.exe'
& $compiler /nologo /std:c++20 /EHsc /O2 /MD /W3 /WX @includes (Join-Path $root 'src\NanoCAD.VisualWorker\VisualWorker.cpp') ('/Fo'+(Join-Path $out 'VisualWorker.obj')) ('/Fe'+(Join-Path $out 'VisualWorker.exe')) /link @libs windowsapp.lib d3d11.lib user32.lib dwmapi.lib
if($LASTEXITCODE -ne 0){throw 'Visual worker build failed'}
& (Join-Path $out 'VisualWorker.exe') --help
if($LASTEXITCODE -ne 0){throw 'Visual worker help failed'}
Get-FileHash -LiteralPath (Join-Path $out 'VisualWorker.exe') -Algorithm SHA256
