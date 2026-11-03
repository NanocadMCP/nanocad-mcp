using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelPatternReferences439
{
    internal sealed record Selection(JsonElement Selector,McObjectId Id,JsonElement? RetainedAxis);
    static void Need([DoesNotReturnIf(false)] bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    internal static McObjectId Id(string value)=>new(Guid.Parse(value));
    internal static McGeomParam Reference(ModelPatternDirectionBinding439 binding)=>new(){ID=Id(binding.native_id),Param=binding.parameter,GSMarker=binding.gs_marker,Data=binding.data};
    internal static Selection Resolve(JsonElement selector,Mc3dSolid body,McObjectId pattern,Db.ObjectId representation,
        DocumentState state,Db.Transaction tx,string bridge,bool circular,Action? cancel)
    {
        cancel?.Invoke();string kind=selector.GetProperty("kind").GetString()!;var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        McObjectId id=McObjectId.Null;JsonElement? retained=null;
        if(kind is "global_axis" or "fixed_axis")return new(selector,id,null); // Native factories run only after WriteJob starts.
        if(kind=="work_axis")
        {
            id=Id(selector.GetProperty("axis_id").GetString()!);var axis=id.GetObjectOfType<WorkAxis>();
            Need(axis!=null&&axis.ID==id&&axis.DbEntity.Document?.ID==owner.ID&&(axis.HostSolidID.IsNull||axis.HostSolidID==body.ID)&&
                axis.Status.ToString()=="Ok"&&!axis.IsSuppressed&&!axis.IsBeyondEndOfPart,"DATUM_DOCUMENT_MISMATCH","Select a healthy WorkAxis in this part's actual owner document, with no foreign part affinity.");
            var depends=axis.GetAllDependsOn(false);Need(depends!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The selected WorkAxis source closure is unavailable.");ModelInventoryBudget.Ids(depends,cancel);
            Need(pattern.IsNull||!depends.Contains(pattern),"DEPENDENCY_CYCLE","The selected axis depends on the pattern being edited.");
            foreach(var source in depends)
            {
                cancel?.Invoke();var item=source.GetObjectOfType<HistoryItem>();
                Need(item==null||item.HostSolidID.IsNull||item.HostSolidID==body.ID,"DEPENDENCY_OWNERSHIP_MISMATCH","The selected axis is defined by another part's history.");
            }
            using var line=axis.PositionAsLineSeg;Need(line!=null&&line.Length>0,"INVALID_GEOMETRY","The actual WorkAxis has no nonzero native geometry.");
            retained=AxisState(axis,cancel??(()=>{}));
        }
        else if(kind=="line")
        {
            var source=CadStructureWriter.Find(state.Document.Database,selector.GetProperty("handle").GetString()!);
            var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
            var line=tx.GetObject(source,Db.OpenMode.ForRead) as Db.Line;
            Need(line!=null&&line.GetType()==typeof(Db.Line)&&line.OwnerId==model&&line.Length>0,"OUT_OF_SCOPE","Choose an exact nonzero top-level native Line; prior projection is not required.");
            id=McObjectId.FromHandle(source.Handle.Value,McDocument.ActiveDocument.ID);
            using var geometry=new McGeomParam{ID=id}.GetGeometry();Need(geometry!=null&&geometry.GeometryType==EntityGeomType.kLine,"INVALID_GEOMETRY","The direction Line's actual native geometry is unavailable.");
        }
        else
        {
            var edges=ModelEdgeReader.Capture(representation,state,bridge,CancellationToken.None,cancel);
            Need(edges.edge_token==selector.GetProperty("edge_token").GetString(),"STALE_TOPOLOGY","Refresh the selected part's native model edges before binding the array.");
            string requested=selector.GetProperty("edge_id").GetString()!;
            var edge=edges.edges.SingleOrDefault(item=>item.model_edge_id.Equals(requested,StringComparison.OrdinalIgnoreCase));
            Need(edge!=null,"SUBENTITY_NOT_FOUND","The requested native model edge is not current in this selected part.");
            id=Id(requested);using var geometry=Service.GetFEVGeometry(id);
            Need(geometry!=null&&(geometry.GeometryType==EntityGeomType.kLine||circular&&geometry.GeometryType==EntityGeomType.kCircArc),"INVALID_GEOMETRY","Choose a linear edge for a rectangular direction, or a linear/circular edge for a circular axis.");
        }
        Need(!id.IsNull,"PATTERN_BINDING_UNAVAILABLE","The actual selected pattern reference identity is unavailable.");return new(selector,id,retained);
    }
    internal static Selection Materialize(Selection selection,Mc3dSolid body,HashSet<McObjectId> created,Action cancel)
    {
        cancel();var owner=body.DbEntity.Document;string kind=selection.Selector.GetProperty("kind").GetString()!;
        if(kind=="global_axis")
        {
            var type=selection.Selector.GetProperty("axis").GetString() switch{"X"=>GCSElementType.WA_OX,"Y"=>GCSElementType.WA_OY,_=>GCSElementType.WA_OZ};
            var axis=owner.Get3dHistory().GetGCSElement(type) as WorkAxis;
            Need(axis!=null&&axis.IsGCSObject&&axis.GCSItemType==type&&axis.DbEntity.Document?.ID==owner.ID,"DATUM_DOCUMENT_MISMATCH","The selected owner's native global axis is unavailable.");
            return selection with{Id=axis.ID,RetainedAxis=AxisState(axis,cancel)};
        }
        if(kind!="fixed_axis")return selection;
        var origin=selection.Selector.GetProperty("origin");var vector=selection.Selector.GetProperty("direction");
        var point=new Point3d(origin[0].GetDouble(),origin[1].GetDouble(),origin[2].GetDouble());
        double scale=vector.EnumerateArray().Max(value=>Math.Abs(value.GetDouble()));
        var direction=new Vector3d(vector[0].GetDouble()/scale,vector[1].GetDouble()/scale,vector[2].GetDouble()/scale).GetNormal();
        var fixedAxis=new WorkAxis();Need(fixedAxis.DbEntity.Document==null||fixedAxis.DbEntity.Document.ID==owner.ID,"DATUM_DOCUMENT_MISMATCH","A newly constructed WorkAxis already has a foreign owner.");
        Need(McObjectManager.Add2Document(fixedAxis.DbEntity,owner),"MODEL_UPDATE_FAILED","Registering the native fixed WorkAxis in the actual owner was rejected.");
        created.Add(fixedAxis.ID);
        Need(fixedAxis.DbEntity.Document?.ID==owner.ID&&fixedAxis.SetFixedDefinition(point,point+direction),"MODEL_UPDATE_FAILED","The native fixed WorkAxis definition was rejected.");
        fixedAxis.DbEntity.Update();return selection with{Id=fixedAxis.ID,RetainedAxis=AxisState(fixedAxis,cancel)};
    }
    internal static void VerifySelection(Selection selection,ModelPatternDirectionBinding439 binding,Action cancel)
    {
        cancel();Need(binding.native_id.Equals(selection.Id.ToString(),StringComparison.OrdinalIgnoreCase),"PATTERN_BINDING_CHANGED","The native array did not retain the selected axis/direction ID.");
    }
    internal static bool SameBinding(ModelPatternDirectionBinding439 before,ModelPatternDirectionBinding439 after)=>
        before==after;
    internal static JsonElement CaptureTarget(McGeomParam reference,Action cancel)
    {
        cancel();var id=reference.ID;var axis=id.GetObjectOfType<WorkAxis>();
        if(axis!=null)return AxisState(axis,cancel);
        using var geometry=reference.GetGeometry();Need(geometry!=null,"PATTERN_BINDING_UNAVAILABLE","The retained array reference geometry is unavailable.");
        if(geometry.GeometryType==EntityGeomType.kLine){using var line=geometry.LineSeg;return Wire.Element(SketchGeometryReader.Line(line));}
        if(geometry.GeometryType==EntityGeomType.kCircArc){using var arc=geometry.CircArc;return Wire.Element(SketchGeometryReader.Arc(arc));}
        throw new DomainException("PATTERN_BINDING_UNAVAILABLE","The retained axis is not a supported native WorkAxis, linear or circular reference.");
    }
    static JsonElement AxisState(WorkAxis axis,Action cancel)
    {
        cancel();Need(axis!=null&&axis.DbEntity.Document!=null&&axis.Status.ToString()=="Ok"&&!axis.IsSuppressed,"DATUM_DEFINITION_CHANGED","The exact retained WorkAxis is unavailable or unhealthy.");
        using var line=axis.PositionAsLineSeg;Need(line!=null&&line.Length>0,"INVALID_GEOMETRY","The actual WorkAxis line is unavailable.");
        var direction=(line.EndPoint-line.StartPoint).GetNormal();var start=line.StartPoint;
        double projection=start.X*direction.X+start.Y*direction.Y+start.Z*direction.Z;
        // Canonical oriented infinite line, not its display-segment bounds.
        // Preserve every actual definition binding and the native owner.
        var references=axis.GetDefinitionParams();Need(references!=null,"DATUM_DEFINITION_UNAVAILABLE","The WorkAxis definition bindings are unavailable.");
        string[] Ids(IEnumerable<McObjectId>? ids)
        {Need(ids!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The selected WorkAxis link inventory is unavailable.");ModelInventoryBudget.Ids(ids,cancel);return ids.Select(id=>id.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();}
        var constraints=axis.GetAttached3dConstraints();Need(constraints!=null,"DATUM_DEFINITION_UNAVAILABLE","The selected WorkAxis attached constraints are unavailable.");
        return Wire.Element(new{native_axis_state=true,native_id=axis.ID.ToString(),owner_document_id=axis.DbEntity.Document.ID.ToString(),definition=axis.DefinitionType.ToString(),
            direction=new[]{direction.X,direction.Y,direction.Z},normal_origin=new[]{start.X-projection*direction.X,start.Y-projection*direction.Y,start.Z-projection*direction.Z},
            definition_bindings=references.Select(reference=>{cancel();return new{id=reference.ID.IsNull?null:reference.ID.ToString(),parameter=reference.Param,gs_marker=reference.GSMarker,data=reference.Data};}).ToArray(),
            host_solid_id=axis.HostSolidID.IsNull?null:axis.HostSolidID.ToString(),native_order_before_id=axis.OrderObjBefore.IsNull?null:axis.OrderObjBefore.ToString(),
            dependency_ids=Ids(axis.GetDependsOn(false,false)).Where(id=>!id.Equals(axis.OrderObjBefore.ToString(),StringComparison.OrdinalIgnoreCase)).ToArray(),
            constraint_ids=constraints.Select(constraint=>{cancel();Need(constraint!=null,"DATUM_DEFINITION_UNAVAILABLE","A retained axis constraint is unavailable.");return constraint.ID.ToString();}).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            direct_consumer_ids=Ids(axis.GetDependent()),downstream_consumer_ids=Ids(axis.GetAllDependent(false))});
    }
    internal static void VerifyTarget(JsonElement before,McGeomParam reference,Mc3dSolid body,ArrayFeature pattern,Action cancel,bool released=false)
    {
        var after=CaptureTarget(reference,cancel);
        if(!before.TryGetProperty("native_axis_state",out _))
        {Need(ModelParameterSnapshot.Equivalent(before,after),"DATUM_DEFINITION_CHANGED","The retained native direction curve changed geometry.");return;}
        var old=System.Text.Json.Nodes.JsonNode.Parse(before.GetRawText())!.AsObject();var next=System.Text.Json.Nodes.JsonNode.Parse(after.GetRawText())!.AsObject();
        string[] Values(JsonElement value,string field)=>value.GetProperty(field).EnumerateArray().Select(item=>item.GetString()!).ToArray();
        var constraint=pattern.ConstraintID;var allowed=(pattern.GetAllDependent(false)??throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE","The array's actual downstream links are unavailable.")).Select(id=>id.ToString()).Append(pattern.ID.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!constraint.IsNull)allowed.Add(constraint.ToString());
        foreach(string field in new[]{"constraint_ids","direct_consumer_ids","downstream_consumer_ids"})
        {
            var prior=Values(before,field).ToHashSet(StringComparer.OrdinalIgnoreCase);var actual=Values(after,field).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Explicit axis retarget releases this array's exact relationship
            // from the old axis. It never releases an unrelated constraint or
            // consumer, nor every consumer merely because an axis was selected.
            var required=prior.Where(id=>!released||(field=="constraint_ids"?!id.Equals(constraint.ToString(),StringComparison.OrdinalIgnoreCase):!allowed.Contains(id))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Need(required.IsSubsetOf(actual)&&actual.Except(prior,StringComparer.OrdinalIgnoreCase).All(id=>field=="constraint_ids"?id.Equals(constraint.ToString(),StringComparison.OrdinalIgnoreCase):allowed.Contains(id)),
                "DATUM_DEPENDENCY_CHANGED","Using the WorkAxis lost a retained constraint/consumer or added a link unrelated to this exact native array.");
            old.Remove(field);next.Remove(field);
        }
        if(released&&!constraint.IsNull)
        {
            old["dependency_ids"]=System.Text.Json.JsonSerializer.SerializeToNode(Values(before,"dependency_ids").Where(id=>!id.Equals(constraint.ToString(),StringComparison.OrdinalIgnoreCase)),Wire.Json);
            next["dependency_ids"]=System.Text.Json.JsonSerializer.SerializeToNode(Values(after,"dependency_ids").Where(id=>!id.Equals(constraint.ToString(),StringComparison.OrdinalIgnoreCase)),Wire.Json);
        }
        if(before.GetProperty("host_solid_id").GetString()!=after.GetProperty("host_solid_id").GetString()||
            before.GetProperty("native_order_before_id").GetString()!=after.GetProperty("native_order_before_id").GetString())
        {
            bool acquisition=before.GetProperty("host_solid_id").ValueKind==JsonValueKind.Null&&after.GetProperty("host_solid_id").GetString()==body.ID.ToString();
            bool release=released&&before.GetProperty("host_solid_id").GetString()==body.ID.ToString()&&after.GetProperty("host_solid_id").ValueKind==JsonValueKind.Null&&
                after.GetProperty("native_order_before_id").ValueKind==JsonValueKind.Null&&!ModelInventoryBudget.History(body,cancel).Contains(reference.ID);
            Need(acquisition||release,"DATUM_ORDER_CHANGED","Only native acquisition or explicit binding release of this document WorkAxis may change its part affinity/order.");
            string? predecessor=after.GetProperty("native_order_before_id").GetString();
            Need(predecessor==null||ModelInventoryBudget.History(body,cancel).Any(id=>id.ToString().Equals(predecessor,StringComparison.OrdinalIgnoreCase)),"DATUM_ORDER_CHANGED","An acquired WorkAxis has an unproved native predecessor.");
        }
        old.Remove("host_solid_id");next.Remove("host_solid_id");old.Remove("native_order_before_id");next.Remove("native_order_before_id");
        Need(ModelParameterSnapshot.Equivalent(Wire.Element(old),Wire.Element(next)),"DATUM_DEFINITION_CHANGED","Using the WorkAxis changed its physical oriented line, definition, owner or retained non-order source links.");
    }
}
