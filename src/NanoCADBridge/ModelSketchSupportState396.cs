using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Actual native support/part affinity. This is independent of whether MCS can
// create or solve a requested relation; a document-only sketch remains valid.
internal static class ModelSketchSupportState396
{
    internal sealed record Binding(string Kind,McObjectId Support,SketchPlaneSpec Plane,string Token,JsonElement Stored,
        ModelDatumPlaneState353.Snapshot? Datum=null);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)=>ModelSketchLifecycleReader362.Need(ok,code,message);
    static string Key(McObjectId id)=>id.ToString();
    static Point3 P(G.Point3d p)=>new(p.X,p.Y,p.Z);
    static Point3 V(G.Vector3d v)=>new(v.X,v.Y,v.Z);
    static SketchPlaneSpec Frame(Point3 origin,Point3 x,Point3 y)
    {
        Need(new[]{origin.X,origin.Y,origin.Z,x.X,x.Y,x.Z,y.X,y.Y,y.Z}.All(double.IsFinite)&&
            Math.Abs(DrawingWorkplane.Dot(x,x)-1)<=1e-8&&Math.Abs(DrawingWorkplane.Dot(y,y)-1)<=1e-8&&Math.Abs(DrawingWorkplane.Dot(x,y))<=1e-8,
            "SKETCH_FRAME_UNAVAILABLE","Native support must expose a finite orthonormal WCS frame; no guessed or normalized axes are substituted.");
        return new(origin,x,y);
    }
    static SketchPlaneSpec Frame(G.Plane3d plane)=>Frame(P(plane.Origin),V(plane.DirU),V(plane.DirV));
    internal static SketchPlaneSpec Frame(PlanarSketch sketch)
    {var origin=sketch.TransformPointSketchToWorld(G.Point3d.Origin);return Frame(P(origin),V(sketch.TransformPointSketchToWorld(new G.Point3d(1,0,0))-origin),V(sketch.TransformPointSketchToWorld(new G.Point3d(0,1,0))-origin));}
    internal static bool SamePhysical(SketchPlaneSpec a,SketchPlaneSpec b)
    {
        var n=DrawingWorkplane.Normal(a);var m=DrawingWorkplane.Normal(b);
        return Math.Abs(Math.Abs(DrawingWorkplane.Dot(n,m))-1)<=1e-8&&Math.Abs(DrawingWorkplane.Dot(new(b.Origin.X-a.Origin.X,b.Origin.Y-a.Origin.Y,b.Origin.Z-a.Origin.Z),n))<=1e-7;
    }
    static JsonElement Faces(Mc3dSolid body,Db.ObjectId representation,DocumentState state,string bridge,Action cancel)
    {var value=Wire.Element(ModelFaceReader.Capture(representation,state,bridge,CancellationToken.None,cancel));Need(value.GetProperty("complete").GetBoolean()&&value.GetProperty("body_mc_id").GetString()==Key(body.ID),"SUPPORT_SCOPE_MISMATCH","Actual native face inventory belongs to another part.");return value;}
    internal static Binding Capture(ModelSketchSupportSpec396 request,Mc3dSolid body,Db.ObjectId representation,McDocument owner,DocumentState state,Db.Transaction tx,string bridge,Action cancel,bool requireToken=true)
    {
        cancel();
        if(request.Kind=="face")
        {
            var faces=Faces(body,representation,state,bridge,cancel);var rows=faces.GetProperty("faces").EnumerateArray().ToArray();
            if(requireToken)Need(faces.GetProperty("face_token").GetString()==request.Token,"STALE_TOPOLOGY","Refresh inspect_solid_topology with include_native_model_faces before selecting sketch support.");
            var selected=rows.Where(row=>row.GetProperty("model_face_id").GetString()!.Equals(request.Id,StringComparison.OrdinalIgnoreCase)).ToArray();
            Need(selected.Length==1,"SUBENTITY_NOT_FOUND","The exact requested planar support face is absent from the selected part.");
            var id=McObjectId.FromGsMarker(body.ID,long.Parse(selected[0].GetProperty("marker").GetString()!,CultureInfo.InvariantCulture));
            Need(!id.IsNull&&id.IsFace&&id.ParentId==body.ID&&Key(id).Equals(request.Id,StringComparison.OrdinalIgnoreCase),"SUPPORT_SCOPE_MISMATCH","The support face lacks exact native identity and selected-part ownership.");
            using var geometry=Service.GetFEVGeometry(id,FEVGeometryQueryMode.ExactGeometry|FEVGeometryQueryMode.CacheLock);using var plane=geometry?.Plane;
            Need(geometry!=null&&geometry.GeometryType==G.EntityGeomType.kPlaneSegment&&plane!=null,"UNSUPPORTED_FACE_TYPE","Choose a real native planar face for sketch support.");
            var frame=Frame(plane);return new("face",id,frame,faces.GetProperty("face_token").GetString()!,Wire.Element(new{kind="face",support_id=Key(id),body_id=Key(body.ID),owner_id=Key(owner.ID),face=selected[0]}));
        }
        var root=ModelDatumPlaneState353.Resolve(request.Id,owner,cancel);var datum=ModelDatumPlaneState353.Capture(root,body,state,tx,cancel);
        if(requireToken)Need(datum.Token==request.Token,"PRECONDITION_FAILED","Refresh the actual supported WorkPlane datum_token.");
        var contents=ModelInventoryBudget.History(body,cancel);
        Need(root.HostSolidID.IsNull||root.HostSolidID==body.ID&&contents.Contains(root.ID),"SUPPORT_SCOPE_MISMATCH","Use document construction or a WorkPlane of this actual part; another part's datum is not reassigned.");
        Need(datum.Read.status=="Ok"&&!datum.Read.suppressed&&!datum.Read.beyond_end_of_part,"MODEL_STATE_UNAVAILABLE","The actual WorkPlane support must be healthy and active.");
        var nodes=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);var visited=new HashSet<McObjectId>();var pending=new Stack<McObjectId>();pending.Push(root.ID);
        while(pending.Count>0)
        {
            cancel();var id=pending.Pop();if(id.IsNull||id==body.ID||!visited.Add(id))continue;
            if(id.IsFace||id.IsEdge||id.IsVertex)
            {
                Need(id.ParentId==body.ID,"SUPPORT_SCOPE_MISMATCH","Native WorkPlane sources must stay in this same part.");
                using var geometry=Service.GetFEVGeometry(id,FEVGeometryQueryMode.ExactGeometry|FEVGeometryQueryMode.CacheLock);
                Need(geometry!=null,"SUBENTITY_NOT_FOUND","A retained WorkPlane source is absent from its exact native definition geometry.");
                var feature=id.HostFeatureOrParentId;Need(!feature.IsNull&&(feature==body.ID||contents.Contains(feature)),"SUPPORT_SCOPE_MISMATCH","A WorkPlane topology source lacks its actual selected-part creating feature.");
                object source;
                if(id.IsFace)
                {
                    Need(geometry.GeometryType==G.EntityGeomType.kPlaneSegment,"SUPPORT_DEFINITION_UNAVAILABLE","This support adapter observes an actual planar face definition source.");
                    using var face=geometry.Plane;Need(face!=null,"SUPPORT_DEFINITION_UNAVAILABLE","A WorkPlane face source is not planar.");
                    var normal=V(face.Normal);double offset=face.Normal.X*face.Origin.X+face.Normal.Y*face.Origin.Y+face.Normal.Z*face.Origin.Z;
                    Need(new[]{normal.X,normal.Y,normal.Z,offset}.All(double.IsFinite),"INVALID_GEOMETRY","The exact WorkPlane face source has nonfinite plane geometry.");source=new{kind="plane",normal=new[]{normal.X,normal.Y,normal.Z},offset};
                }
                else if(geometry.GeometryType==G.EntityGeomType.kLine){using var line=geometry.LineSeg;source=SketchGeometryReader.Line(line);}
                else if(geometry.GeometryType==G.EntityGeomType.kCircArc){using var arc=geometry.CircArc;source=SketchGeometryReader.Arc(arc);}
                else if(NativeSketchMembers385.IsPoint(geometry)){var point=geometry.Point;source=new{kind="point",point=new[]{point.X,point.Y,point.Z}};}
                else throw new DomainException("SUPPORT_DEFINITION_UNAVAILABLE","This exact WorkPlane topology source needs a typed geometric observation adapter.");
                nodes[Key(id)]=Wire.Element(new{source_id=Key(id),source_feature_id=Key(feature),body_id=Key(body.ID),geometry_kind=geometry.GeometryType.ToString(),source_geometry=source});continue;
            }
            var item=id.GetObjectOfType<HistoryItem>();
            Need(item!=null&&item.ID==id&&item.DbEntity.Document?.ID==owner.ID&&(item.HostSolidID.IsNull||item.HostSolidID==body.ID&&contents.Contains(id)),
                "SUPPORT_SCOPE_MISMATCH","A support dependency lacks exact native owner/part affinity.");
            Need(item.Status.ToString()=="Ok"&&!item.IsSuppressed&&!item.IsBeyondEndOfPart,"MODEL_STATE_UNAVAILABLE","A retained support dependency is unhealthy or inactive.");
            var depends=item.GetDependsOn(false,false);Need(depends!=null,"SUPPORT_DEFINITION_UNAVAILABLE","Native support dependency identities are unavailable.");ModelInventoryBudget.Ids(depends,cancel);
            var plane=id.GetObjectOfType<WorkPlane>();McObjectId driver=McObjectId.Null;
            if(plane!=null)
            {
                nodes[Key(id)]=ModelDatumPlaneState353.Capture(plane,body,state,tx,cancel).Stored;
                driver=plane.ParamVarID;
                // Dependency APIs can describe owning objects; retain and walk
                // the actual geometric definition references as well. Never
                // infer a face from a body ID or a coincident physical plane.
                if(plane.DefinitionType!=WPLDefinitionType.Fixed)
                {
                    var definitions=plane.GetDefinitionParams();Need(definitions!=null,"SUPPORT_DEFINITION_UNAVAILABLE","Native WorkPlane definition references are unavailable.");
                    foreach(var reference in definitions){cancel();Need(reference!=null,"SUPPORT_DEFINITION_UNAVAILABLE","A native WorkPlane definition reference is unavailable.");if(!reference.ID.IsNull)pending.Push(reference.ID);}
                }
            }
            else
            {
                var direct=item.GetDependent();var downstream=item.GetAllDependent(false);
                Need(direct!=null&&downstream!=null,"SUPPORT_DEFINITION_UNAVAILABLE","Native support consumer identities are unavailable.");ModelInventoryBudget.Ids(direct,cancel);ModelInventoryBudget.Ids(downstream,cancel);
                nodes[Key(id)]=Wire.Element(new{native_id=Key(id),owner_id=Key(owner.ID),host_solid_id=ModelProfileDiscovery348.Id(item.HostSolidID),status=item.Status.ToString(),item.IsSuppressed,item.IsBeyondEndOfPart,
                    dependencies=depends.Select(Key).OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray(),direct=direct.Select(Key).OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray(),downstream=downstream.Select(Key).OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray()});
            }
            foreach(var dependency in depends)if(dependency!=driver&&dependency!=item.OrderObjBefore)pending.Push(dependency);
        }
        using var position=root.Position;Need(position!=null,"SUPPORT_DEFINITION_UNAVAILABLE","The actual supported WorkPlane position is unavailable.");var observed=Frame(position);
        ModelDatumDependents411.RequireConsumerScope(root,body,owner,cancel);
        return new("workplane",root.ID,observed,datum.Token,Wire.Element(new{kind="workplane",support_id=Key(root.ID),body_id=Key(body.ID),owner_id=Key(owner.ID),nodes=nodes.OrderBy(row=>row.Key,StringComparer.OrdinalIgnoreCase).Select(row=>new{id=row.Key,state=row.Value}).ToArray()}),datum);
    }
    internal static SketchPlaneSpec RequireAttached(Binding support,Mc3dSolid body,McDocument owner,PlanarSketch sketch,Action cancel)
    {
        cancel();var contents=ModelInventoryBudget.History(body,cancel);
        // An empty newly supported sketch can have an intermediate native
        // status. The SDK enters edit mode before its geometry/profile exists;
        // final Capture enforces healthy geometry after initialization.
        bool affinity=sketch.HostSolidID.IsNull||sketch.HostSolidID==body.ID&&contents.Contains(sketch.ID);
        Need(sketch.PlanarEntityID==support.Support&&affinity&&sketch.DbEntity.Document?.ID==owner.ID&&
            !sketch.IsSuppressed&&!sketch.IsBeyondEndOfPart,"SKETCH_OWNERSHIP_MISMATCH","The native support did not establish the actual selected-part sketch ownership before constraints.");
        var frame=Frame(sketch);Need(SamePhysical(frame,support.Plane),"SKETCH_FRAME_UNAVAILABLE","The actual supported sketch does not lie in its retained support plane.");return frame;
    }
    static JsonElement Normalize(JsonElement value,string newSketch,JsonElement? prior=null,IReadOnlySet<string>? admittedDownstream=null)
    {
        var node=JsonNode.Parse(value.GetRawText())!.AsObject();
        foreach(string name in new[]{"direct","downstream"})if(node[name] is JsonArray ids)
        {
            if(prior.HasValue)
            {
                var old=prior.Value.GetProperty(name).EnumerateArray().Select(id=>id.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);var current=ids.Select(id=>id!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Need(old.IsSubsetOf(current)&&current.Except(old,StringComparer.OrdinalIgnoreCase).All(id=>id.Equals(newSketch,StringComparison.OrdinalIgnoreCase)||name=="downstream"&&admittedDownstream?.Contains(id)==true),"SUPPORT_DEPENDENCY_CHANGED","A retained support consumer changed outside the exact selected sketch and frozen same-part downstream graph.");
            }
            node.Remove(name);
        }
        if(node["design"] is JsonObject design&&design["driver"] is JsonObject driver)
        {
            if(driver["downstream_objects"] is JsonArray ids)
            {
                if(prior.HasValue){var old=prior.Value.GetProperty("design").GetProperty("driver").GetProperty("downstream_objects").EnumerateArray().Select(id=>id.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);var current=ids.Select(id=>id!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);Need(old.IsSubsetOf(current)&&current.Except(old,StringComparer.OrdinalIgnoreCase).All(id=>id.Equals(newSketch,StringComparison.OrdinalIgnoreCase)||admittedDownstream?.Contains(id)==true),"SUPPORT_DEPENDENCY_CHANGED","A retained support driver acquired an unrelated consumer.");}
                driver.Remove("downstream_objects");
            }
        }
        return Wire.Element(node);
    }
    static JsonElement AcquiredRepresentation(JsonElement before,JsonElement after,PlanarSketch target,McDocument owner,
        DocumentState state,Db.Transaction tx,Action cancel,bool requireNewSketchReactor=false)
    {
        var prior=JsonNode.Parse(before.GetRawText())!.AsObject();var actual=JsonNode.Parse(after.GetRawText())!.AsObject();
        if(prior["representation"] is not JsonObject old||actual["representation"] is not JsonObject current)
        {Need(!requireNewSketchReactor,"SUPPORT_DEPENDENCY_CHANGED","Document-owned support display changes require an observed before/after resident representation.");return before;}
        bool exactNewSketchReactor=false;
        // First native support acquisition can hide its auxiliary drawable and
        // register the exact supported sketch as a persistent reactor. Prove
        // that current resident identity; no coincident/name/handle guess.
        if(old["properties"] is JsonObject oldProperties&&current["properties"] is JsonObject properties&&
            oldProperties["visible"]?.GetValue<bool>()==true&&properties["visible"]?.GetValue<bool>()==false)
            oldProperties["visible"]=false;
        if(old["metadata"] is JsonObject oldMetadata&&current["metadata"] is JsonObject metadata&&
            oldMetadata["reactors"] is JsonArray oldReactors&&metadata["reactors"] is JsonArray reactors)
        {
            var retained=oldReactors.Select(value=>value!.GetValue<string>()).ToList();var added=new List<string>();
            foreach(var value in reactors)
            {
                cancel();string handle=value!.GetValue<string>();int index=retained.FindIndex(item=>item.Equals(handle,StringComparison.OrdinalIgnoreCase));
                if(index>=0)retained.RemoveAt(index);else added.Add(handle);
            }
            Need(retained.Count==0,"SUPPORT_DEPENDENCY_CHANGED","Native support acquisition removed an existing persistent reactor.");
            if(added.Count>0)
            {
                long handle=target.ID.Handle;var drawing=ModelProfileDiscovery348.DrawingDocument(state);
                Need(handle!=0&&state.Document.Database.TryGetObjectId(new Db.Handle(handle),out var raw)&&!raw.IsNull&&!raw.IsErased&&!raw.IsEffectivelyErased&&
                    tx.GetObject(raw,Db.OpenMode.ForRead) is Db.Entity&&target.DbEntity.Document?.ID==owner.ID&&
                    (McObjectId.FromHandle(raw.Handle.Value,owner.ID)==target.ID||McObjectId.FromHandle(raw.Handle.Value,drawing.ID)==target.ID)&&
                    added.Count==1&&added[0].Equals(Validation.NormalizeHandle(raw.Handle.ToString()),StringComparison.OrdinalIgnoreCase),
                    "SUPPORT_DEPENDENCY_CHANGED","A new support reactor is not the exact resident selected sketch.");
                oldMetadata["reactors"]=JsonNode.Parse(reactors.ToJsonString());
                exactNewSketchReactor=true;
            }
        }
        Need(!requireNewSketchReactor||exactNewSketchReactor,"SUPPORT_DEPENDENCY_CHANGED","Document-owned support display changes require one exact newly attached resident sketch reactor.");
        return Wire.Element(prior);
    }
    internal static void Retained(Binding before,Binding after,string newSketch,DocumentState state,Db.Transaction tx,Action cancel,IReadOnlySet<string>? admittedDownstream=null)
    {
        Need(before.Kind==after.Kind&&before.Support==after.Support&&SamePhysical(before.Plane,after.Plane),"SUPPORT_DEFINITION_CHANGED","The actual selected native support identity or physical plane changed.");
        if(before.Kind=="face"){Need(ModelProfileRebindState349.Equivalent(before.Stored,after.Stored,cancel),"SUPPORT_DEFINITION_CHANGED","Native support face geometry/identity changed.");return;}
        var old=before.Stored.GetProperty("nodes").EnumerateArray().ToDictionary(row=>row.GetProperty("id").GetString()!,row=>row.GetProperty("state"),StringComparer.OrdinalIgnoreCase);
        var current=after.Stored.GetProperty("nodes").EnumerateArray().ToDictionary(row=>row.GetProperty("id").GetString()!,row=>row.GetProperty("state"),StringComparer.OrdinalIgnoreCase);
        Need(old.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(current.Keys),"SUPPORT_DEFINITION_CHANGED","The retained native support dependency graph changed.");
        foreach(var row in old)
        {
            cancel();bool root=row.Key.Equals(Key(before.Support),StringComparison.OrdinalIgnoreCase);
            var prior=Normalize(row.Value,newSketch,admittedDownstream:admittedDownstream);var actual=Normalize(current[row.Key],newSketch,row.Value,admittedDownstream);
            if(root)
            {
                Need(before.Datum!=null&&after.Datum!=null,"SUPPORT_DEFINITION_UNAVAILABLE","The selected WorkPlane lacks its actual before/after owner snapshot.");
                if(before.Datum.Read.host_solid_id==null&&after.Datum.Read.host_solid_id!=null)
                {
                    var body=after.Datum.Body.GetObjectOfType<Mc3dSolid>();var target=ModelSketchLifecycleReader362.Resolve(newSketch,after.Datum.Document,cancel);
                    Need(body!=null&&target.HostSolidID==body.ID&&target.PlanarEntityID==after.Support&&after.Datum.Plane.HostSolidID==body.ID&&
                        ModelInventoryBudget.History(body,cancel).Contains(target.ID)&&ModelInventoryBudget.History(body,cancel).Contains(after.Support)&&
                        (after.Datum.Plane.OrderObjBefore.IsNull||ModelInventoryBudget.History(body,cancel).Contains(after.Datum.Plane.OrderObjBefore)),"DATUM_HOST_MISMATCH","Native WorkPlane acquisition lacks exact selected-part composition/assigned-predecessor proof.");
                    prior=AcquiredRepresentation(prior,actual,target,after.Datum.Document,state,tx,cancel);
                }
                else if(before.Datum.Read.host_solid_id==null&&after.Datum.Read.host_solid_id==null&&
                    !ModelProfileRebindState349.Equivalent(prior.GetProperty("representation"),actual.GetProperty("representation"),cancel))
                {
                    // Native AddPlanarSketch/support binding can keep both
                    // objects as document construction until a forming feature
                    // consumes the profile. It can already hide the support's
                    // drawable and register this exact attached sketch. This
                    // changes neither native owner nor physical datum design.
                    var target=ModelSketchLifecycleReader362.Resolve(newSketch,after.Datum.Document,cancel);
                    Need(target.HostSolidID.IsNull&&target.PlanarEntityID==after.Support&&target.DbEntity.Document?.ID==after.Datum.Document.ID&&
                        SamePhysical(Frame(target),after.Plane)&&after.Datum.Read.direct_consumer_ids.Contains(newSketch,StringComparer.OrdinalIgnoreCase),
                        "SUPPORT_SCOPE_MISMATCH","Document-owned support display changes lack the exact same-owner attached sketch, physical plane or direct consumer.");
                    prior=AcquiredRepresentation(prior,actual,target,after.Datum.Document,state,tx,cancel,requireNewSketchReactor:true);
                }
                prior=ModelDatumDependents411.SupportAcquisition(prior,actual,Key(before.Datum.Body),newSketch,cancel);
            }
            Need(ModelProfileRebindState349.Equivalent(prior,actual,cancel),"SUPPORT_DEFINITION_CHANGED","A retained support definition/driver/geometry/owned state changed.");
        }
    }
    internal static ModelSketchNativeSupport396 Describe(Binding value,Mc3dSolid body,McDocument owner,SketchPlaneSpec nativeFrame,PlanarSketch? sketch=null)
    {static double[] A(Point3 p)=>[p.X,p.Y,p.Z];return new(value.Kind,Key(value.Support),value.Token,Key(body.ID),Key(owner.ID),new(A(nativeFrame.Origin),A(nativeFrame.XAxis),A(nativeFrame.YAxis),A(DrawingWorkplane.Normal(nativeFrame))),
        value.Datum==null?Key(body.ID):ModelProfileDiscovery348.Id(value.Datum.Plane.HostSolidID),sketch==null?null:ModelProfileDiscovery348.Id(sketch.HostSolidID));}
    internal static ModelSketchNativeSupport396? Observe(Mc3dSolid body,PlanarSketch sketch,SketchPlaneSpec frame,DocumentState state,Db.Transaction tx,string bridge,Action cancel)
    {
        var id=sketch.PlanarEntityID;if(id.IsNull||sketch.HostSolidID!=body.ID)return null;
        try
        {
            string kind;if(id.IsFace&&id.ParentId==body.ID)kind="face";
            else if(id.GetObjectOfType<WorkPlane>() is {} plane&&(plane.HostSolidID.IsNull||plane.HostSolidID==body.ID))kind="workplane";
            else return null;
            var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var representation=ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel);
            var actual=Capture(new(kind,Key(id),""),body,representation,owner,state,tx,bridge,cancel,false);RequireAttached(actual,body,owner,sketch,cancel);return Describe(actual,body,owner,frame,sketch);
        }
        // The caller already enforced the existing sketch identity, health,
        // geometry and ownership guards before this optional provenance read.
        // Missing/unsupported support diagnostics never disable that read or
        // claim support. Selected write preflight has no such catch.
        catch(DomainException error)when(error.Error.code!="CANCELLED"){return null;}
        catch(Teigha.Runtime.Exception error)when(error.ErrorStatus.ToString() is "NotImplemented" or "NotImplementedYet"){return null;}
    }
}
