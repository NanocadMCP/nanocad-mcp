using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad.Mc3D;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Observes existing exact scope only. No solver, bootstrap, relation mutation
// or alternate-manager retry when native HRESULT leaves inventory unavailable.
internal static class ModelSketchConstraintObservation353
{
    internal const string Warning="SKETCH_CONSTRAINT_INVENTORY_UNAVAILABLE: Native sketch constraints could not be enumerated. This is not an empty-network or complete constraint-preservation claim; actual sketch geometry, source/feature identities and owning drivers remain checked. This readonly observation does not itself request a setter or solver.";
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string code,string message)
    {if(!value)throw new DomainException(code,message);}
    internal static JsonElement Read(Mc3dSolid body,PlanarSketch sketch,Action cancel)
    {
        cancel();var owner=body.DbEntity.Document;using var plane=sketch.Plane;
        using var manager=owner.Get2dConstraintsManager(Constraint2dNetType.Auto,sketch.ID,plane);
        Need(manager!=null&&manager.DocumentId==owner.ID&&manager.PlanarSketchId==sketch.ID&&!manager.ScopeId.IsNull,
            "CONSTRAINT_SCOPE_MISMATCH","The readonly sketch constraint manager lacks exact actual owner/document/sketch scope; no solver/bootstrap is used.");
        var ids=manager.GetAllConstraints();cancel();
        if(ids==null)return Wire.Element(new NanoCAD.Contracts.ModelSketchConstraintObservation353(owner.ID.ToString(),sketch.ID.ToString(),manager.ScopeId.ToString(),
            manager.ConstraintsNetworkType.ToString(),"not_evaluated",false,null,"native_GetAllConstraints_returned_null",Warning));
        ModelInventoryBudget.Ids(ids,cancel);var values=new List<JsonElement>();var bytes=new ModelInventoryBudget(cancel);
        object? Reference(IConstraintObjectInfo? info)
        {
            if(info==null||info.ObjectId.IsNull)return null;var p=info.PointRefOnObject;var point=new[]{p.X,p.Y,p.Z};
            return new{object_id=info.ObjectId.ToString(),subentity_id=ModelProfileDiscovery348.Id(info.SubentId),segment=info.PolylineSegmentIndex,
                point=point.All(double.IsFinite)?point:null,point_scope=point.All(double.IsFinite)?"reported_native_reference":"unspecified_or_nonfinite"};
        }
        foreach(var id in ids.OrderBy(id=>id.ToString(),StringComparer.OrdinalIgnoreCase))
        {
            cancel();using var info=manager.GetConstraintInfo(id);Need(info!=null,"CONSTRAINTS_UNAVAILABLE","A listed retained constraint has unavailable native metadata.");
            double parameter=info.Param;var item=Wire.Element(new{id=id.ToString(),type=info.Type.ToString(),info.IsGeometrical,info.IsDimensional,
                first=Reference(info.FirstObject),second=Reference(info.SecondObject),third=Reference(info.ThirdObject),
                parameter=double.IsFinite(parameter)?(double?)parameter:null,variable_id=ModelProfileDiscovery348.Id(info.VarId),variable_name=info.VarName,
                variable_expression=info.VarExpression,dimension_id=ModelProfileDiscovery348.Id(info.DimId)});
            bytes.Add(item);values.Add(item);
        }
        return Wire.Element(new NanoCAD.Contracts.ModelSketchConstraintObservation353(owner.ID.ToString(),sketch.ID.ToString(),manager.ScopeId.ToString(),
            manager.ConstraintsNetworkType.ToString(),"not_evaluated",true,values.ToArray(),null,null));
    }
    internal static bool Complete(JsonElement observation)=>observation.GetProperty("constraints_complete").GetBoolean();
    internal static string[] Warnings(IEnumerable<JsonElement> observations)=>observations.Any(value=>!Complete(value))?[Warning]:[];
    internal static bool Equivalent(JsonElement before,JsonElement after,Action cancel)
    {
        if(Complete(before)&&Complete(after))return ModelProfileRebindState349.Equivalent(before,after,cancel);
        return Scope(before).GetRawText()==Scope(after).GetRawText();
    }
    static JsonElement Scope(JsonElement observation)
    {
        var node=JsonNode.Parse(observation.GetRawText())!.AsObject();
        foreach(string key in new[]{"constraints_complete","constraints","unavailable_reason","warning"})node.Remove(key);
        return Wire.Element(node);
    }
    internal static JsonElement WithoutUnavailableDetails(JsonElement sketch)
    {
        var node=JsonNode.Parse(sketch.GetRawText())!.AsObject();node["constraints"]=JsonNode.Parse(Scope(sketch.GetProperty("constraints")).GetRawText());return Wire.Element(node);
    }
    internal sealed record HistoryOutcome(object Data,string[] Warnings);
}
