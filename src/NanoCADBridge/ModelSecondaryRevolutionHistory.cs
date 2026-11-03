using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Read-only admission for the connected335 subset. It proves the stored native
// chain and projected sources, without treating the revolution as the base or
// constructing an unrelated detached volume oracle.
internal static class ModelSecondaryRevolutionHistory
{
    private static void Need([DoesNotReturnIf(false)] bool ok,string message)
    {if(!ok)throw new DomainException("OUT_OF_SCOPE",message);}
    internal static string[] Sequence(JsonElement snapshot,ModelViewFollowers.Snapshot views,Action? cancel=null)
    {
        var model=JsonNode.Parse(snapshot.GetRawText())!.AsObject();model["last_item"]=views.LastModelItem.ToString();
        return ModelPlacementHistory.Sequence(Wire.Element(model),cancel);
    }
    internal static bool ValidateIfSecondary(Mc3dSolid body,JsonElement snapshot,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var nodes=snapshot.GetProperty("nodes").EnumerateArray().ToArray();
        static string? Kind(JsonElement node)=>node.GetProperty("parameters").ValueKind==JsonValueKind.Object&&node.GetProperty("parameters").TryGetProperty("kind",out var kind)?kind.GetString():null;
        var revolutions=nodes.Where(n=>Kind(n)=="revolution").ToArray();
        if(revolutions.Length!=1||!nodes.Any(n=>Kind(n)=="extrusion"))return false;
        var contents=ModelInventoryBudget.History(body,cancel);var ids=contents.ToArray();
        var nativeById=ids.ToDictionary(id=>id.ToString(),StringComparer.OrdinalIgnoreCase);
        var views=ModelViewFollowers.Resolve(body,ids,cancel);ModelViewFollowers.Verify(body,views,cancel);
        var sequence=Sequence(snapshot,views,cancel);var byId=nodes.ToDictionary(n=>n.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        // Retain the old base-revolution adapter for revolution→processing.
        if(Kind(byId[sequence[0]])=="revolution")return false;
        var revolutionId=nativeById[revolutions[0].GetProperty("mc_id").GetString()!];
        Need(sequence[^1]==revolutionId.ToString()&&views.LastModelItem==revolutionId&&
            sequence.All(id=>Kind(byId[id]) is "extrusion" or "revolution"),
            "The secondary-angle subset requires a retained extrusion base/processing chain followed by its one terminal revolution.");
        var external=ProjectedHistorySources.Read(body,ids,state,tx,cancel);ProjectedHistorySources.Healthy(ids,external);
        var profiles=new HashSet<McObjectId>();var sketches=new HashSet<McObjectId>();var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase){body.ID.ToString()};
        foreach(string id in sequence)
        {
            cancel();var native=nativeById[id];var extrusion=native.GetObjectOfType<ExtrudeFeature>();var revolve=native.GetObjectOfType<RevolveFeature>();
            McObjectId profileId;
            if(extrusion!=null)
            {Need(extrusion.HostSolidID==body.ID&&extrusion.PartOperation is PartFeatureOperation.Join or PartFeatureOperation.Cut,"Extrusion ownership or material operation is unsupported.");profileId=extrusion.ProfileID;}
            else
            {
                Need(revolve!=null&&revolve.HostSolidID==body.ID&&revolve.Extent==RevolveParamExtType.Angle&&revolve.Direction==FeatureExtentDirection.Positive&&
                    (revolve.PartOperation is PartFeatureOperation.Join or PartFeatureOperation.Cut)&&double.IsFinite(revolve.Angle)&&revolve.Angle>0&&revolve.Angle<=2*Math.PI&&
                    DrawingProfileGeometry.Near(revolve.Angle2,0)&&!revolve.RotationAngleParamVarID.IsNull,
                    "Use the retained positive first Angle extent, native angle variable and join/cut operation; second/legacy extents remain protected.");
                profileId=revolve.ProfileID;
            }
            var profile=profileId.GetObjectOfType<SketchProfile>();var sketch=profile?.ParentSketchID.GetObjectOfType<PlanarSketch>();
            Need(profile!=null&&sketch!=null&&profile.HostSolidID==body.ID&&sketch.HostSolidID==body.ID&&!sketch.IsInEditMode&&
                profiles.Add(profile.ID)&&sketches.Add(sketch.ID),"Each forming feature must retain its own profile and available body-owned sketch.");
            var dependencies=profile.GetDependsOn(false,false);Need(dependencies!=null&&dependencies.Contains(sketch.ID),"Native profile/sketch dependency is unavailable.");
            allowed.Add(native.ToString());allowed.Add(profile.ID.ToString());allowed.Add(sketch.ID.ToString());
        }
        var feature=revolutionId.GetObjectOfType<RevolveFeature>()!;var featureProfile=feature.ProfileID.GetObjectOfType<SketchProfile>()!;
        var featureSketch=featureProfile.ParentSketchID.GetObjectOfType<PlanarSketch>()!;
        var projections=new List<ProjectionSketchEnt>();
        foreach(var id in ids)
        {
            cancel();var projection=id.GetObjectOfType<ProjectionSketchEnt>();
            if(projection!=null){Need(sketches.Contains(projection.HostSketchID),"Projection belongs to an unselected sketch.");allowed.Add(id.ToString());if(!projection.PlanarObjectID.IsNull)allowed.Add(projection.PlanarObjectID.ToString());if(projection.HostSketchID==featureSketch.ID)projections.Add(projection);}
        }
        Need(projections.Count>0&&projections.Select(p=>p.SrcObjectID).Distinct().Count()==projections.Count,"Secondary revolution requires distinct retained source projections.");
        var sources=external.ToDictionary(s=>s.id,StringComparer.OrdinalIgnoreCase);
        Need(sources.TryGetValue(feature.Axis.ID.ToString(),out var axis)&&axis.revolution_axis_ids.Contains(feature.ID.ToString()),"The secondary revolution has no verified native axis dependency.");
        var profileSources=projections.Select(p=>{cancel();Need(sources.TryGetValue(p.SrcObjectID.ToString(),out var source),"A projected contour source is unavailable.");return source;}).ToArray();
        var circular=profileSources.Select(s=>{cancel();return tx.GetObject(CadStructureWriter.Find(state.Document.Database,s.handle),Db.OpenMode.ForRead);}).OfType<Db.Entity>().FirstOrDefault(e=>e is Db.Arc or Db.Circle);
        var frame=DrawingWorkplaneNative.FromSketch(featureSketch,circular);DrawingWorkplane.Parse(Wire.Element(new{origin=new[]{frame.Origin.X,frame.Origin.Y,frame.Origin.Z},x_axis=new[]{frame.XAxis.X,frame.XAxis.Y,frame.XAxis.Z},y_axis=new[]{frame.YAxis.X,frame.YAxis.Y,frame.YAxis.Z}}));
        var curves=new List<SketchConstraintCurve>();var budget=new ModelInventoryBudget(cancel);
        foreach(var source in profileSources)
        {cancel();var current=DrawingProfileWriter.SourceOf(CadStructureWriter.Find(state.Document.Database,source.handle),state,tx,false,frame);Need(current.Native.ToString()==source.id,"Contour source identity changed.");budget.Add(current.Curve);curves.Add(current.Curve);}
        var currentAxis=DrawingProfileWriter.SourceOf(CadStructureWriter.Find(state.Document.Database,axis.handle),state,tx,false,frame);
        Need(currentAxis.Native==feature.Axis.ID&&currentAxis.Curve.Kind=="line"&&profileSources.All(s=>s.id!=axis.id),"Axis must remain a separate coplanar exact native Line.");
        if(curves.Any(c=>c.Kind=="arc"))ModelPolycurve.FromCurves(curves.ToArray(),cancel);else DrawingProfileGeometry.Profile(curves.ToArray(),cancel);
        foreach(var source in external)allowed.Add(source.id);
        allowed.UnionWith(views.ViewIds.Select(id=>id.ToString()));
        Need(ids.All(id=>allowed.Contains(id.ToString())),"Additional feature/placement families are outside this secondary-angle subset.");
        Need(byId[sequence[0]].GetProperty("parameters").GetProperty("part_operation").GetString()=="Join","The first extrusion must be an additive base.");
        return true;
    }
}
