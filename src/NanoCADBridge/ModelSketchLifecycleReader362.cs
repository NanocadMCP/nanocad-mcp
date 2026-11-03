using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelSketchLifecycleReader362
{
    internal sealed record Snapshot(PlanarSketch Sketch,SketchPlaneSpec Plane,JsonElement Geometry,McObjectId[] Elements,McObjectId[] Containing,
        ModelProfileDiscovery348.ProjectionState Projections,JsonElement Constraints,JsonElement Profiles,JsonElement Stored,string Token);
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition,string code,string message)
    {if(!condition)throw new DomainException(code,message);}
    internal static string Key(McObjectId id)=>id.ToString();
    internal static PlanarSketch Resolve(string identity,McDocument owner,Action cancel)
        =>ModelNativeObjectResolver415.Sketch(identity,owner,cancel);
    static Point3 Point(JsonElement value)=>new(value[0].GetDouble(),value[1].GetDouble(),value[2].GetDouble());
    internal static JsonElement Metadata(McObjectId identity,McDbObject native,McDocument owner,DocumentState state,Db.Transaction tx,Action cancel,bool observeResidentIdentity=false)
    {
        // Native construction may have no resident DWG representation. Do not
        // invent one from a GUID or treat a coincident raw handle as ownership.
        var drawing=ModelProfileDiscovery348.DrawingDocument(state);
        if(identity.Handle!=0&&state.Document.Database.TryGetObjectId(new Db.Handle(identity.Handle),out var raw)&&!raw.IsNull&&!raw.IsErased&&!raw.IsEffectivelyErased&&
            (McObjectId.FromHandle(raw.Handle.Value,owner.ID)==identity||McObjectId.FromHandle(raw.Handle.Value,drawing.ID)==identity))
        {
            cancel();Need(native.Document?.ID==owner.ID&&native.Entity?.ID==identity,"OBJECT_IDENTITY_MISMATCH","Native metadata observation requires the exact current construction and its actual owner document.");
            var value=tx.GetObject(raw,Db.OpenMode.ForRead);var reactors=native.GetReactors();
            var metadata=ModelProfileElementsState358.Metadata(value,tx,cancel,false,observeNativeProxy:true);
            var reactorObservation=ModelProfileElementsState358.ReactorObservation(value,drawing,owner,identity,[],reactors,tx,cancel);
            if(observeResidentIdentity)
            {
                Need(value.ObjectId==raw&&value.Database?.UnmanagedObject==state.Document.Database.UnmanagedObject,"OBJECT_IDENTITY_MISMATCH","Native metadata observation requires its exact current local resident object.");
                return Wire.Element(new{scope="exact_resident_DWG_owned_metadata",metadata,
                    resident_identity=new{native_id=Key(identity),native_owner_document_id=Key(owner.ID),raw_handle=Validation.NormalizeHandle(raw.Handle.ToString()),
                        raw_owner_handle=value.OwnerId.Handle.ToString(),runtime_type=value.GetType().FullName},reactor_observation=reactorObservation});
            }
            return Wire.Element(new{scope="exact_resident_DWG_owned_metadata",metadata,reactor_observation=reactorObservation});
        }
        return Wire.Element(new{scope="no_proved_resident_DWG_representation; native_properties_geometry_and_relationships_observed"});
    }
    internal static SketchConstraintCurve[] Curves(Snapshot value,Action cancel)
    {
        return NativeSketchGeometry385.Curves(value.Geometry,value.Plane,cancel);
    }
    internal static Snapshot Capture(Mc3dSolid body,PlanarSketch sketch,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);Resolve(Key(sketch.ID),owner,cancel);var contents=ModelInventoryBudget.History(body,cancel);
        Need(sketch.HostSolidID.IsNull||sketch.HostSolidID==body.ID&&contents.Contains(sketch.ID),"SKETCH_OWNERSHIP_MISMATCH","The sketch has native affinity outside the selected part or lacks actual body composition.");
        Need(sketch.Status.ToString()=="Ok"&&!sketch.IsSuppressed&&!sketch.IsBeyondEndOfPart&&!sketch.IsInEditMode,"MODEL_STATE_UNAVAILABLE","The exact sketch must be healthy and active without native edit mode.");
        var bytes=new ModelInventoryBudget(cancel);var geometry=Wire.Element(SketchGeometryReader.Read(sketch,cancel,bytes.Add));
        Need(geometry.GetProperty("geometry_details_complete").GetBoolean()&&geometry.GetProperty("elements").EnumerateArray().All(item=>item.GetProperty("status").GetString()=="read"&&item.GetProperty("geometry").GetProperty("kind").GetString() is "line" or "circular_arc" or "polyline" or "spline" or "elliptical_arc"),
            "OUT_OF_SCOPE","This sketch lifecycle reads complete native Line/Arc/Circle and polyline-segment geometry.");
        var frame=geometry.GetProperty("frame");var plane=new SketchPlaneSpec(Point(frame.GetProperty("origin_wcs")),Point(frame.GetProperty("x_step_wcs")),Point(frame.GetProperty("y_step_wcs")));
        Need(Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.XAxis)-1)<=1e-10&&Math.Abs(DrawingWorkplane.Dot(plane.YAxis,plane.YAxis)-1)<=1e-10&&Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.YAxis))<=1e-10,
            "SKETCH_FRAME_UNAVAILABLE","The actual sketch frame must have perpendicular unit WCS axes; no normalization is substituted.");
        var raw=sketch.GetAllItemsGeometry(false);Need(raw!=null,"SKETCH_GEOMETRY_UNAVAILABLE","The complete native sketch element identity inventory is unavailable.");McObjectId[] elements;
        try{elements=raw.Keys.OrderBy(Key,StringComparer.OrdinalIgnoreCase).ToArray();}
        finally{foreach(var value in raw.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
        Need(elements.All(id=>!id.IsNull)&&elements.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(geometry.GetProperty("elements").EnumerateArray().Select(item=>item.GetProperty("element_id").GetString()!)),
            "SKETCH_GEOMETRY_UNAVAILABLE","The exact native element inventory changed during its read; element IDs are not assumed to be standalone resident objects.");
        var containing=sketch.GetContainingObjects(true,true,false);Need(containing!=null,"SKETCH_GEOMETRY_UNAVAILABLE","Complete native sketch membership is unavailable.");ModelInventoryBudget.Ids(containing,cancel);
        var projections=ModelProfileDiscovery348.Projections(sketch,plane,state,tx,cancel,elements);
        Need(projections.Ids.All(id=>containing.Contains(id)&&sketch.ContainsObject(id)),"SKETCH_OWNERSHIP_MISMATCH","A source projection lacks actual native sketch membership.");
        var profiles=new List<JsonElement>();
        foreach(var id in ModelProfileDiscovery348.Ids<SketchProfile>(owner,cancel))
        {
            // ObjectFilter can include native child construction outside this
            // selected sketch. Prove the actual typed candidate/parent first;
            // exact work-ID/document resolution applies only to selected peers.
            cancel();var candidate=id.GetObjectOfType<SketchProfile>();
            if(candidate==null||candidate.ID!=id)
                throw new DomainException("PROFILE_OWNERSHIP_MISMATCH","A document profile inventory item lacks its exact current typed identity.",false,
                    new{component="profile_inventory_candidate_identity",inventory_profile_id=Key(id),selected_sketch_id=Key(sketch.ID),
                        typed_profile_id=candidate==null?null:Key(candidate.ID),owner_document_id=Key(owner.ID)});
            var candidateParent=candidate.ParentSketchID;if(candidateParent!=sketch.ID)continue;
            SketchProfile peer;
            try{peer=ModelProfileDiscovery348.Resolve(Key(id),owner,cancel);}
            catch(DomainException error)when(error.Error.code=="PROFILE_OWNERSHIP_MISMATCH")
            {
                throw new DomainException(error.Error.code,error.Error.message,false,
                    new{component="selected_profile_exact_document_resolution",inventory_profile_id=Key(id),typed_profile_id=Key(candidate.ID),
                        typed_parent_sketch_id=Key(candidateParent),selected_sketch_id=Key(sketch.ID),owner_document_id=Key(owner.ID),
                        original_details=error.Error.details});
            }
            if(peer.ID!=id||peer.ParentSketchID!=sketch.ID)
                throw new DomainException("PROFILE_OWNERSHIP_MISMATCH","A selected exact profile changed identity or parent during owner resolution.",false,
                    new{component="selected_profile_parent_after_resolution",inventory_profile_id=Key(id),resolved_profile_id=Key(peer.ID),
                        resolved_parent_sketch_id=ModelProfileDiscovery348.Id(peer.ParentSketchID),selected_sketch_id=Key(sketch.ID),owner_document_id=Key(owner.ID)});
            var selected=ModelProfileElementsReader358.Capture(body,peer,state,tx,cancel);
            var value=Wire.Element(new{profile_id=Key(peer.ID),properties=new{layer=peer.DbEntity.Layer,visibility=peer.DbEntity.Visibility},native_metadata=Metadata(peer.ID,peer.DbEntity,owner,state,tx,cancel),state=selected.Stored});bytes.Add(value);profiles.Add(value);
        }
        var depends=sketch.GetDependsOn(false,false);Need(depends!=null,"HISTORY_UNAVAILABLE","The sketch's actual dependency inventory is unavailable.");ModelInventoryBudget.Ids(depends,cancel);
        var constraints=ModelSketchConstraintObservation353.Read(body,sketch,cancel);
        var stored=Wire.Element(new{sketch_id=Key(sketch.ID),mc_document_id=Key(owner.ID),host_solid_id=ModelProfileDiscovery348.Id(sketch.HostSolidID),
            support_id=ModelProfileDiscovery348.Id(sketch.PlanarEntityID),previous=ModelProfileDiscovery348.Id(sketch.OrderObjBefore),name=sketch.Name,
            properties=new{layer=sketch.DbEntity.Layer,visibility=sketch.DbEntity.Visibility},native_metadata=Metadata(sketch.ID,sketch.DbEntity,owner,state,tx,cancel),geometry,
            containing_ids=containing.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            dependencies=depends.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),projection_bindings=projections.Bindings,sources=projections.Sources,
            constraint_observation=constraints,profiles});bytes.Add(stored);
        return new(sketch,plane,geometry,elements,containing.ToArray(),projections,constraints,Wire.Element(profiles),stored,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Wire.Json))));
    }
    internal static ModelSketchRead362 Describe(Snapshot value,McDocument owner,DocumentState state,Db.Transaction tx,Action cancel,Mc3dSolid body,string bridge)
    {
        static double[] V(Point3 point)=>[point.X,point.Y,point.Z];
        return new(Key(value.Sketch.ID),value.Stored.GetProperty("mc_document_id").GetString()!,ModelProfileDiscovery348.Id(value.Sketch.HostSolidID),ModelProfileDiscovery348.Id(value.Sketch.PlanarEntityID),value.Token,
            new(V(value.Plane.Origin),V(value.Plane.XAxis),V(value.Plane.YAxis),V(DrawingWorkplane.Normal(value.Plane))),value.Geometry,
            value.Profiles.EnumerateArray().Select(item=>item.GetProperty("profile_id").GetString()!).ToArray(),value.Projections.Ids.Select(Key).ToArray(),value.Projections.Sources.Select(source=>source.id).Concat(value.Projections.NativeReferences.Select(Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),value.Constraints,
            value.Sketch.HostSolidID.IsNull?"document_construction":"native_host_affinity",
            ModelSketchGeometryBindingsReader365.Read(value.Sketch,owner,value.Constraints,cancel),
            NativeSketchMembers385.Observe(value.Sketch,state,tx,cancel),ModelSketchSupportState396.Observe(body,value.Sketch,value.Plane,state,tx,bridge,cancel));
    }
    internal static ModelSketchDiscovery362 Read(DocumentState state,Db.Transaction tx,Action cancel,bool includeInventory,string? cursor,string[]? sketchIds,string[] bodyHandles,string bridge)
    {
        var drawing=ModelProfileDiscovery348.DrawingDocument(state);var owners=new Dictionary<McObjectId,McDocument>();var bodies=new List<Mc3dSolid>();
        foreach(string handle in bodyHandles)
        {
            cancel();var id=McObjectId.FromHandle(unchecked((long)Convert.ToUInt64(handle,16)),drawing.ID);var body=id.GetObjectOfType<Mc3dSolid>();Need(body!=null&&body.ID==id,"OBJECT_IDENTITY_MISMATCH","Select actual current native bodies for document sketch discovery.");
            var owner=ModelProfileDiscovery348.OwnerDocument(body,state);owners[owner.ID]=owner;bodies.Add(body);
        }
        Need(owners.Count==1,"SKETCH_DOCUMENT_SCOPE_AMBIGUOUS","Choose bodies sharing the intended sketch's actual native owner document.");var doc=owners.Values.Single();ModelSketchInventory362? inventory=null;
        if(includeInventory)
        {
            var items=new List<ModelSketchIdentity362>();int used=2;bool complete=true;
            foreach(var id in ModelProfileDiscovery348.Ids<PlanarSketch>(doc,cancel))
            {
                cancel();if(cursor!=null&&StringComparer.OrdinalIgnoreCase.Compare(Key(id),cursor)<=0)continue;var sketch=Resolve(Key(id),doc,cancel);
                var item=new ModelSketchIdentity362(Key(id),ModelProfileDiscovery348.Id(sketch.HostSolidID),sketch.Name);int bytes=JsonSerializer.SerializeToUtf8Bytes(item,Wire.Json).Length+1;
                if(used+bytes>128*1024&&items.Count>0){complete=false;break;}used+=bytes;items.Add(item);
            }
            inventory=new(Key(doc.ID),"same_document_native_sketch_identities_not_body_composition",items.ToArray(),complete,complete?null:items[^1].sketch_id);
        }
        var selected=new List<ModelSketchRead362>();var budget=new ModelInventoryBudget(cancel);
        foreach(string identity in sketchIds??[])
        {
            var sketch=Resolve(identity,doc,cancel);var body=sketch.HostSolidID.IsNull?bodies[0]:bodies.SingleOrDefault(value=>value.ID==sketch.HostSolidID);
            Need(body!=null,"SKETCH_OWNERSHIP_MISMATCH","A selected sketch has affinity to a different native body; select its actual part.");var value=Describe(Capture(body,sketch,state,tx,cancel),doc,state,tx,cancel,body,bridge);budget.Add(value);selected.Add(value);
        }
        return new(inventory,selected.ToArray());
    }
}
