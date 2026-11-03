using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record NativeSketchPrimitive385(string Key,string Kind,Point3[] Points,double? Radius,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] NativeSketchSpline445? Spline=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] NativeSketchEllipse445? Ellipse=null);
public sealed record NativeSketchInitialization385(NativeSketchPrimitive385[] Geometry,SketchConstraintSpec[] Constraints,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string[]? ReferenceIds=null);
public sealed record NativeSketchMember385(string member_id,string object_id,string native_type,int? segment_index,
    JsonElement geometry,bool independently_owned,string? unavailable_reason,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool reference_only=false);
public sealed record NativeSketchBindings385(bool complete,string? unavailable_reason,string binding_token,NativeSketchMember385[] members,
    string identity_scope,string scope);
public sealed record NativeSketchCreated385(string key,string object_id,string[] member_ids);

public static class NativeSketchValidation385
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement value,string field,string path)
    {if(!value.TryGetProperty(field,out var v)||v.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(v.GetString()))throw Invalid(path+"."+field,"Expected a nonempty string.");return v.GetString()!;}
    public static NativeSketchInitialization385 Parse(JsonElement value,JsonElement constraints,SketchPlaneSpec? plane,string path,string[]? referenceIds=null)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()==0)throw Invalid(path,"Supply native sketch geometry.");
        var result=new List<NativeSketchPrimitive385>();var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in value.EnumerateArray())
        {
            string p=path+"["+result.Count+"]";if(row.ValueKind!=JsonValueKind.Object)throw Invalid(p,"Expected a geometry object.");
            var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var field in row.EnumerateObject())if(!seen.Add(field.Name))throw Invalid(p,"Duplicate geometry field.");
            string key=Text(row,"key",p),kind=Text(row,"kind",p);
            if(key.Length>64||key.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'&&c!='-')||!keys.Add(key))throw Invalid(p,"Use distinct bounded input keys.");
            if(kind is "spline" or "ellipse")
            {result.Add(NativeSketchCurveValidation445.Parse(row,key,kind,plane,p));continue;}
            foreach(var field in row.EnumerateObject())if(field.Name is not ("key" or "kind" or "points" or "radius"))throw Invalid(p,"Unknown geometry field.");
            int pointCount=kind switch{"line"=>2,"circle"=>1,"arc"=>3,_=>0};
            if(pointCount==0||!row.TryGetProperty("points",out var points)||points.ValueKind!=JsonValueKind.Array||points.GetArrayLength()!=pointCount)throw Invalid(p,"Line uses two WCS endpoints; Circle uses its WCS center; Arc uses ordered start, through and end WCS points.");
            var coords=new List<Point3>();foreach(var point in points.EnumerateArray())
            {
                if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=3)throw Invalid(p,"Use three finite WCS coordinates per point.");
                var a=point.EnumerateArray().Select(x=>x.ValueKind==JsonValueKind.Number&&x.TryGetDouble(out double n)&&double.IsFinite(n)&&Math.Abs(n)<=1e6?n:throw Invalid(p,"Use finite WCS coordinates within drawing range.")).ToArray();
                var q=new Point3(a[0],a[1],a[2]);if(plane!=null)DrawingWorkplane.Local(plane,q);coords.Add(q);
            }
            double? radius=null;if(kind=="circle")
            {if(!row.TryGetProperty("radius",out var r)||r.ValueKind!=JsonValueKind.Number||!r.TryGetDouble(out double n)||!double.IsFinite(n)||n<=0||n>1e6)throw Invalid(p,"Use a positive finite radius in millimeters.");radius=n;}
            else
            {
                if(row.TryGetProperty("radius",out _))throw Invalid(p,"Only Circle supplies radius; Arc radius follows its three explicit points.");
                if(kind=="line"&&Math.Sqrt(Math.Pow(coords[0].X-coords[1].X,2)+Math.Pow(coords[0].Y-coords[1].Y,2)+Math.Pow(coords[0].Z-coords[1].Z,2))<=1e-7)throw Invalid(p,"Use a nondegenerate Line.");
                if(kind=="arc"&&plane!=null)NativeSketchArcGeometry390.Curve(coords.ToArray(),plane,p);
            }
            result.Add(new(key,kind,coords.ToArray(),radius));
        }
        var references=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string reference in referenceIds??[])
        {
            if(!Guid.TryParseExact(reference,"B",out var id)||id==Guid.Empty||!references.Add(id.ToString("B").ToUpperInvariant()))
                throw Invalid(path,"Initial projected operands require distinct exact source GUIDs from reference_ids.");
        }
        string Operand(string identity)
        {
            if(keys.Contains(identity))return identity;
            if(references.Contains(identity))return Guid.ParseExact(identity,"B").ToString("B").ToUpperInvariant();
            throw Invalid(path,"Initial operands use own native_geometry keys or exact source GUIDs supplied in reference_ids; no invented projection alias.");
        }
        var specs=SketchConstraintValidation385.Parse(constraints,path+".initial_constraints",false)
            .Select(spec=>spec with{First=Operand(spec.First),Second=spec.Second==null?null:Operand(spec.Second),Third=spec.Third==null?null:Operand(spec.Third)}).ToArray();
        var distinct=new HashSet<string>(StringComparer.Ordinal);
        foreach(var spec in specs)
        {
            if(spec.Second!=null&&spec.First==spec.Second||spec.Third!=null&&(spec.Third==spec.First||spec.Third==spec.Second))throw Invalid(path,"Select distinct initial operands, including a separate symmetry axis.");
            if(!keys.Contains(spec.First)&&(spec.Second==null||!keys.Contains(spec.Second)))
                throw Invalid(path,"At least one initial relation operand must be own native_geometry; projected references are not driven directly.");
            if(!distinct.Add(SketchConstraintValidation385.BindingKey(spec)))throw Invalid(path,"Duplicate initial relation binding after native reference normalization; keep one control for each binding.");
        }
        return new(result.ToArray(),specs,referenceIds==null?null:references.ToArray());
    }
    // Supported requests have no guessed plane in the normalized contract.
    // Validate their geometry against the observed native support before any
    // setter, and again against the actual sketch frame before initialization.
    public static void RequirePlane(NativeSketchInitialization385 value,SketchPlaneSpec plane,string path)
    {
        foreach(var row in value.Geometry)
        {
            foreach(var point in row.Points)DrawingWorkplane.Local(plane,point);
            if(row.Kind=="arc")NativeSketchArcGeometry390.Curve(row.Points,plane,path+"."+row.Key);
            if(row.Kind is "spline" or "ellipse")NativeSketchCurveValidation445.RequirePlane(row,plane,path+"."+row.Key);
        }
    }
}

