using System.Text;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record DimensionDisplayEdit341(string[] Targets,JsonElement Properties):EditSpec("set_dimension_display",Targets);
public sealed record DimensionDefinitionEdit341(string[] Targets,string DimensionKind,JsonElement ExpectedDefinition,JsonElement Definition)
    :EditSpec("set_dimension_definition",Targets);

// Native effective display fields. Values do not change drawing units or a
// driving source. Enum ranges come from the installed SDK's IMcStyles.h and
// McDimensionDataMapVariables.h, rather than a guessed native write.
public static class DimensionEditingValidation341
{
    public static readonly string[] DisplayNames={"linear_factor","linear_unit_format","angular_unit_format","rounding",
        "linear_zero_suppression","angular_zero_suppression","primary_prefix","primary_suffix",
        "alternate_enabled","alternate_factor","alternate_unit_format","alternate_precision","alternate_rounding",
        "alternate_zero_suppression","alternate_prefix","alternate_suffix","extension_offset","extension_beyond",
        "text_gap","suppress_extension1","suppress_extension2","suppress_dimension1","suppress_dimension2"};
    public static readonly string[] Kinds={"AlignedDimension","RotatedDimension","RadialDimension","DiametricDimension",
        "LineAngularDimension2","Point3AngularDimension","OrdinateDimension","ArcDimension","RadialDimensionLarge"};
    public static bool Handles(string operation)=>operation is "set_dimension_display" or "set_dimension_definition";
    // Bind expected readback to the actual native value type, not the JSON
    // spelling (0.0 / 0e0 / 0 all request the same native double).
    public static object NativeDisplayValue(string name,JsonElement value)=>name switch
    {
        "linear_unit_format" or "angular_unit_format" or "alternate_unit_format" or
        "linear_zero_suppression" or "angular_zero_suppression" or "alternate_zero_suppression" or "alternate_precision"=>value.GetInt32(),
        "alternate_enabled" or "suppress_extension1" or "suppress_extension2" or "suppress_dimension1" or "suppress_dimension2"=>value.GetBoolean(),
        "linear_factor" or "rounding" or "alternate_factor" or "alternate_rounding" or "extension_offset" or "extension_beyond" or "text_gap"=>value.GetDouble(),
        _=>throw new DomainException("INVALID_ARGUMENT","Unknown native scalar dimension display field.")
    };
    private static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static JsonElement Required(JsonElement v,string name,string path)=>v.TryGetProperty(name,out var found)?found:throw Invalid(path+"."+name,"Required field.");
    private static void Shape(JsonElement v,string path,params string[] names)
    {
        if(v.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in v.EnumerateObject())if(!seen.Add(p.Name)||!names.Contains(p.Name,StringComparer.Ordinal))throw Invalid(path+"."+p.Name,"Unknown or duplicate field.");
    }
    private static string Text(JsonElement v,string path,bool literal=false)
    {
        if(v.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected string.");
        string text;try{text=v.GetString()!;new UTF8Encoding(false,true).GetByteCount(text);}catch(Exception e)when(e is InvalidOperationException or EncoderFallbackException){throw Invalid(path,"Invalid Unicode scalar text.");}
        if(text.Any(char.IsControl)||literal&&(text.IndexOfAny(new[]{'\\','{','}','^','%'})>=0||text.Contains("<>")||text.Contains("[]")))
            throw Invalid(path,"Use literal visible affixes, without native formatting/field/measurement codes.");
        return text;
    }
    private static double Number(JsonElement v,string path,int sign=0)
    {
        if(v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out var n)||!double.IsFinite(n)||Math.Abs(n)>1e9||sign==1&&n<0||sign==2&&n<=0)
            throw Invalid(path,"Expected finite native value within +/-1e9 with the declared sign.");
        return n;
    }
    private static int Integer(JsonElement v,string path,int min,int max)=>v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out var n)&&n>=min&&n<=max?n:throw Invalid(path,$"Expected native integer from{min} to{max}.");
    private static void Boolean(JsonElement v,string path){if(v.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Invalid(path,"Expected boolean.");}
    public static EditSpec Parse(JsonElement input,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Address one exact existing dimension per operation.");
        string operation=Text(Required(input,"operation",path),path+".operation");
        if(operation=="set_dimension_display")
        {
            Shape(input,path,"operation","handles","properties");var p=Required(input,"properties",path);string at=path+".properties";Shape(p,at,DisplayNames);
            if(!p.EnumerateObject().Any())throw Invalid(at,"Request at least one display field.");
            foreach(var field in p.EnumerateObject())
            {
                string pp=at+"."+field.Name;var value=field.Value;
                switch(field.Name)
                {
                    case "linear_unit_format":Integer(value,pp,1,6);break;
                    case "angular_unit_format":Integer(value,pp,0,4);break;
                    case "alternate_unit_format":Integer(value,pp,1,8);break;
                    case "linear_zero_suppression":case "alternate_zero_suppression":Integer(value,pp,0,15);break;
                    case "angular_zero_suppression":Integer(value,pp,0,3);break;
                    case "alternate_precision":Integer(value,pp,0,8);break;
                    case "primary_prefix":case "primary_suffix":case "alternate_prefix":case "alternate_suffix":Text(value,pp,true);break;
                    case "alternate_enabled":case "suppress_extension1":case "suppress_extension2":case "suppress_dimension1":case "suppress_dimension2":Boolean(value,pp);break;
                    case "alternate_factor":Number(value,pp,2);break;
                    case "rounding":case "alternate_rounding":case "extension_offset":case "extension_beyond":Number(value,pp,1);break;
                    default:Number(value,pp);break;
                }
            }
            foreach(string group in new[]{"primary","alternate"})if(p.TryGetProperty(group+"_prefix",out _)!=p.TryGetProperty(group+"_suffix",out _))throw Invalid(at,"Supply both literal prefix and suffix together; the whole native measurement template is replaced.");
            return new DimensionDisplayEdit341(handles,p.Clone());
        }
        if(operation!="set_dimension_definition")throw Invalid(path+".operation","Unknown dimension operation.");
        Shape(input,path,"operation","handles","dimension_kind","expected_definition","definition");
        string kind=Text(Required(input,"dimension_kind",path),path+".dimension_kind");if(!Kinds.Contains(kind,StringComparer.Ordinal))throw Invalid(path+".dimension_kind","Use the exact native dimension_kind from inspect_objects.");
        var expected=Required(input,"expected_definition",path);var definition=Required(input,"definition",path);
        ValidateDefinition(kind,expected,path+".expected_definition");ValidateDefinition(kind,definition,path+".definition");
        return new DimensionDefinitionEdit341(handles,kind,expected.Clone(),definition.Clone());
    }
    public static string[] DefinitionNames(string kind)=>kind switch
    {
        "AlignedDimension"=>new[]{"xline1_point","xline2_point","dimline_point"},
        "RotatedDimension"=>new[]{"xline1_point","xline2_point","dimline_point","rotation"},
        "RadialDimension"=>new[]{"center","chord_point","leader_length"},
        "DiametricDimension"=>new[]{"chord_point","far_chord_point","leader_length"},
        "LineAngularDimension2"=>new[]{"xline1_start","xline1_end","xline2_start","xline2_end","arc_point"},
        "Point3AngularDimension"=>new[]{"center_point","xline1_point","xline2_point","arc_point"},
        "ArcDimension"=>new[]{"center_point","xline1_point","xline2_point","arc_point"},
        "RadialDimensionLarge"=>new[]{"center","chord_point","override_center","jog_point","jog_angle"},
        "OrdinateDimension"=>new[]{"origin","defining_point","leader_end_point","using_x_axis"},
        _=>throw Invalid("dimension_kind","Unsupported kind.")
    };
    public static void ValidateDefinition(string kind,JsonElement value,string path)
    {
        var names=DefinitionNames(kind);Shape(value,path,names);
        foreach(string name in names)
        {
            var v=Required(value,name,path);string at=path+"."+name;
            if(name=="using_x_axis")Boolean(v,at);
            else if(name is "rotation" or "jog_angle")Number(v,at);
            else if(name=="leader_length")Number(v,at,1);
            else
            {
                if(v.ValueKind!=JsonValueKind.Array||v.GetArrayLength()!=3)throw Invalid(at,"Use a full WCS XYZ point from the stored definition.");
                for(int i=0;i<3;i++)Number(v[i],at+"["+i+"]");
                if(v[2].GetDouble()!=0)throw Invalid(at,"Definition editing currently uses the retained WCS XY/Z0 plane.");
            }
        }
        // A native angular definition needs two actual directions; no analytical
        // measurement/volume oracle is imposed on its eventual native result.
        double[] P(string name)=>value.GetProperty(name).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        bool Equal(double[] a,double[] b)=>a.Zip(b).All(p=>p.First==p.Second);
        Point2 Q(string name){var p=value.GetProperty(name);return new(p[0].GetDouble(),p[1].GetDouble());}
        if(kind=="ArcDimension")ExtendedAnnotationValidation356.ArcGeometry(Q("center_point"),Q("xline1_point"),Q("xline2_point"),Q("arc_point"),path);
        if(kind=="RadialDimensionLarge")ExtendedAnnotationValidation356.JogGeometry(Q("center"),Q("chord_point"),Q("override_center"),Q("jog_point"),value.GetProperty("jog_angle").GetDouble(),path);
        if(kind=="LineAngularDimension2"&&(Equal(P("xline1_start"),P("xline1_end"))||Equal(P("xline2_start"),P("xline2_end"))))throw Invalid(path,"Angular extension lines need nonzero directions.");
        if(kind=="Point3AngularDimension"&&(Equal(P("center_point"),P("xline1_point"))||Equal(P("center_point"),P("xline2_point"))))throw Invalid(path,"Angular reference rays need nonzero directions.");
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {
        foreach(var edit in operations.Where(e=>e is DimensionDisplayEdit341 or DimensionDefinitionEdit341))
            if(operations.Count(other=>other.Handles.Intersect(edit.Handles,StringComparer.Ordinal).Any())!=1)
                throw new DomainException("INVALID_OPERATION_ORDER","A dimension display/definition target must have only its own operation in this request; inspect again before a dependent edit.");
    }
    public static bool SameDefinition(JsonElement expected,JsonElement actual)
    {
        if(expected.ValueKind!=JsonValueKind.Object||actual.ValueKind!=JsonValueKind.Object||expected.EnumerateObject().Count()!=actual.EnumerateObject().Count())return false;
        bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-8;
        bool Same(JsonElement a,JsonElement b)
        {
            if(a.ValueKind!=b.ValueKind)return false;
            if(a.ValueKind==JsonValueKind.Array)return a.GetArrayLength()==b.GetArrayLength()&&a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>Same(p.First,p.Second));
            if(a.ValueKind==JsonValueKind.Number)return Near(a.GetDouble(),b.GetDouble());
            return a.GetRawText()==b.GetRawText();
        }
        foreach(var p in expected.EnumerateObject())
        {
            if(!actual.TryGetProperty(p.Name,out var value))return false;
            if(p.Name=="rotation")
            {double delta=value.GetDouble()-p.Value.GetDouble();if(!Near(Math.Sin(delta),0)||Math.Cos(delta)<=0)return false;}
            else if(p.Name is not("dimline_point" or "arc_point")&&!Same(p.Value,value))return false;
        }
        if(expected.TryGetProperty("dimline_point",out var line))
        {
            var next=actual.GetProperty("dimline_point");if(Same(line,next))return true;
            if(!Near(line[2].GetDouble(),next[2].GetDouble()))return false;
            double ux,uy;
            if(expected.TryGetProperty("rotation",out var rotation)){ux=Math.Cos(rotation.GetDouble());uy=Math.Sin(rotation.GetDouble());}
            else{var a=expected.GetProperty("xline1_point");var b=expected.GetProperty("xline2_point");ux=b[0].GetDouble()-a[0].GetDouble();uy=b[1].GetDouble()-a[1].GetDouble();double length=Math.Sqrt(ux*ux+uy*uy);if(length==0)return false;ux/=length;uy/=length;}
            return Math.Abs((next[0].GetDouble()-line[0].GetDouble())*uy-(next[1].GetDouble()-line[1].GetDouble())*ux)<=1e-7;
        }
        if(!expected.TryGetProperty("arc_point",out var arc))return true;
        var newArc=actual.GetProperty("arc_point");if(Same(arc,newArc))return true;
        if(!Near(arc[2].GetDouble(),newArc[2].GetDouble()))return false;
        double[] P(string name)=>expected.GetProperty(name).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        double cx,cy,x1,y1,x2,y2;
        if(expected.TryGetProperty("center_point",out var center))
        {cx=center[0].GetDouble();cy=center[1].GetDouble();var a=P("xline1_point");var b=P("xline2_point");x1=a[0]-cx;y1=a[1]-cy;x2=b[0]-cx;y2=b[1]-cy;}
        else
        {
            var a=P("xline1_start");var b=P("xline1_end");var c=P("xline2_start");var d=P("xline2_end");
            x1=b[0]-a[0];y1=b[1]-a[1];x2=d[0]-c[0];y2=d[1]-c[1];double cross=x1*y2-y1*x2;
            if(cross==0)return false;double t=((c[0]-a[0])*y2-(c[1]-a[1])*x2)/cross;cx=a[0]+t*x1;cy=a[1]+t*y1;
        }
        double ax=arc[0].GetDouble()-cx,ay=arc[1].GetDouble()-cy,bx=newArc[0].GetDouble()-cx,by=newArc[1].GetDouble()-cy;
        double radius=Math.Sqrt(ax*ax+ay*ay),nextRadius=Math.Sqrt(bx*bx+by*by);
        bool Sector(double x,double y)=>Math.Sign(x*ay-y*ax)==Math.Sign(x*by-y*bx);
        return double.IsFinite(radius)&&double.IsFinite(nextRadius)&&Math.Abs(radius-nextRadius)<=1e-7&&Sector(x1,y1)&&Sector(x2,y2);
    }
}
