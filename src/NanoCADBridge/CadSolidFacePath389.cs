using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// A retained native path is read-only. Only its independent geometry copy is
// passed to the native face operation; the existing resident body is mutated.
internal static class CadSolidFacePath389
{
    internal const string PrivateDataWarning="FACE_PATH_SOURCE_DATA_UNVERIFIED: Original path identity, geometry, appearance and observed root metadata remain; opaque owned private payloads are not deeply verified. The operation creates no persistent path association.";
    internal sealed record State(JsonElement Shape,JsonElement Metadata,JsonElement Display,bool HasExtension);
    private static void Need(bool ok,string code,string text)
    {if(!ok)throw new DomainException(code,text);}
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static JsonElement Shape(Curve value,Action cancel)
        =>CadRetainedSolidPath392.StableShape(value,cancel);
    internal static Curve CheckPath(Entity source,Database db,ObjectId model,Action cancel,string purpose="Face extrusion")
    {
        cancel();var id=source.ObjectId;var sourceDatabase=source.Database;
        Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased&&source.OwnerId==model&&
            sourceDatabase!=null&&sourceDatabase.UnmanagedObject==db.UnmanagedObject&&source.UnmanagedObject!=IntPtr.Zero,
            "OUT_OF_SCOPE",purpose+" needs a committed native curve in the same current Model Space.");
        Need(source is Curve c&&CadRetainedSolidPath392.Supported(c),"UNSUPPORTED_ENTITY_TYPE",
            "Use an exact native Line/Arc/Circle/Polyline/Ellipse/Spline/Helix path; no custom geometry or nested block source is inferred.");
        var curve=(Curve)source;var shape=Shape(curve,cancel);
        Need(shape.GetProperty("end_parameter").GetDouble()>shape.GetProperty("start_parameter").GetDouble(),
            "INVALID_GEOMETRY","The current native path has no positive parameter span.");
        return curve;
    }
    internal static State Capture(Curve source,Action cancel)
    {
        var budget=new CadContourBudget344(cancel);using var data=source.XData;using var links=source.GetPersistentReactorIds();
        var reactors=new List<string?>();foreach(ObjectId link in links){cancel();var value=Id(link);budget.Add(value!);reactors.Add(value);}
        var shape=Shape(source,cancel);var metadata=Wire.Element(new{handle=Id(source.ObjectId),owner_handle=Id(source.OwnerId),
            runtime_type=source.GetType().FullName,source.HasFields,extension_handle=Id(source.ExtensionDictionary),
            xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),reactors=reactors.ToArray()});
        var display=Wire.Element(new{layer_handle=Id(source.LayerId),properties=EntityAdapter.StoredProperties(source)});
        CadContourBudget344.Check(new{shape,metadata,display},cancel);return new(shape,metadata,display,!source.ExtensionDictionary.IsNull);
    }
    internal static void Preflight(SolidFaceEditSpec spec,Solid3d body,EntityRef reference,Entity source,Database db,ObjectId model,Action cancel)
    {
        _=CadSolidFaceEditor.Validate(spec,body,reference,cancel);
        var path=CheckPath(source,db,model,cancel);
        CadRetainedSolidPath392.RequireCurrentToken(path,spec.PathToken,cancel);
        _=Capture(path,cancel);
    }
    internal static void Apply(SolidFaceEditSpec spec,Solid3d body,EntityRef reference,Entity source,ObjectId model,
        Database db,Action cancel,List<string>? warnings)
    {
        // The transaction owns the resident wrappers. Keep them ForRead and
        // release only the separately adopted detached copy after the call.
        var path=CheckPath(source,db,model,cancel);
        CadRetainedSolidPath392.RequireCurrentToken(path,spec.PathToken,cancel);
        var before=Capture(path,cancel);
        using var copy=(Curve)CadContourBudget344.Detached(path);
        CadCurveCopyGeometry417.Require(before.Shape,Shape(copy,cancel),path.GetType()==typeof(Spline)&&copy.GetType()==typeof(Spline),
            ModelParameterSnapshot.Equivalent,"NATIVE_PATH_CLONE_MISMATCH",
            "The detached native path copy does not retain its complete observed source geometry.","resident_face_path",cancel,warnings);
        CadSolidFaceEditor.Apply(spec,body,reference,cancel,copy);
        var after=Capture(CheckPath(source,db,model,cancel),cancel);
        bool geometry=ModelParameterSnapshot.Equivalent(before.Shape,after.Shape),metadata=ModelParameterSnapshot.Equivalent(before.Metadata,after.Metadata),
            display=ModelParameterSnapshot.Equivalent(before.Display,after.Display);
        Need(geometry&&metadata&&display,"NATIVE_PATH_SOURCE_CHANGED",
            "Native face growth changed the retained original path geometry, identity, appearance or observed root data.");
        if(before.HasExtension)warnings?.Add(PrivateDataWarning);
    }
}
