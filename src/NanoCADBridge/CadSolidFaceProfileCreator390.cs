using System.Globalization;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Original source wrappers remain transaction-owned ForRead. The native
// face-profile methods receive only independent detached geometry copies.
internal static class CadSolidFaceProfileCreator390
{
    private sealed record State(JsonElement Shape,JsonElement Metadata,JsonElement Display,bool HasExtension);
    private static void Need(bool ok,string code,string text)
    {if(!ok)throw new DomainException(code,text);}
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static JsonElement Topology(Solid3d source,EntityRef reference,Action cancel)
        =>Wire.Element(SolidTopologyReader.Capture(source,reference,CancellationToken.None,cancel));
    private static Solid3d CheckSource(Entity source,Database db,ObjectId model,EntityRef reference,
        SolidFaceProfile390 selector,Action cancel)
    {
        cancel();var id=source.ObjectId;var sourceDatabase=source.Database;
        Need(source.GetType()==typeof(Solid3d)&&!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased&&
            source.OwnerId==model&&sourceDatabase!=null&&sourceDatabase.UnmanagedObject==db.UnmanagedObject&&
            source.UnmanagedObject!=IntPtr.Zero&&Id(id)==selector.Handle,
            "OUT_OF_SCOPE","The face profile must belong to a committed exact native Solid3d in the same current Model Space.");
        var solid=(Solid3d)source;
        Need(!solid.RecordHistory&&!solid.ShowHistory,"UNSUPPORTED_SOLID_HISTORY",
            "Face-profile creation follows existing solid-copy scope: enabled source history flags are not supported.");
        CadSolidEditor.CheckGeometry(solid,cancel);var topology=Topology(solid,reference,cancel);
        Need(topology.GetProperty("snapshot_token").GetString()==selector.TopologyToken,"STALE_TOPOLOGY",
            "Reacquire the source topology; the face-profile snapshot differs.");
        Need(topology.GetProperty("faces").EnumerateArray().Any(face=>face.GetProperty("native_id").GetString()==selector.NativeId),
            "SUBENTITY_NOT_FOUND","The selected native face does not belong to this exact source topology.");
        return solid;
    }
    internal static ObjectId[] Resolve(FaceProfileSolidSpec390 spec,Database db,Transaction tx,ObjectId model,
        EntityRef reference,Action cancel)
    {
        var id=CadStructureWriter.Find(db,spec.ProfileFace.Handle);
        if(tx.GetObject(id,OpenMode.ForRead) is not Entity source)
            throw new DomainException("UNSUPPORTED_ENTITY_TYPE","A face profile requires a native source body.");
        _=CheckSource(source,db,model,reference,spec.ProfileFace,cancel);
        if(spec.PathHandle==null)return new[]{id};
        var pathId=CadStructureWriter.Find(db,spec.PathHandle);
        if(tx.GetObject(pathId,OpenMode.ForRead) is not Entity path)
            throw new DomainException("UNSUPPORTED_ENTITY_TYPE","A face-profile sweep requires a native path curve.");
        var retained=CadSolidFacePath389.CheckPath(path,db,model,cancel);
        CadRetainedSolidPath392.RequireCurrentToken(retained,spec.PathToken,cancel);
        return new[]{id,pathId};
    }
    private static JsonElement SolidShape(Solid3d source,EntityRef reference,Action cancel)
    {
        var topology=Topology(source,reference,cancel);cancel();var mass=source.MassProperties;var bounds=source.GeometricExtents;
        return Wire.Element(new{topology_token=topology.GetProperty("snapshot_token").GetString(),volume=mass.Volume,
            centroid=new[]{mass.Centroid.X,mass.Centroid.Y,mass.Centroid.Z},
            min=new[]{bounds.MinPoint.X,bounds.MinPoint.Y,bounds.MinPoint.Z},
            max=new[]{bounds.MaxPoint.X,bounds.MaxPoint.Y,bounds.MaxPoint.Z},source.RecordHistory,source.ShowHistory});
    }
    private static JsonElement CurveShape(Curve source,Action cancel)
        =>CadRetainedSolidPath392.StableShape(source,cancel);
    private static State Capture(Entity source,JsonElement shape,Action cancel)
    {
        cancel();var budget=new CadContourBudget344(cancel);using var data=source.XData;using var links=source.GetPersistentReactorIds();
        var reactors=new List<string?>();foreach(ObjectId link in links){cancel();var value=Id(link);budget.Add(value!);reactors.Add(value);}
        var metadata=Wire.Element(new{handle=Id(source.ObjectId),owner_handle=Id(source.OwnerId),runtime_type=source.GetType().FullName,
            source.HasFields,extension_handle=Id(source.ExtensionDictionary),
            xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),reactors});
        var display=Wire.Element(new{layer_handle=Id(source.LayerId),properties=EntityAdapter.StoredProperties(source)});
        CadContourBudget344.Check(new{shape,metadata,display},cancel);return new(shape,metadata,display,!source.ExtensionDictionary.IsNull);
    }
    private static void Verify(State before,State after)
    {
        Need(ModelParameterSnapshot.Equivalent(before.Shape,after.Shape)&&
            ModelParameterSnapshot.Equivalent(before.Metadata,after.Metadata)&&ModelParameterSnapshot.Equivalent(before.Display,after.Display),
            "NATIVE_FACE_PROFILE_SOURCE_CHANGED","Native independent face-profile creation changed an original source's observed geometry, identity, appearance or root data.");
    }
    internal static Solid3d Create(FaceProfileSolidSpec390 spec,ObjectId[] ids,Transaction tx,EntityRef reference,
        Action cancel,List<string>? warnings)
    {
        var source=(Solid3d)tx.GetObject(ids[0],OpenMode.ForRead);
        var sourceBefore=Capture(source,SolidShape(source,reference,cancel),cancel);
        Need(sourceBefore.Shape.GetProperty("topology_token").GetString()==spec.ProfileFace.TopologyToken,"STALE_TOPOLOGY",
            "The source face-profile snapshot changed before construction.");
        using var profile=(Solid3d)CadContourBudget344.Detached(source);
        // Never infer subentity IDs survive cloning from counts or ordering.
        // Compare the complete actual B-rep token under the same source ref.
        var copiedShape=SolidShape(profile,reference,cancel);
        Need(copiedShape.GetProperty("topology_token").GetString()==spec.ProfileFace.TopologyToken&&
            ModelParameterSnapshot.Equivalent(sourceBefore.Shape,copiedShape),"SUBENTITY_IDENTITY_UNAVAILABLE",
            "The independent source copy does not retain the complete actual face-profile mapping and shape.");
        Curve? path=null,copiedPath=null;State? pathBefore=null;
        var result=new Solid3d();
        try
        {
            if(spec.PathHandle!=null)
            {
                path=(Curve)tx.GetObject(ids[1],OpenMode.ForRead);
                CadRetainedSolidPath392.RequireCurrentToken(path,spec.PathToken,cancel);
                pathBefore=Capture(path,CurveShape(path,cancel),cancel);
                copiedPath=(Curve)CadContourBudget344.Detached(path);
                CadCurveCopyGeometry417.Require(pathBefore.Shape,CurveShape(copiedPath,cancel),path.GetType()==typeof(Spline)&&copiedPath.GetType()==typeof(Spline),
                    ModelParameterSnapshot.Equivalent,"NATIVE_PATH_CLONE_MISMATCH",
                    "The independent path copy does not retain the complete reported native source geometry.","independent_face_profile_path",cancel,warnings);
            }
            var face=new SubentityId(SubentityType.Face,new IntPtr(long.Parse(spec.ProfileFace.NativeId,CultureInfo.InvariantCulture)));
            result.RecordHistory=false;cancel();
            if(spec.Kind=="extrusion")
            {
                var builder=new SweepOptionsBuilder{DraftAngle=spec.Angle};using var options=builder.ToSweepOptions();
                result.CreateExtrudedSolid(profile,face,new Vector3d(spec.Vector.X,spec.Vector.Y,spec.Vector.Z),options);
            }
            else if(spec.Kind=="revolve")
            {
                using var options=new RevolveOptions();
                result.CreateRevolvedSolid(profile,face,new Point3d(spec.Origin.X,spec.Origin.Y,spec.Origin.Z),
                    new Vector3d(spec.Vector.X,spec.Vector.Y,spec.Vector.Z).GetNormal(),spec.Angle,spec.StartAngle,options);
            }
            else
            {
                var basePoint=spec.BasePoint!;
                var builder=new SweepOptionsBuilder{Align=spec.Align?SweepOptionsAlignOption.AlignSweepEntityToPath:SweepOptionsAlignOption.NoAlignment,
                    BasePoint=new Point3d(basePoint.X,basePoint.Y,basePoint.Z),Bank=spec.Bank,TwistAngle=spec.Twist,ScaleFactor=spec.Scale,
                    CheckIntersections=true};using var options=builder.ToSweepOptions();
                result.CreateSweptSolid(profile,face,copiedPath!,options);
            }
            CadSolidEditor.CheckGeometry(result,cancel);
            Verify(sourceBefore,Capture(source,SolidShape(source,reference,cancel),cancel));
            if(path!=null)Verify(pathBefore!,Capture(path,CurveShape(path,cancel),cancel));
            if(sourceBefore.HasExtension||pathBefore?.HasExtension==true)
                warnings?.Add("FACE_PROFILE_SOURCE_DATA_UNVERIFIED: Original body/path identity, geometry, display and observed root metadata remain; opaque source-owned private records are not deeply verified. The new direct body has no persistent profile/path association.");
            return result;
        }
        catch{result.Dispose();throw;}
        finally{copiedPath?.Dispose();}
    }
}
