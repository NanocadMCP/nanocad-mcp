using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using Db=Teigha.DatabaseServices;
using G=Teigha.Geometry;

namespace NanoCADBridge;

// Public associative features, retained in the real history sequence. No direct
// transform of a live model representation and no bounding-box pivot fallback.
internal static class ModelPlacementHistory
{
    internal static bool IsPlacement(McObjectId id)=>id.GetObjectOfType<AssocMoveFeature>()!=null||id.GetObjectOfType<AssocRotateFeature>()!=null;
    internal static McObjectId[] Ids(McObjectId[] ids)
    {
        var result=ids.Where(IsPlacement).ToArray();
        return result;
    }
    internal static ModelPlacement Read(McObjectId id)
    {
        void Need(bool ok,string message){if(!ok)throw new DomainException("OUT_OF_SCOPE",message);}
        var item=id.GetObjectOfType<HistoryItem>();Need(item!=null&&!item.IsSuppressed&&item.Status.ToString()=="Ok","Placement is unavailable or unhealthy.");
        var move=id.GetObjectOfType<AssocMoveFeature>();
        if(move!=null)
        {
            var v=move.AssocOffset;Need(!move.CreatesCopy,"Associative copies require a different geometry oracle.");
            Need(new[]{v.X,v.Y,v.Z}.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1e6),"Placement displacement is out of bounds.");return new("move",new(v.X,v.Y,v.Z));
        }
        var rotate=id.GetObjectOfType<AssocRotateFeature>();Need(rotate!=null,"Unsupported placement type.");
        var param=rotate!.BasePointParam;Need(param!=null&&!param.ID.IsNull,"Rotation requires an explicit point anchor; implicit body-center pivots are protected.");
        var workPoint=param!.ID.GetObjectOfType<WorkPoint>();Need(workPoint!=null&&workPoint.DefinitionType==WPTDefinitionType.Fixed,"Rotation requires a native fixed WorkPoint.");
        var p=rotate.BasePoint;var point=workPoint!.Position;
        Need(new[]{p.X,p.Y,p.Z}.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1e6)&&Math.Abs(p.X-point.X)<1e-7&&Math.Abs(p.Y-point.Y)<1e-7&&Math.Abs(p.Z-point.Z)<1e-7,"Rotation pivot must equal its explicit fixed point.");
        var angles=new Point3(rotate.RotateX,rotate.RotateY,rotate.RotateZ);
        Need(new[]{angles.X,angles.Y,angles.Z}.All(v=>double.IsFinite(v)&&Math.Abs(v)<=360),"Rotation angles are outside supported degrees.");return new("rotate",angles,new(p.X,p.Y,p.Z));
    }
    internal static McObjectId[] Variables(McObjectId id)
    {
        var m=id.GetObjectOfType<AssocMoveFeature>();if(m!=null)return[m.OffsetXParamVarID,m.OffsetYParamVarID,m.OffsetZParamVarID];
        var r=id.GetObjectOfType<AssocRotateFeature>()!;return[r.RotateXParamVarID,r.RotateYParamVarID,r.RotateZParamVarID];
    }
    internal static Db.ObjectId SourceId(DocumentState state,Db.Transaction tx,McObjectId id)
    {
        var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        var all=((Db.BlockTableRecord)tx.GetObject(model,Db.OpenMode.ForRead)).Cast<Db.ObjectId>().ToArray();
        
        var found=all.Where(o=>!o.IsErased&&McObjectId.FromHandle(o.Handle.Value,McDocument.ActiveDocument.ID)==id).ToArray();
        if(found.Length!=1)throw new DomainException("OBJECT_IDENTITY_MISMATCH","Cannot identify a unique anchor representation.");return found[0];
    }
    internal static object? Parameters(McObjectId id)
    {
        var m=id.GetObjectOfType<AssocMoveFeature>();
        if(m!=null){var v=m.AssocOffset;return new{kind="associative_move",displacement=new[]{v.X,v.Y,v.Z},creates_copy=m.CreatesCopy,variable_ids=Variables(id).Select(v=>v.ToString()).ToArray(),distance_unit="drawing_units"};}
        var r=id.GetObjectOfType<AssocRotateFeature>();if(r==null)return null;
        var p=r.BasePoint;return new{kind="associative_rotate",center=new[]{p.X,p.Y,p.Z},anchor_object_id=r.BasePointParam.ID.IsNull?null:r.BasePointParam.ID.ToString(),angles_degrees=new[]{r.RotateX,r.RotateY,r.RotateZ},rotation_order="X_Y_Z",variable_ids=Variables(id).Select(v=>v.ToString()).ToArray()};
    }
    internal static G.Matrix3d Matrix(ModelPlacement placement)
    {
        var v=placement.Vector;if(placement.Kind=="move")return G.Matrix3d.Displacement(new(v.X,v.Y,v.Z));
        var c=placement.Center!;var p=new G.Point3d(c.X,c.Y,c.Z);double unit=Math.PI/180;
        return G.Matrix3d.Rotation(v.Z*unit,G.Vector3d.ZAxis,p)*G.Matrix3d.Rotation(v.Y*unit,G.Vector3d.YAxis,p)*G.Matrix3d.Rotation(v.X*unit,G.Vector3d.XAxis,p);
    }
    internal static bool Same(ModelPlacement a,ModelPlacement b)=>a.Kind==b.Kind&&Near(a.Vector,b.Vector)&&(a.Center==null?b.Center==null:b.Center!=null&&Near(a.Center,b.Center));
    internal static string[] Sequence(JsonElement snapshot,Action? cancel=null)
    {
        var nodes=snapshot.GetProperty("nodes").EnumerateArray().ToArray();
        var features=nodes.Where(n=>n.GetProperty("parameters").ValueKind==JsonValueKind.Object&&n.GetProperty("parameters").TryGetProperty("kind",out var k)&&k.GetString() is "extrusion" or "revolution" or "associative_move" or "associative_rotate").Select(n=>n.GetProperty("mc_id").GetString()!).ToArray();
        var links=nodes.Select(n=>new ModelSequenceLink(n.GetProperty("mc_id").GetString()!,n.GetProperty("native_order_before_id").GetString())).ToList();
        foreach(var source in snapshot.GetProperty("external_sources").EnumerateArray())
        {string id=source.GetProperty("id").GetString()!;if(!links.Any(l=>l.Id==id))links.Add(new(id,null));}
        var orders=new List<string[]>();
        foreach(var root in features)
            try{cancel?.Invoke();orders.Add(ModelFeatureSequence.FromHistory(links.ToArray(),features,root,snapshot.GetProperty("last_item").GetString()!,cancel));}
            catch(DomainException e)when(e.Error.code=="MODEL_ORDER_UNAVAILABLE"){}
        if(orders.Count!=1)throw new DomainException("MODEL_ORDER_UNAVAILABLE","One unambiguous ordered feature chain is required for native placement.");return orders[0];
    }
    internal static JsonElement WithoutOrderLinks(JsonElement[] nodes)
    {
        var result=JsonNode.Parse(NanoCAD.Contracts.Wire.Element(nodes).GetRawText())!.AsArray();
        foreach(var node in result)
        {
            node!.AsObject().Remove("native_order_before_id");
            if(node["parameters"] is JsonObject parameters)parameters.Remove("native_order_before_id");
        }
        return NanoCAD.Contracts.Wire.Element(result);
    }
    private static bool Near(Point3 a,Point3 b)=>Math.Abs(a.X-b.X)<1e-7&&Math.Abs(a.Y-b.Y)<1e-7&&Math.Abs(a.Z-b.Z)<1e-7;
    internal static void Apply(Db.Solid3d detached,ModelPlacement placement,Action cancel){cancel();detached.TransformBy(Matrix(placement));}
    internal static DrawingProfileWriter.TailFeature Step(McObjectId id)=>new(id,McObjectId.Null,McObjectId.Null,"placement",null!,"",Read(id));
    internal static void Apply(Db.Solid3d detached,DrawingProfileWriter.TailFeature step,Action cancel)
    {if(step.Placement!=null)Apply(detached,step.Placement,cancel);else ModelFeatureOracle.Apply(detached,step.Definition,step.Operation,cancel);}
}
