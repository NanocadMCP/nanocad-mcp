using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using static NanoCADBridge.CadHatchEditingReader;

namespace NanoCADBridge;

// Use the resident hatch's native context for PAT lookup and evaluation. A
// detached clone cannot prove that lookup, and is never an availability gate.
internal static class CadHatchPropertiesEditor
{
    private sealed record Target(string Name,HatchPatternType Type,double Angle,double Scale,Point2d Origin,HatchStyle Style,bool Double,bool Solid,double? Space);
    private static void Token(Snapshot before,SetHatchPropertiesSpec edit)
    {Need(before.Token==edit.HatchToken,"Stored Hatch state differs from hatch_token; refresh inspect_objects geometry.editing.","HATCH_STATE_CHANGED");}
    private static Target Expected(PatternValue before,HatchPropertyChanges edit)
    {
        Need(Enum.TryParse<HatchPatternType>(before.Type,out var type)&&type is HatchPatternType.PreDefined or HatchPatternType.UserDefined or HatchPatternType.CustomDefined,
            "Unknown native pattern type requires its owning adapter.");
        string name=edit.PatternName??before.Name;
        var targetType=edit.PatternName!=null?HatchPatternType.PreDefined:type;
        Need(!edit.PatternSpace.HasValue||targetType==HatchPatternType.UserDefined,
            "pattern_space applies only to the retained native UserDefined pattern; selecting pattern_name chooses PreDefined.","INVALID_ARGUMENT");
        bool solid=edit.PatternName!=null?name.Equals("SOLID",StringComparison.OrdinalIgnoreCase):before.Solid;
        double scale=edit.PatternScale??before.Scale,angle=edit.PatternAngle??before.Angle;
        Need(solid||targetType==HatchPatternType.UserDefined||scale>0,"Changing this hatch to a predefined/custom line pattern requires an explicit positive pattern_scale when its stored scale is not positive.","INVALID_ARGUMENT");
        var origin=edit.OriginOcs is {} point?new Point2d(point.X,point.Y):new Point2d(before.Origin[0],before.Origin[1]);
        string style=edit.HatchStyle??before.Style;
        var nativeStyle=style switch{"normal"=>HatchStyle.Normal,"outer"=>HatchStyle.Outer,"ignore"=>HatchStyle.Ignore,
            _=>throw new DomainException("UNSUPPORTED_HATCH_CONTENT","Unknown native island handling requires its owning adapter.")};
        // Native PatternSpace is the independent user-defined line spacing.
        // Selecting a predefined pattern replaces its whole definition context;
        // retaining a user-defined pattern must retain its spacing as well.
        return new(name,targetType,angle,scale,origin,nativeStyle,edit.PatternDouble??before.Double,solid,
            targetType==HatchPatternType.UserDefined?edit.PatternSpace??before.Space:null);
    }
    internal static Snapshot CheckSource(Hatch hatch,SetHatchPropertiesSpec edit,Database db,Transaction tr,Action cancel)
    {
        var before=Capture(hatch,db,tr,cancel);Token(before,edit);_ = Expected(before.Stored.Pattern,edit.Properties);
        return before;
    }
    private static bool Close(double expected,double actual)=>double.IsFinite(expected)&&double.IsFinite(actual)&&
        Math.Abs(expected-actual)<=1e-12+Math.Abs(expected)*1e-12;
    private static void Verify(Target target,PatternValue actual,HatchPropertyChanges requested)
    {
        Need(string.Equals(target.Name,actual.Name,StringComparison.OrdinalIgnoreCase)&&actual.Type==target.Type.ToString()&&
            (target.Solid&&!requested.PatternAngle.HasValue||Close(target.Angle,actual.Angle))&&
            (target.Solid&&!requested.PatternScale.HasValue||Close(target.Scale,actual.Scale))&&
            (target.Solid&&requested.OriginOcs==null||Close(target.Origin.X,actual.Origin[0])&&Close(target.Origin.Y,actual.Origin[1]))&&
            actual.Style==Style(target.Style)&&(target.Solid&&!requested.PatternDouble.HasValue||actual.Double==target.Double)&&
            actual.Solid==target.Solid&&(!target.Space.HasValue||Close(target.Space.Value,actual.Space)),
            "Resident native Hatch did not retain requested pattern properties or unspecified editable values.","NATIVE_HATCH_MISMATCH");
    }
    private static bool Preserved(JsonElement expected,JsonElement actual,bool metadataPreserved)
    {
        // Geometric scalars may be normalized by native evaluation. Stored
        // metadata/IDs/XData are exact except the classified explicitly set
        // native Origin app field checked against the verified native Origin.
        return BlockMoveGeometry.SameProperties(expected.GetProperty("normal"),actual.GetProperty("normal"))&&
            BlockMoveGeometry.SameProperties(expected.GetProperty("elevation"),actual.GetProperty("elevation"))&&
            BlockMoveGeometry.SameProperties(expected.GetProperty("loops"),actual.GetProperty("loops"))&&
            expected.GetProperty("hatch_object_type").GetRawText()==actual.GetProperty("hatch_object_type").GetRawText()&&
            expected.GetProperty("associative").GetRawText()==actual.GetProperty("associative").GetRawText()&&
            metadataPreserved;
    }
    private sealed record PreservationField(string path,string expected_sha256,string current_sha256,bool different_under_guard);
    private static object PreservationDetails(Snapshot before,Snapshot after,Action cancel,bool expectedNativeOriginXDataChange)
    {
        // Diagnostic only: use completed managed snapshots, no new native getter.
        // A byte budget limits notices, never Hatch admission or loop inventory.
        const int detailBytes=8192;
        try
        {
            var fields=new List<PreservationField>();int observed=0,different=0,bytes=1536;
            void Add(string path,JsonElement? expected,JsonElement? current,bool exact=false)
            {
                cancel();observed++;
                bool changed=expected.HasValue!=current.HasValue||expected.HasValue&&
                    (exact?expected.Value.GetRawText()!=current!.Value.GetRawText():!BlockMoveGeometry.SameProperties(expected.Value,current!.Value));
                if(expectedNativeOriginXDataChange&&(path=="/metadata"||path=="/metadata/xdata"))changed=false;
                if(changed)different++;
                var oldBytes=JsonSerializer.SerializeToUtf8Bytes(expected,Wire.Json);cancel();
                var newBytes=JsonSerializer.SerializeToUtf8Bytes(current,Wire.Json);cancel();
                var value=new PreservationField(path,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(oldBytes)),
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(newBytes)),changed);
                int size=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length+1;
                if((long)bytes+size<=detailBytes){fields.Add(value);bytes+=size;}
            }
            void Properties(string path,JsonElement expected,JsonElement current,bool exact)
            {
                foreach(var property in expected.EnumerateObject())
                {cancel();Add(path+"/"+property.Name,property.Value,current.TryGetProperty(property.Name,out var value)?value:(JsonElement?)null,exact);}
                foreach(var property in current.EnumerateObject())
                {cancel();if(!expected.TryGetProperty(property.Name,out _))Add(path+"/"+property.Name,null,property.Value,exact);}
            }
            var beforeState=before.Stored.Preserved;var afterState=after.Stored.Preserved;
            Add("/identity",Wire.Element(before.Identity),Wire.Element(after.Identity),true);
            Add("/normal",beforeState.GetProperty("normal"),afterState.GetProperty("normal"));
            Add("/elevation",beforeState.GetProperty("elevation"),afterState.GetProperty("elevation"));
            Add("/hatch_object_type",beforeState.GetProperty("hatch_object_type"),afterState.GetProperty("hatch_object_type"),true);
            Add("/associative",beforeState.GetProperty("associative"),afterState.GetProperty("associative"),true);
            Add("/loops",beforeState.GetProperty("loops"),afterState.GetProperty("loops"));
            Add("/metadata",beforeState.GetProperty("metadata"),afterState.GetProperty("metadata"),true);
            Add("/sources",before.Sources,after.Sources);
            var expectedMetadata=beforeState.GetProperty("metadata");var currentMetadata=afterState.GetProperty("metadata");
            Properties("/metadata",expectedMetadata,currentMetadata,true);
            Properties("/metadata/properties",expectedMetadata.GetProperty("properties"),currentMetadata.GetProperty("properties"),true);
            var expectedLoops=beforeState.GetProperty("loops");var currentLoops=afterState.GetProperty("loops");
            int loops=Math.Max(expectedLoops.GetArrayLength(),currentLoops.GetArrayLength());
            for(int index=0;index<loops;index++)
            {
                cancel();JsonElement? first=index<expectedLoops.GetArrayLength()?expectedLoops[index]:null;
                JsonElement? second=index<currentLoops.GetArrayLength()?currentLoops[index]:null;
                if(first.HasValue&&second.HasValue)Properties("/loops/"+index,first.Value,second.Value,false);
                else Add("/loops/"+index,first,second);
            }
            var result=new{diagnostic_scope="complete_captured_state_component_fingerprints_only",
                expected_hatch_token=before.Token,current_hatch_token=after.Token,expected_native_origin_xdata_change=expectedNativeOriginXDataChange,observed_component_count=observed,
                differing_component_count=different,component_fingerprints=fields.ToArray(),omitted_component_count=observed-fields.Count,
                omitted_differing_component_count=different-fields.Count(field=>field.different_under_guard)};
            if(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length>detailBytes)
                return new{diagnostic_unavailable_reason="Complete diagnostic envelope exceeded its byte budget; no partial state comparison is claimed."};
            return result;
        }
        catch(OperationCanceledException){throw;}
        catch(System.Exception error)
        {return new{diagnostic_unavailable_reason="Captured-state diagnostics unavailable: "+error.GetType().Name};}
    }
    internal static void Apply(Hatch hatch,SetHatchPropertiesSpec edit,Database db,Transaction tr,Action cancel,bool evaluate,bool checkToken=true)
    {
        // Source-only validation has already inspected the resident object.
        // Detached Hatch pattern setters have unproven Database/PAT context;
        // do not turn their failure/difference into a resident feature refusal.
        if(!evaluate){cancel();return;}
        var before=Capture(hatch,db,tr,cancel);if(checkToken)Token(before,edit);
        var expected=Expected(before.Stored.Pattern,edit.Properties);cancel();
        bool nativeOriginSetterExecuted=false;
        bool sameDefinitionContext=string.Equals(expected.Name,before.Stored.Pattern.Name,StringComparison.OrdinalIgnoreCase)&&
            expected.Type.ToString()==before.Stored.Pattern.Type&&Close(expected.Angle,before.Stored.Pattern.Angle)&&Close(expected.Scale,before.Stored.Pattern.Scale)&&
            Close(expected.Origin.X,before.Stored.Pattern.Origin[0])&&Close(expected.Origin.Y,before.Stored.Pattern.Origin[1])&&expected.Double==before.Stored.Pattern.Double&&
            (!expected.Space.HasValue||Close(expected.Space.Value,before.Stored.Pattern.Space));
        if(edit.Properties.PatternName!=null&&
            (!string.Equals(expected.Name,before.Stored.Pattern.Name,StringComparison.OrdinalIgnoreCase)||before.Stored.Pattern.Type!=expected.Type.ToString()))
        {
            hatch.SetHatchPattern(expected.Type,expected.Name);cancel();
            // SetHatchPattern can replace its pattern definition/defaults.
            // Restore unspecified editable values explicitly, preserving the
            // user's stored choices rather than native reset defaults.
            if(expected.Scale>0&&(!expected.Solid||edit.Properties.PatternScale.HasValue))hatch.PatternScale=expected.Scale;
            if(!expected.Solid||edit.Properties.PatternAngle.HasValue)hatch.PatternAngle=expected.Angle;
            if(!expected.Solid||edit.Properties.PatternDouble.HasValue)hatch.PatternDouble=expected.Double;
            if(!expected.Solid||edit.Properties.OriginOcs!=null)
            {hatch.Origin=expected.Origin;nativeOriginSetterExecuted=true;}
            hatch.HatchStyle=expected.Style;
        }
        else
        {
            if(edit.Properties.PatternScale.HasValue&&!Close(expected.Scale,before.Stored.Pattern.Scale))hatch.PatternScale=expected.Scale;
            if(edit.Properties.PatternAngle.HasValue&&!Close(expected.Angle,before.Stored.Pattern.Angle))hatch.PatternAngle=expected.Angle;
            if(edit.Properties.PatternDouble.HasValue&&expected.Double!=before.Stored.Pattern.Double)hatch.PatternDouble=expected.Double;
            if(edit.Properties.OriginOcs!=null&&(!Close(expected.Origin.X,before.Stored.Pattern.Origin[0])||!Close(expected.Origin.Y,before.Stored.Pattern.Origin[1])))
            {hatch.Origin=expected.Origin;nativeOriginSetterExecuted=true;}
            if(edit.Properties.HatchStyle!=null&&Style(expected.Style)!=before.Stored.Pattern.Style)hatch.HatchStyle=expected.Style;
        }
        if(expected.Space.HasValue&&!Close(expected.Space.Value,hatch.PatternSpace))hatch.PatternSpace=expected.Space.Value;
        cancel();hatch.EvaluateHatch(true);cancel();
        var after=Capture(hatch,db,tr,cancel);Verify(expected,after.Stored.Pattern,edit.Properties);
        if(sameDefinitionContext)Need(BlockMoveGeometry.SameProperties(before.Stored.Definitions,after.Stored.Definitions),
            "Native Hatch changed an unedited stored pattern definition.","NATIVE_HATCH_MISMATCH");
        var beforeMetadata=before.Stored.Preserved.GetProperty("metadata");
        var currentMetadata=after.Stored.Preserved.GetProperty("metadata");
        bool metadataPreserved=CadHatchOriginXData348.MetadataPreserved(beforeMetadata,currentMetadata,
            before.Stored.Pattern.Origin,after.Stored.Pattern.Origin,nativeOriginSetterExecuted,cancel);
        bool expectedNativeOriginXDataChange=metadataPreserved&&beforeMetadata.GetRawText()!=currentMetadata.GetRawText();
        if(!(before.Identity==after.Identity&&Preserved(before.Stored.Preserved,after.Stored.Preserved,metadataPreserved)&&
            // Boundary source geometry may have native float roundoff; exact
            // identity/reactor/metadata checks are performed below separately.
            BlockMoveGeometry.SameProperties(before.Sources,after.Sources)))
            throw new DomainException("NATIVE_HATCH_MISMATCH",
                "Native Hatch evaluation changed its identity, stored loops, plane, display values or associative sources.",false,
                PreservationDetails(before,after,cancel,expectedNativeOriginXDataChange));
        Need(before.Sources.GetArrayLength()==after.Sources.GetArrayLength(),"Associative native boundary inventory changed.","NATIVE_HATCH_MISMATCH");
        foreach(var pair in before.Sources.EnumerateArray().Zip(after.Sources.EnumerateArray()))
        {
            cancel();Need(pair.First.GetProperty("handle").GetRawText()==pair.Second.GetProperty("handle").GetRawText()&&
                pair.First.GetProperty("owner_handle").GetRawText()==pair.Second.GetProperty("owner_handle").GetRawText()&&
                pair.First.GetProperty("runtime_type").GetRawText()==pair.Second.GetProperty("runtime_type").GetRawText()&&
                pair.First.GetProperty("metadata").GetRawText()==pair.Second.GetProperty("metadata").GetRawText(),
                "Native Hatch evaluation changed associative source identities or stored metadata/reactors.","NATIVE_HATCH_MISMATCH");
        }
        // No Area/bbox/generated-line oracle and no speculative density gate:
        // island style legitimately changes filled area while retaining loops.
    }
}
