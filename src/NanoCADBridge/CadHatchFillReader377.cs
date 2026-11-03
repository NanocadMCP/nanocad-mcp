using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using NativeColor=Teigha.Colors.Color;

namespace NanoCADBridge;

// Read the active native fill, never generated hatch lines or evaluated colors.
// Active observations remain available even when a dependency prevents editing.
internal static class CadHatchFillReader377
{
    internal sealed record Snapshot(CadHatchEditingReader.FillBoundary377 Boundary,string Kind,JsonElement Active,string Token);
    internal sealed record ReadValue(string fill_kind,string native_object_type,JsonElement active_fill,string? fill_token,
        string[]? source_handles,string? editing_unavailable_reason,string observation_scope);
    internal static void Need([DoesNotReturnIf(false)]bool value,string message,string code="UNSUPPORTED_HATCH_CONTENT")
    {if(!value)throw new DomainException(code,message);}
    static double Number(double value)=>CadHatchEditingReader.Number(value);
    static float Single(float value)
    {Need(float.IsFinite(value),"Native gradient contains a nonfinite32-bit value.","INVALID_GEOMETRY");return value==0?0:value;}
    static void Bytes(object value,int budget,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length<=budget,"Complete native fill exceeds the actual byte budget; no color or definition was truncated.","RESPONSE_LIMIT_EXCEEDED");}
    internal static string GradientType(GradientPatternType value)=>value switch{
        GradientPatternType.PreDefinedGradient=>"predefined",GradientPatternType.UserDefinedGradient=>"user_defined",_=>value.ToString()};
    internal static string PatternType(HatchPatternType value)=>value switch{
        HatchPatternType.PreDefined=>"predefined",HatchPatternType.UserDefined=>"user_defined",HatchPatternType.CustomDefined=>"custom_defined",_=>value.ToString()};
    static object Color(NativeColor color)=>new{method=color.IsByLayer?"by_layer":color.IsByBlock?"by_block":color.IsByAci?"aci":color.IsByColor?"true_color":color.ColorMethod.ToString(),
        aci=color.IsByAci?(int?)color.ColorIndex:null,rgb=color.IsByColor?new[]{(int)color.Red,(int)color.Green,(int)color.Blue}:null};
    internal static (string Kind,JsonElement Active) Active(Hatch hatch,Action cancel,Transaction? tr=null)
    {
        cancel();Need(hatch.GetType()==typeof(Hatch),"Select an exact native Hatch.","UNSUPPORTED_ENTITY_TYPE");
        if(hatch.HatchObjectType==HatchObjectType.GradientObject)
        {
            Need(hatch.IsGradient,"Native Hatch object type and active gradient flag disagree.","OBJECT_READ_FAILED");
            var stops=hatch.GetGradientColors();Need(stops!=null,"Native gradient color array is unavailable.","OBJECT_READ_FAILED");
            var colors=new List<object>();long bytes=1024;
            try
            {
                foreach(var stop in stops)
                {
                    cancel();var color=stop.get_Color();Need(color!=null,"Native gradient stop color is unavailable.","OBJECT_READ_FAILED");
                    var value=new{value=Single(stop.get_Value()),color=Color(color)};
                    bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length+1;
                    Need(bytes<=8*1024*1024,"Native gradient color array exceeds the actual snapshot-byte budget.","RESPONSE_LIMIT_EXCEEDED");colors.Add(value);
                }
                var active=Wire.Element(new{gradient_type=GradientType(hatch.GradientType),gradient_name=hatch.GradientName,
                    angle=Number(hatch.GradientAngle),shift=Single(hatch.GradientShift),one_color_mode=hatch.GradientOneColorMode,
                    shade_tint=Single(hatch.ShadeTintValue),colors});
                Bytes(active,8*1024*1024,cancel);return("gradient",active);
            }
            finally
            {
                // Installed GetGradientColors allocates copied Color wrappers;
                // GradientColor is a value type; get_Color() returns its stored copy reference.
                foreach(var color in stops.Select(stop=>stop.get_Color()).Where(color=>color!=null).Distinct<NativeColor>(ReferenceEqualityComparer.Instance))color.Dispose();
            }
        }
        Need(hatch.HatchObjectType==HatchObjectType.HatchObject&&!hatch.IsGradient,"Unknown native Hatch fill type.");
        if(hatch.IsSolidFill)return("solid",Wire.Element(new{pattern_name=hatch.PatternName,pattern_type=PatternType(hatch.PatternType)}));
        // Stored read-only pattern getters also work when editing is scoped
        // out by a Field/association; do not hide active fill observations.
        var definitions=new List<object>();int count=hatch.NumberOfPatternDefinitions;long patternBytes=1024;
        Need(count>=0,"Native pattern returned a negative definition count.","OBJECT_READ_FAILED");
        for(int index=0;index<count;index++)
        {
            cancel();var line=hatch.GetPatternDefinitionAt(index);var dashes=line.GetDashes();var values=new List<double>();
            for(int item=0;item<dashes.Count;item++)
            {
                cancel();double number=Number(dashes[item]);patternBytes+=JsonSerializer.SerializeToUtf8Bytes(number,Wire.Json).Length+1;
                Need(patternBytes<=8*1024*1024,"Native pattern exceeds actual snapshot bytes.","RESPONSE_LIMIT_EXCEEDED");values.Add(number);
            }
            var definition=new{angle=Number(line.Angle),base_point=new[]{Number(line.BaseX),Number(line.BaseY)},offset=new[]{Number(line.OffsetX),Number(line.OffsetY)},dashes=values};
            patternBytes+=128;Need(patternBytes<=8*1024*1024,"Native pattern exceeds actual snapshot bytes.","RESPONSE_LIMIT_EXCEEDED");definitions.Add(definition);
        }
        var pattern=Wire.Element(new{pattern_type=PatternType(hatch.PatternType),pattern_name=hatch.PatternName,angle=Number(hatch.PatternAngle),
            scale=Number(hatch.PatternScale),origin_ocs=new[]{Number(hatch.Origin.X),Number(hatch.Origin.Y)},pattern_double=hatch.PatternDouble,spacing=Number(hatch.PatternSpace),definitions});
        Bytes(pattern,8*1024*1024,cancel);return("pattern",pattern);
    }
    internal static Snapshot Capture(Hatch hatch,Database db,Transaction tr,Action cancel,(string Kind,JsonElement Active)? observed=null)
    {
        Need(hatch.Annotative!=AnnotativeStates.True,"Annotative fill changes require their owning context adapter.");
        var boundary=CadHatchEditingReader.CaptureFillBoundary377(hatch,db,tr,cancel);var active=observed??Active(hatch,cancel,tr);
        var state=new{boundary.Identity,boundary.Geometry.Preserved,boundary.Sources,boundary.Origin,boundary.HatchStyle,active.Kind,active.Active};
        Bytes(state,8*1024*1024,cancel);
        return new(boundary,active.Kind,active.Active,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state,Wire.Json))));
    }
    internal static (object? Value,string? Reason) TryRead(Hatch hatch,Action cancel)
    {
        try
        {
            cancel();var active=Active(hatch,cancel);string? token=null,reason=null;string[]? sources=null;
            try
            {
                var db=hatch.Database;Need(db!=null,"Editing requires this Hatch's current resident document.");
                using var tr=db.TransactionManager.StartTransaction();var current=Capture(hatch,db,tr,cancel,active);
                token=current.Token;sources=current.Boundary.Geometry.SourceHandles;
            }
            catch(OperationCanceledException){throw;}
            catch(DomainException error){reason=error.Error.code+": "+error.Error.message;}
            catch(Exception error){reason="Fill editing metadata unavailable: "+error.GetType().Name;}
            var value=new ReadValue(active.Kind,hatch.HatchObjectType.ToString(),active.Active,token,sources,reason,
                "complete_active_native_stored_fill; no_color_evaluation_or_generated_line_read");
            Bytes(value,Wire.PayloadBudget-16384,cancel);return(value,null);
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return(null,error.Error.code+": "+error.Error.message);}
        catch(Exception error){return(null,"Native active fill unavailable: "+error.GetType().Name);}
    }
}
