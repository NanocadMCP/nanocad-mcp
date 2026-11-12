# Compile-time SDK dependency slice. Does not build/load a DLL or touch CAD.
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sdk=Join-Path $taskRoot 'NCadSDK_260_EN_7355.4942.259/include/MAPI'
$output=Join-Path $PSScriptRoot 'sdk-compat/generated'
$encoding=[Text.Encoding]::GetEncoding(28591) # Byte-preserving lexical view.
$anchors=@{
    'IMcObjects.h'='DC2BB56B9277A20069771F2EA7F3D834FB3C6740406CA58475F7B0A20B030E0F'
    'IMcCutter.h'='2E3444E919673542BBD73A65738B34D2A2C2F01763E3C88CC82B46C3F4576535'
    'McsGeometry.h'='D8A377471B5A460DC54044B453868013785B9ED855B64BAC5930D334EE13D573'
    '3DCore.h'='710E2603AD27B75C44A3C55C0BED154544D5FB245975598163EC601C36A7E314'
    'IMcDimension.h'='5CEC267EEB966DE567D53C070069804FEF9D593339DD9A9BA22A9839423EB115'
    'IMcStyles.h'='5151C1191EACF2B0F8C42622F6C634BF231AC7FC7FA9B7BCC7598EC941AD7D9F'
}
$coreKeep=@('m3dIPPDef','IMc3dHistoryItem','IMc3dSolid','IMc3dAlienSolid','IMc3dAlienSolidData','IMcFeatureEntity','IMc3dHole','IMc3dThicknessFeature','IMcsDocVariablesManager',
    'IMc3dArrFeatureType','IMc3dCircPatFeature','IMc3dRectPatFeature','McDrawingViewLocalCutData','IMcSketch','IMcPlanarSketch',
    'IMc3dCoordSysObject','IMc3dWorkPlane','IMc3dDrawingView')
