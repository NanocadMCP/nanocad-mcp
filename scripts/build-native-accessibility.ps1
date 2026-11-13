# Copyright 2026 Andrey Juliovich Camejo Severinov. Licensed under MIT.
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$MfcDirectory
)

# Compile only. This script never loads the DLL or connects to nanoCAD.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else {
    Join-Path $root ('artifacts\native-accessibility\' + $Configuration)
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Build Tools are required.' }
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'MSVC x64 build tools are required.' }
$msvc = (Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1).FullName
$mfc = if ($MfcDirectory) { [IO.Path]::GetFullPath($MfcDirectory) } else { Join-Path $msvc 'atlmfc' }
foreach ($relative in @('include\afxwin.h', 'include\afxbasetabctrl.h',
    'lib\x64\mfc140u.lib', 'lib\x64\mfcs140u.lib', 'lib\x64\atls.lib')) {
    if (!(Test-Path -LiteralPath (Join-Path $mfc $relative))) {
        throw "Matching x64 MFC/ATL development dependencies are missing: $relative. Supply -MfcDirectory for an isolated dependency directory."
    }
}
$sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdkVersion = (Get-ChildItem -LiteralPath (Join-Path $sdk 'Include') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h') } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1).Name
if (!$sdkVersion) { throw 'Windows SDK headers and x64 libraries are required.' }
$includes = @(('/I' + (Join-Path $mfc 'include')), ('/I' + (Join-Path $msvc 'include')))
foreach ($part in @('ucrt', 'shared', 'um', 'winrt')) {
    $includes += '/I' + (Join-Path $sdk "Include\$sdkVersion\$part")
}
$libraries = @(('/LIBPATH:' + (Join-Path $mfc 'lib\x64')),
    ('/LIBPATH:' + (Join-Path $msvc 'lib\x64')),
    ('/LIBPATH:' + (Join-Path $sdk "Lib\$sdkVersion\um\x64")),
    ('/LIBPATH:' + (Join-Path $sdk "Lib\$sdkVersion\ucrt\x64")))
New-Item -ItemType Directory -Path $out -Force | Out-Null
# MFC imports use stable ordinal exports. Read the exact getter import from
# the selected x64 library rather than embedding a version-specific ordinal.
$archive = [IO.File]::ReadAllBytes((Join-Path $mfc 'lib\x64\mfc140u.lib'))
if ([Text.Encoding]::ASCII.GetString($archive, 0, 8) -ne "!<arch>`n") {
    throw 'The selected MFC import library is not a COFF archive.'
}
$valueImports = @()
$archiveOffset = 8
while ($archiveOffset + 60 -le $archive.Length) {
    $memberSize = [int]::Parse([Text.Encoding]::ASCII.GetString($archive, $archiveOffset + 48, 10).Trim())
    $body = $archiveOffset + 60
    if ($memberSize -lt 0 -or $body + $memberSize -gt $archive.Length) { throw 'Invalid MFC archive member.' }
    if ($memberSize -ge 20 -and [BitConverter]::ToUInt16($archive, $body) -eq 0 -and
        [BitConverter]::ToUInt16($archive, $body + 2) -eq 0xffff) {
        $dataSize = [BitConverter]::ToUInt32($archive, $body + 12)
        if ($dataSize -le $memberSize - 20) {
            $names = [Text.Encoding]::ASCII.GetString($archive, $body + 20, [int]$dataSize).Split([char]0)
            if ($names.Length -ge 2 -and $names[0].StartsWith('?get_accValue@CMFCBaseTabCtrl@@', [StringComparison]::Ordinal)) {
                $valueImports += [pscustomobject]@{
                    symbol = $names[0]; dll = $names[1]
                    ordinal = [BitConverter]::ToUInt16($archive, $body + 16)
                    type = [BitConverter]::ToUInt16($archive, $body + 18)
                    machine = [BitConverter]::ToUInt16($archive, $body + 6)
                }
            }
        }
    }
    $archiveOffset = $body + $memberSize + ($memberSize % 2)
}
if ($valueImports.Count -ne 1 -or $valueImports[0].machine -ne 0x8664 -or
    $valueImports[0].type -ne 0 -or $valueImports[0].dll -ne 'mfc140u.dll') {
    throw 'The selected x64 MFC import library has no unique ordinal import for the native base Value getter.'
}
$valueImport = $valueImports[0]
@"
// Generated from the selected official MFC x64 import library. Do not edit.
#pragma once
namespace NanoCAD::Accessibility::Build
{
    inline constexpr unsigned short BaseValueOrdinal = $($valueImport.ordinal);
    inline constexpr char MfcImportDll[] = "mfc140u.dll";
}
"@ | Set-Content -LiteralPath (Join-Path $out 'NativeValueImport.h') -Encoding ascii
$includes += '/I' + $out
$source = Join-Path $root 'src\NanoCAD.NativeAccessibility\AccessibilityGuard.cpp'
$compiler = Join-Path $msvc 'bin\Hostx64\x64\cl.exe'
$options = @('/nologo', '/std:c++20', '/permissive-', '/utf-8', '/EHsc', '/GR', '/MD', '/W4',
    '/LD', '/D_AFXEXT', '/D_AFXDLL', '/DNANOCAD_ACCESSIBILITY_BUILD', '/DUNICODE', '/D_UNICODE',
    '/D_WIN32_WINNT=0x0A00', '/DNDEBUG', '/Zi', ('/Fd' + (Join-Path $out 'AccessibilityGuard.compiler.pdb')))
$options += if ($Configuration -eq 'Release') { '/O2' } else { '/Od' }
$guidsObject = Join-Path $out 'AccessibilityGuids.obj'
& $compiler /nologo /c /std:c++20 /utf-8 /EHsc /MD /O2 @includes `
    (Join-Path $root 'src\NanoCAD.NativeAccessibility\AccessibilityGuids.cpp') ('/Fo' + $guidsObject)
if ($LASTEXITCODE -ne 0) { throw 'Accessibility SDK GUID compilation failed.' }
& $compiler @options @includes $source $guidsObject ('/Fo' + (Join-Path $out 'AccessibilityGuard.obj')) /link @libraries `
    Oleacc.lib Ole32.lib OleAut32.lib Comctl32.lib /DEBUG:FULL /INCREMENTAL:NO /GUARD:CF /DYNAMICBASE /NXCOMPAT `
    ('/OUT:' + (Join-Path $out 'NanoCAD.NativeAccessibility.dll')) `
    ('/IMPLIB:' + (Join-Path $out 'NanoCAD.NativeAccessibility.lib')) `
    ('/PDB:' + (Join-Path $out 'NanoCAD.NativeAccessibility.pdb'))
if ($LASTEXITCODE -ne 0) { throw 'Native accessibility guard build failed; no CAD action was performed.' }
[pscustomobject]@{
    dll = Join-Path $out 'NanoCAD.NativeAccessibility.dll'
    sha256 = (Get-FileHash -LiteralPath (Join-Path $out 'NanoCAD.NativeAccessibility.dll') -Algorithm SHA256).Hash
    compiler = $msvc
    mfc = $mfc
    windows_sdk = $sdkVersion
    runtime = 'Shared release MFC/CRT (/MD), including when Configuration=Debug'
    base_value_import = $valueImport
} | ConvertTo-Json -Compress
