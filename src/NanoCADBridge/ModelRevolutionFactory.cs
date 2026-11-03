using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class ModelRevolutionFactory
{
    internal sealed record Items(PlanarSketch Sketch,SketchProfile Profile,RevolveFeature Feature);
    internal static Items Append(Mc3dSolid body,ModelExtrusionSpec profileDefinition,McObjectId[] sources,McObjectId axis,
        double angleDegrees,string operation,Action cancel,Action<string> trace)
    {
        // Reuse the accepted body-owned native profile/projection factory. Its
        // distance is only a unit-depth profile validation value; no extrusion
        // feature or detached reference solid is created here.
        var (sketch,profile)=ModelExtrusionFactory.CreateProfile(body,profileDefinition,cancel,trace,sources);
        cancel();double angle=angleDegrees*Math.PI/180;
        var feature=body.AddRevolveFeature(profile.ID,new McGeomParam{ID=axis},angle);
        if(feature==null)throw new DomainException("MODEL_UPDATE_FAILED","Native revolution was not created.");
        // Set all angle properties before the first feature rebuild. Once an
        // angular variable exists, its setter uses native degree expressions
        // while Angle's getter remains radians (accepted194/195 evidence).
        feature.Extent=RevolveParamExtType.Angle;
        feature.Direction=FeatureExtentDirection.Positive;
        feature.Angle=angle;
        feature.PartOperation=operation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join;
        feature.Name=profileDefinition.Name+" Revolution";feature.DbEntity.Update();
        trace("revolution_created");return new(sketch,profile,feature);
    }
}