public static class SketchConstraintValidation385
{
    public static readonly string[] Kinds=["horizontal","vertical","parallel","perpendicular","collinear","tangent","smooth","equal","concentric","coincident","fix_point","fix","symmetric","radius","diameter","length","distance","distance_x","distance_y","angle"];
    public static bool Dimensional(string kind)=>kind is "radius" or "diameter" or "length" or "distance" or "distance_x" or "distance_y" or "angle";
    // A second literal does not make a second control on the same binding
    // independent. Normalize only inherently symmetric geometric operands;
    // directed dimensional operands retain their caller order and selectors.
    // This detects duplicate requests, not general redundancy or solver DOF.
    internal static string BindingKey(SketchConstraintSpec spec)
    {
        var key=spec with{Value=null};
        if(spec.Second!=null&&spec.Kind is "parallel" or "perpendicular" or "collinear" or "tangent" or "smooth" or "equal" or "concentric" or "coincident" or "symmetric")
        {
            int order=StringComparer.Ordinal.Compare(spec.First,spec.Second);
            if(order>0||order==0&&StringComparer.Ordinal.Compare(spec.FirstPoint,spec.SecondPoint)>0)
                key=key with{First=spec.Second,Second=spec.First,FirstPoint=spec.SecondPoint,SecondPoint=spec.FirstPoint,
                    FirstSegmentIndex=spec.SecondSegmentIndex,SecondSegmentIndex=spec.FirstSegmentIndex};
        }
        return JsonSerializer.Serialize(key);
    }
    public static SketchConstraintSpec[] Parse(JsonElement array,string path,bool nativeIds=true)
    {
        DomainException Invalid(string message)=>new("INVALID_ARGUMENT",message,false,new{path});
        if(array.ValueKind!=JsonValueKind.Array)throw Invalid("Expected a constraints array.");var result=new List<SketchConstraintSpec>();var unique=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in array.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object)throw Invalid("Expected a constraint object.");var seen=new HashSet<string>();
            foreach(var field in row.EnumerateObject())if(!seen.Add(field.Name)||field.Name is not ("kind" or "first" or "second" or "third" or "first_point" or "second_point" or "value"))throw Invalid("Unknown or duplicate constraint field.");
            string Text(string name){if(!row.TryGetProperty(name,out var v)||v.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(v.GetString()))throw Invalid("Missing "+name+".");return v.GetString()!;}
            string Identity(string name){string t=Text(name);if(!nativeIds){if(t.Length>64)throw Invalid("Input key is too long.");return t;}if(!Guid.TryParseExact(t,"B",out var id)||id==Guid.Empty)throw Invalid("Read the exact current native member ID.");return id.ToString("B").ToUpperInvariant();}
            string kind=Text("kind"),first=Identity("first");if(!Kinds.Contains(kind))throw Invalid("Unsupported constraint kind.");
            bool pair=kind is "parallel" or "perpendicular" or "collinear" or "tangent" or "smooth" or "equal" or "concentric" or "coincident" or "distance" or "distance_x" or "distance_y" or "angle" or "symmetric";
            string? second=pair?Identity("second"):null;if(pair&&first==second)throw Invalid("Select distinct members.");if(!pair&&row.TryGetProperty("second",out _))throw Invalid("This relation uses one member.");
            string? third=kind=="symmetric"?Identity("third"):null;
            if(third!=null&&(third==first||third==second))throw Invalid("The symmetry axis must be a separate actual Line member.");
            if(kind!="symmetric"&&row.TryGetProperty("third",out _))throw Invalid("Only symmetry uses a third member for its actual Line axis.");
            bool points=kind is "coincident" or "smooth" or "fix_point" or "distance" or "distance_x" or "distance_y";string? a=points?Text("first_point"):null,b=points&&pair?Text("second_point"):null;
            if(a!=null&&a is not ("start" or "end" or "center")||b!=null&&b is not ("start" or "end" or "center"))throw Invalid("Use an actual Line/Arc endpoint or Circle/Arc center.");
            if(kind=="smooth"&&(a=="center"||b=="center"))throw Invalid("Smooth binds actual start/end endpoints; one operand must be a native Spline.");
            if(!points&&(row.TryGetProperty("first_point",out _)||row.TryGetProperty("second_point",out _))||!pair&&row.TryGetProperty("second_point",out _))throw Invalid("Point references do not apply to this relation.");
            double? value=null;if(Dimensional(kind))
            {if(!row.TryGetProperty("value",out var v)||v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out double n)||!double.IsFinite(n)||n<=0||kind=="angle"&&n>=Math.PI)throw Invalid("Use a positive finite millimeter control, or an angle strictly between zero and pi radians.");value=n;}
            else if(row.TryGetProperty("value",out _))throw Invalid("Geometric relations have no control value.");
            var spec=new SketchConstraintSpec(kind,first,second,a,b,value,third);if(!unique.Add(BindingKey(spec)))throw Invalid("Duplicate requested relation binding; keep one control for each binding and edit its existing value.");result.Add(spec);
        }
        return result.ToArray();
    }
}