$required=@{
    'IMcObjects.h'=@('IMcPropertySource','IMcDbObject','IMcDbEntity','IMcEntity','IMc3dConstraintCompatible','IMcObjectsManager','mcsComplexWorkID','McGeometryParam')
    '3DCore.h'=$coreKeep
    'IMcDocuments.h'=@('IMcDocument','IMcDocumentsManager')
    'IMcDimension.h'=@('IMcDimension','McDimensionData','CTechSize')
    'IMcStyles.h'=@('IMcDimStyle')
}
$records=@{};$definitions=[Collections.Generic.List[object]]::new();$removed=[Collections.Generic.List[object]]::new();$exceptions=[Collections.Generic.List[object]]::new()
function Hash-Text([string]$text){$hash=[Security.Cryptography.SHA256]::Create();try{[BitConverter]::ToString($hash.ComputeHash($encoding.GetBytes($text))).Replace('-','')}finally{$hash.Dispose()}}
function Mask-Cpp([string]$text){
    [regex]::Replace($text,'(?s)/\*.*?\*/|//[^\r\n]*|"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''',
        [Text.RegularExpressions.MatchEvaluator]{param($match)[regex]::Replace($match.Value,'[^\r\n]',' ')})
}
function Preview-Declaration([string]$text){
    $pattern='(?ms)^[\t ]*virtual HRESULT getDimPreviewGraphics\([^{};]+\)\s*\{\s*dimPreviewGeom\.SetSize\(0\);\s*return E_NOTIMPL;\s*\}'
    $matches=[regex]::Matches($text,$pattern)
    if($matches.Count -ne 1){throw 'Expected the one approved unused preview inline body'}
    $method=$matches[0].Value;$open=$method.IndexOf('{');$declaration=$method.Substring(0,$open)+';'
    return [pscustomobject]@{Text=$text.Replace($method,$declaration);Original=$method;Declaration=$declaration;Signature=$method.Substring(0,$open)}
}
function Slice-Classes([string]$name,[string]$text){
    if($name -eq 'IMcStyles.h'){
        # The dimension's complete style and enum declarations are one contiguous
        # vendor region. Uncalled UI/XML/native-gate interfaces are not imported.
        $start=$text.IndexOf('enum MCS_DIMSTYLE_TYPE',[StringComparison]::Ordinal)
        $last='typedef MComQIPtr<IMcDimStyle> IMcDimStylePtr;'
        $end=$text.IndexOf($last,[StringComparison]::Ordinal)
        if($start -lt 0 -or $end -lt $start){throw 'Vendor dimension-style declaration region changed'}
        $license=$text.Substring(0,$text.IndexOf('#pragma once',[StringComparison]::Ordinal))
        $text=$license+'#pragma once'+"`r`n"+'#include "MCSTypes.h"'+"`r`n"+'#include "McsGeometry.h"'+"`r`n"+
            $text.Substring($start,$end+$last.Length-$start)+"`r`n"
    }
    $masked=Mask-Cpp $text;$changes=[Collections.Generic.List[object]]::new();$found=@{}
    foreach($match in [regex]::Matches($masked,'(?m)^(?<kind>struct|class)\s+(?:(?:MC_API_CLASS|M3D_API|MECHCTL_API)\s+)?(?<name>\w+)\b')){
        $type=$match.Groups['name'].Value;$open=$masked.IndexOf('{',$match.Index+$match.Length);$semi=$masked.IndexOf(';',$match.Index+$match.Length)
        if($open -lt 0 -or ($semi -ge 0 -and $semi -lt $open)){continue}
        $depth=1;$end=$open+1;while($depth -gt 0 -and $end -lt $masked.Length){if($masked[$end] -eq '{'){$depth++}elseif($masked[$end] -eq '}'){$depth--};$end++}
        if($depth -ne 0){throw "Unclosed SDK declaration $name::$type"}
        while($end -lt $masked.Length -and [char]::IsWhiteSpace($masked[$end])){$end++}
        if($masked[$end] -ne ';'){throw "Nonstandard SDK declaration $name::$type"};$end++
        $body=$text.Substring($match.Index,$end-$match.Index);$found[$type]=$body
        $drop=($name -eq '3DCore.h' -and $type -notin $coreKeep) -or
            ($name -eq 'IMcObjects.h' -and $type -in 'IMcCdEntity','CBBoxUnit','CBBoxUnits') -or
            ($name -eq 'IMcCutter.h' -and $type -eq 'ShellHatchParam') -or
            ($name -eq 'McsGeometry.h' -and $type -eq 'IMcGeometryBuilder')
        if($drop){
            $changes.Add([pscustomobject]@{Start=$match.Index;End=$end;Text=($match.Groups['kind'].Value+' '+$type+'; // Unused SDK type: no reconstructed layout.')})
            $removed.Add([ordered]@{file=$name;type=$type;sha256=Hash-Text $body})
        }
    }
    if($required.ContainsKey($name)){foreach($type in $required[$name]){
        if(!$found.ContainsKey($type)){throw "Required complete interface missing: $name::$type"}
        $expected=$found[$type];$verbatim=$true
        if($name -eq '3DCore.h' -and $type -eq 'IMcFeatureEntity'){
            $preview=Preview-Declaration $expected;$expected=$preview.Text;$verbatim=$false
            $exceptions.Add([ordered]@{file=$name;type=$type;method='getDimPreviewGraphics';original_method_sha256=Hash-Text $preview.Original;
                declaration_sha256=Hash-Text $preview.Declaration;signature_sha256=Hash-Text $preview.Signature;
                original_method=$preview.Original;generated_declaration=$preview.Declaration;
                approval='Root explicitly authorized only this unused inline body removal; signature/defaults/order/base/data unchanged. No export calls it or constructs an interface.'})
        }
        $definitions.Add([ordered]@{file=$name;type=$type;sha256=Hash-Text $found[$type];generated_sha256=Hash-Text $expected;verbatim=$verbatim})
    }}
    foreach($change in ($changes|Sort-Object Start -Descending)){$text=$text.Remove($change.Start,$change.End-$change.Start).Insert($change.Start,$change.Text)}
    if($name -eq '3DCore.h'){$text=$text.Replace('#include "IUniEntMon.h"','// Unused UI input monitor not included by read-only parameter slice.')}
    if($name -eq '3DCore.h'){$text=(Preview-Declaration $text).Text}
    if($name -eq 'IMcObjects.h'){
        # Both operators belong to the unrelated omitted bounding-box helpers.
        $text=[regex]::Replace($text,'(?ms)^__inline bool operator [<>] \(CBBoxUnitPtr p1, CBBoxUnitPtr p2\) \{[^{}]*\}',
            '// Unused bounding-box helper comparison omitted; no reader call.')
    }
    if($name -eq 'McsGeometry.h'){
        # These omitted geometry implementations are unrelated to the reader.
        # Do not synthesize a layout or methods. All remaining uses are forwards.
        $forwards=@{'McGe/mcsSphere.h'='struct mcsSphere;';'McGe/mcsCylinder.h'='struct mcsCylinder;';'McGe/mcsCone.h'='struct mcsCone;';
            'McGe/mcsTorus.h'='struct mcsTorus;';'McGe/mcsTriangle.h'='struct mcsTriangle;';'mcsText.h'='struct mcsText;';
            'mcsHatch.h'='struct mcsHatch;';'mcsArrow.h'='struct mcsArrow;';'mcsObjGeomRef.h'='struct mcsObjGeomRef;';
            'mcOfsCrvHolder.h'='class mcOfsCrvHolder;';'mcsRepeatedShape.h'='struct mcsRepeatedShape;';'mcsMesh.h'='struct mcsMesh;'}
        foreach($include in $forwards.Keys){$text=$text.Replace('#include "'+$include+'"',$forwards[$include]+' // Unused missing SDK implementation.')}
    }
    return $text
}
function Source-Sha256([string]$path){
    # MSBuild invokes Windows PowerShell, whose inherited module search path
    # need not expose the Get-FileHash function. Hash the same bytes directly.
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($algorithm.ComputeHash([IO.File]::ReadAllBytes($path))).Replace('-','')}
    finally{$algorithm.Dispose()}
}
function Visit([string]$path){
    $path=[IO.Path]::GetFullPath($path);if($records.ContainsKey($path)){return}
    $relative=$path.Substring($sdk.Length+1);$name=[IO.Path]::GetFileName($path)
    if($anchors.ContainsKey($relative) -and (Source-Sha256 $path) -ne $anchors[$relative]){throw "SDK source changed: $relative"}
    $original=$encoding.GetString([IO.File]::ReadAllBytes($path));$text=Slice-Classes $relative $original
    $record=[pscustomobject]@{Path=$path;Relative=$relative;Original=$original;Text=$text;Changed=($original -cne $text);Copy=$false;Children=@()}
    $records[$path]=$record
    foreach($match in [regex]::Matches($text,'(?m)^\s*#\s*include\s+"([^"]+)"')){
        $include=$match.Groups[1].Value;$child=Join-Path (Split-Path -Parent $path) $include
        if(!(Test-Path -LiteralPath $child)){$child=Join-Path $sdk $include}
        if(Test-Path -LiteralPath $child){$child=[IO.Path]::GetFullPath($child);if($child.StartsWith($sdk,[StringComparison]::OrdinalIgnoreCase)){$record.Children+=,$child;Visit $child}}
    }
}
foreach($name in @('IMcObjects.h','IMcDocuments.h','3DCore.h','IMcDimension.h')){Visit (Join-Path $sdk $name)}
do{$changed=$false;foreach($record in @($records.Values)){if(!$record.Copy -and ($record.Changed -or @($record.Children|Where-Object{$records[$_].Copy}).Count -gt 0)){$record.Copy=$true;$changed=$true}}}while($changed)
$expectedHeaders=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($record in @($records.Values|Where-Object Copy)){[void]$expectedHeaders.Add($record.Relative)}
if(Test-Path -LiteralPath $output){foreach($old in Get-ChildItem -LiteralPath $output -Recurse -File){
    if([IO.Path]::GetExtension($old.Name) -ine '.h'){continue}
    $relative=$old.FullName.Substring($output.Length+1)
    if(!$expectedHeaders.Contains($relative)){throw "Unexpected stale private SDK header: $relative. No silent reuse or automatic deletion."}
}}
$files=@(foreach($record in @($records.Values|Where-Object Copy|Sort-Object Relative)){
    $target=Join-Path $output $record.Relative;[IO.Directory]::CreateDirectory((Split-Path -Parent $target))|Out-Null
    [IO.File]::WriteAllBytes($target,$encoding.GetBytes($record.Text))
    [ordered]@{source=$record.Relative;source_sha256=Hash-Text $record.Original;generated_sha256=Hash-Text $record.Text;modified=$record.Changed}
})
# Assert all called class/base definitions survived byte-for-byte. Omitting an
# unrelated top-level declaration must never alter a retained virtual method.
foreach($definition in $definitions){$record=$records[(Join-Path $sdk $definition.file)];$masked=Mask-Cpp $record.Text
    $pattern='(?m)^(struct|class)\s+(?:(?:MC_API_CLASS|M3D_API|MECHCTL_API)\s+)?'+[regex]::Escape($definition.type)+'\b(?=[^{;]*\{)'
    $match=[regex]::Match($masked,$pattern);$open=$masked.IndexOf('{',$match.Index+$match.Length);$depth=1;$end=$open+1
    while($depth -gt 0){if($masked[$end] -eq '{'){$depth++}elseif($masked[$end] -eq '}'){$depth--};$end++}
    while([char]::IsWhiteSpace($masked[$end])){$end++};$end++
    if((Hash-Text $record.Text.Substring($match.Index,$end-$match.Index)) -ne $definition.generated_sha256){throw "Changed called SDK definition: $($definition.type)"}
}
[ordered]@{scope='Generated private SDK parsing slice; no build/load/CAD';vendor='NCadSDK_260_EN_7355.4942.259';
    complete_interface_definitions=@($definitions);approved_unused_inline_exceptions=@($exceptions);forward_only_unused=@($removed);files=$files;
    rule='All called interface/base signatures, virtual order, data members, inheritance and macros are vendor source. Exactly one approved unused inline body is declaration-only; every other retained definition is byte-for-byte vendor source.'
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'manifest412.json') -Encoding utf8
