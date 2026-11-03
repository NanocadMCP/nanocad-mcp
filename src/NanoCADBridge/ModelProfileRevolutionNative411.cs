using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// SDK Sample3dObject.cs creates one profile (85), then uses its exact ID for
// extrusion (92) and revolution (99). This branch adds only the latter feature;
// it never creates a replacement sketch/profile or copies selected contours.
internal static class ModelProfileRevolutionNative411
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    internal static DrawingProfileWriter.Source ResolveAxis(ModelProfileRevolution411 edit,SketchPlaneSpec plane,McDocument drawing,
        DocumentState state,Db.Transaction tx,Action cancel)
    {
        cancel();Need(double.IsFinite(edit.AngleDegrees)&&edit.AngleDegrees>0&&edit.AngleDegrees<=360,"INVALID_ARGUMENT","Use an angle in (0,360] degrees.");
        var raw=CadStructureWriter.Find(state.Document.Database,edit.AxisHandle);var source=DrawingProfileWriter.SourceOf(raw,state,tx,true,plane);
        Need(source.Curve.Kind=="line"&&tx.GetObject(raw,Db.OpenMode.ForRead) is Db.Line,"INVALID_ARGUMENT","The existing-profile revolution axis must be an actual top-level native Line.");
        var line=(Db.Line)tx.GetObject(raw,Db.OpenMode.ForRead);var normal=DrawingWorkplane.Normal(plane);
        bool OnPlane(Teigha.Geometry.Point3d point)=>new[]{point.X,point.Y,point.Z}.All(double.IsFinite)&&
            Math.Abs((point.X-plane.Origin.X)*normal.X+(point.Y-plane.Origin.Y)*normal.Y+(point.Z-plane.Origin.Z)*normal.Z)<1e-7;
        Need(OnPlane(line.StartPoint)&&OnPlane(line.EndPoint),"PROFILE_PLANE_MISMATCH","The real revolution axis must lie in the selected profile's actual plane.");
        Need(!source.Native.IsNull&&McObjectId.FromHandle(raw.Handle.Value,drawing.ID)==source.Native&&
            source.Native.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbGeometry>() is {} exact&&exact.ID==source.Native,
            "OBJECT_IDENTITY_MISMATCH","The retained axis must resolve to its exact current drawing-native curve.");
        return source;
    }
    internal static RevolveFeature Append(Mc3dSolid body,SketchProfile profile,McObjectId axis,ModelProfileRevolution411 edit,Action cancel)
    {
        cancel();var before=ModelInventoryBudget.History(body,cancel).ToHashSet();
        var feature=body.AddRevolveFeature(profile.ID,new McGeomParam{ID=axis},edit.AngleDegrees*Math.PI/180);
        Need(feature!=null&&!feature.ID.IsNull&&!before.Contains(feature.ID),"MODEL_UPDATE_FAILED","Native revolution did not return a new feature from the exact retained profile.");
        // The native Angle property is radians. Its persisted MCS variable uses
        // degrees, as the accepted revolution route and installed SDK expose.
        feature.Extent=RevolveParamExtType.Angle;feature.Direction=FeatureExtentDirection.Positive;feature.Angle=edit.AngleDegrees*Math.PI/180;
        feature.PartOperation=edit.PartOperation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join;
        feature.Name=edit.Name;feature.DbEntity.Update();return feature;
    }
    internal static void Verify(RevolveFeature feature,SketchProfile profile,McObjectId axis,ModelProfileRevolution411 edit,Action cancel)
    {
        cancel();var exact=feature.ID.GetObjectOfType<RevolveFeature>();
        Need(exact!=null&&exact.ID==feature.ID&&feature.ProfileID==profile.ID&&feature.Axis!=null&&feature.Axis.ID==axis&&
            feature.PartOperation==(edit.PartOperation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join)&&
            feature.Extent==RevolveParamExtType.Angle&&feature.Direction==FeatureExtentDirection.Positive&&
            ModelParameterSnapshot.Near(feature.Angle,edit.AngleDegrees*Math.PI/180)&&!feature.RotationAngleParamVarID.IsNull,
            "PROFILE_OWNERSHIP_MISMATCH","Native revolution lost its exact profile/axis or requested angle, direction and material operation.");
    }
    internal static void VerifyVariable(RevolveFeature feature,ModelProfileRevolution411 edit,JsonElement parameters,Action cancel)
    {
        cancel();var variable=ModelParameterSnapshot.Variable(parameters,feature.RotationAngleParamVarID.ToString());
        Need(variable.GetProperty("host_object_id").GetString()==feature.ID.ToString()&&variable.GetProperty("value_available").GetBoolean()&&
            variable.GetProperty("value_unit").GetString()=="degrees"&&ModelParameterSnapshot.Near(variable.GetProperty("value").GetDouble(),edit.AngleDegrees),
            "MODEL_STATE_UNAVAILABLE","The retained revolution degree variable and native radian angle disagree or have another actual host.");
    }
}
