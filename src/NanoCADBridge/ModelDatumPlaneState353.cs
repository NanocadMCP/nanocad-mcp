using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// A resident native datum has its actual document/host. A selected target body
// establishes document scope; it is never written into HostSolidID or Owner.
internal static class ModelDatumPlaneState353
{
    internal sealed record Snapshot(WorkPlane Plane,McDocument Document,string DrawingDocument,McObjectId Body,
        ModelDatumPlaneRead353 Read,JsonElement Stored,JsonElement Design,string Token,Db.ObjectId? Representation);
    internal sealed record Part(Mc3dSolid Body,Db.ObjectId Representation,string DrawingDocument,string OwnerDocument,
        McObjectId[] Contents,JsonElement Parameters,JsonElement Metadata,JsonElement Display,HashSet<Db.ObjectId> Inventory,
        ModelViewFollowers.Snapshot Views,string[] Warnings);
    internal static void Need([DoesNotReturnIf(false)] bool value,string code,string message)
    {if(!value)throw new DomainException(code,message);}
    internal static string Key(McObjectId id)=>id.ToString();
    internal static string? Id(McObjectId id)=>id.IsNull?null:Key(id);
    static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    internal static bool Requested(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-8;
    static double N(double value)=>double.IsFinite(value)?value:throw new DomainException("INVALID_GEOMETRY","Native datum geometry is nonfinite.");
    static double[] P(G.Point3d value)=>[N(value.X),N(value.Y),N(value.Z)];
    static double[] V(G.Vector3d value)=>[N(value.X),N(value.Y),N(value.Z)];
    static string[] Sorted(IEnumerable<McObjectId>? values,Action cancel)
    {Need(values!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The native datum dependency inventory is unavailable.");ModelInventoryBudget.Ids(values,cancel);return values.Select(id=>{cancel();return Key(id);}).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();}
    internal static WorkPlane Resolve(string planeId,McDocument document,Action cancel)
        =>ModelNativeObjectResolver415.Plane(planeId,document,cancel);
    internal static Part CapturePart(Mc3dSolid body,ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var drawing=ModelFeatureReorderState345.Scope(state,tx);var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        var representation=ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel);var solid=tx.GetObject(representation,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx)&&!solid.IsNull,
            "OUT_OF_SCOPE","Select a nonempty exact current top-level native part.");
        var contents=ModelInventoryBudget.History(body,cancel).ToArray();var views=ModelViewFollowers.Resolve(body,contents,cancel,includeStandalone:true);
        var parameters=ModelParameterSnapshot.Capture(new(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,
            Validation.NormalizeHandle(representation.Handle.ToString()),"","",0),state,cancel);ModelParameterSnapshot.Healthy(parameters);
        var warnings=new List<string>();
        cancel();using(var brep=new Teigha.BoundaryRepresentation.Brep(solid))
        {
            Need(!brep.IsNull,"INVALID_GEOMETRY","The selected part has no native B-rep.");bool? healthy;
            try{healthy=brep.CheckEntity();}
            catch(Teigha.Runtime.Exception error) when(error.ErrorStatus.ToString() is "NotImplemented" or "NotImplementedYet")
            {healthy=null;warnings.Add(ModelFeatureReorderState345.ValidityWarning);}
            Need(healthy!=false,"INVALID_GEOMETRY","Native B-rep validation reports an invalid selected part.");
        }
        cancel();
        var display=Wire.Element(new{layer=solid.LayerId.Handle.ToString(),linetype=solid.LinetypeId.Handle.ToString(),properties=EntityAdapter.StoredProperties(solid)});
        var metadata=CadNativeStyleIdentityMetadata352.Metadata(solid,state.Document.Database,tx,cancel);
        new ModelInventoryBudget(cancel).Add(new{parameters,display,metadata});
        return new(body,representation,Key(drawing.ID),Key(owner.ID),contents,parameters,metadata,display,ModelFeatureReorderState345.Inventory(state,tx,cancel),views,warnings.ToArray());
    }
    internal static Snapshot Capture(WorkPlane plane,Mc3dSolid body,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var drawing=ModelProfileDiscovery348.DrawingDocument(state);var document=ModelProfileDiscovery348.OwnerDocument(body,state);
        Need(Resolve(Key(plane.ID),document,cancel).ID==plane.ID,"DATUM_DOCUMENT_MISMATCH","Native datum identity changed.");
        var contents=ModelInventoryBudget.History(body,cancel);bool member=contents.Contains(plane.ID);
        using var geometry=plane.Position;var origin=geometry.Origin;var u=geometry.DirU;var v=geometry.DirV;var normal=geometry.Normal;
        var o=P(origin);var x=V(u);var y=V(v);var n=V(normal);var exposed=V(plane.Normal);
        double Dot(double[] a,double[] b)=>a.Zip(b).Sum(pair=>pair.First*pair.Second);
        var cross=new[]{x[1]*y[2]-x[2]*y[1],x[2]*y[0]-x[0]*y[2],x[0]*y[1]-x[1]*y[0]};
        Need(Requested(Dot(n,n),1)&&Requested(Dot(x,x),1)&&Requested(Dot(y,y),1)&&Requested(Dot(x,y),0)&&Requested(Dot(n,x),0)&&Requested(Dot(n,y),0)&&
            cross.Zip(n).All(pair=>Requested(pair.First,pair.Second))&&
            n.Zip(exposed).All(pair=>Requested(pair.First,pair.Second)),"INVALID_GEOMETRY","The native datum does not expose a finite coherent oriented orthonormal plane.");
        var bindings=new List<object>();var byteBudget=new ModelInventoryBudget(cancel);
        // SDK only promises definition params for a non-fixed construction.
        if(plane.DefinitionType!=WPLDefinitionType.Fixed)
        {
            var definitions=plane.GetDefinitionParams();Need(definitions!=null,"DATUM_DEFINITION_UNAVAILABLE","Native plane definition parameters are unavailable.");
            foreach(var binding in definitions)
            {
                cancel();Need(binding!=null,"DATUM_DEFINITION_UNAVAILABLE","A native datum definition binding is unavailable.");
                var item=new{native_id=Id(binding.ID),binding.IsNull,marker=binding.GSMarker,data=binding.Data,
                    native_scalar=double.IsFinite(binding.Param)?(double?)binding.Param:null};byteBudget.Add(item);bindings.Add(item);
            }
        }
        bool scalar=plane.DefinitionType is WPLDefinitionType.PlaneNOffset or WPLDefinitionType.PlaneLineNAngle;
        double? parameter=scalar?N(plane.ParamVal):null;var variable=plane.ParamVarID;JsonElement? driver=null;
        if(!variable.IsNull)
        {
            using var manager=document.GetDocumentVariablesManager(Constraint2dNetType.MCS);var info=manager?.GetVariableInfo(variable);
            Need(manager!=null&&info!=null&&manager.GetVariableHostObjectId(variable)==plane.ID,"MODEL_DRIVER_CHANGED","The plane's actual owning MCS driver is unavailable or belongs to another object.");
            bool available=manager.GetVariableValue(variable,out double value);Need(!available||double.IsFinite(value),"INVALID_GEOMETRY","Native datum driver value is nonfinite.");
            driver=Wire.Element(new{mc_id=Key(variable),name=info.Name,expression=info.Expression,type=info.Type.ToString(),
                value=available?(double?)value:null,value_available=available,host_object_id=Key(plane.ID),
                downstream_variables=Sorted(manager.GetDependentOnVariables(variable),cancel),downstream_objects=Sorted(manager.GetDependentOn3dObjects(variable),cancel),
                scalar_scope=plane.DefinitionType==WPLDefinitionType.PlaneNOffset?"native_offset_parameter_not_assumed_equal_geometric_offset":"native_angular_units_unverified"});
        }
        var depends=Sorted(plane.GetDependsOn(false,false),cancel);var direct=Sorted(plane.GetDependent(),cancel);var downstream=Sorted(plane.GetAllDependent(false),cancel);
        var constraints=plane.GetAttached3dConstraints();Need(constraints!=null,"DATUM_DEFINITION_UNAVAILABLE","Native datum attached constraints are unavailable.");
        var constraintIds=constraints.Select(item=>{cancel();Need(item!=null,"DATUM_DEFINITION_UNAVAILABLE","A native datum attached constraint is unavailable.");return Key(item.ID);}).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
        var matches=ModelFeatureReorderState345.Inventory(state,tx,cancel).Where(id=>{cancel();return tx.GetObject(id,Db.OpenMode.ForRead) is Db.Entity&&McObjectId.FromHandle(id.Handle.Value,drawing.ID)==plane.ID;}).ToArray();
        Need(matches.Length<=1,"OBJECT_IDENTITY_MISMATCH","The datum has multiple current top-level native representations.");Db.ObjectId? representation=matches.Length==1?matches[0]:null;JsonElement? raw=null;
        if(representation.HasValue)
        {
            var entity=(Db.Entity)tx.GetObject(representation.Value,Db.OpenMode.ForRead);raw=Wire.Element(new{handle=Validation.NormalizeHandle(entity.Handle.ToString()),
                properties=EntityAdapter.StoredProperties(entity),metadata=CadNativeStyleIdentityMetadata352.Metadata(entity,state.Document.Database,tx,cancel)});
        }
        var frame=Wire.Element(new{origin_wcs=o,x_axis_wcs=x,y_axis_wcs=y,normal_wcs=n,oriented_plane_offset=Dot(o,n)});
        var sourceRelationship=ModelFaceDatumPlaneState356.Relation(plane,body,cancel);var definition=Wire.Element(bindings);var design=Wire.Element(new{name=plane.Name,definition_type=plane.DefinitionType.ToString(),normal=n,oriented_plane_offset=Dot(o,n),
            native_parameter=parameter,parameter_variable_id=Id(variable),driver,definition_bindings=definition,constraint_ids=constraintIds});
        var stored=Wire.Element(new{plane_id=Key(plane.ID),mc_document_id=Key(document.ID),design,plane_wcs=frame,host_solid_id=Id(plane.HostSolidID),
            native_order_before_id=Id(plane.OrderObjBefore),status=plane.Status.ToString(),plane.IsSuppressed,plane.IsBeyondEndOfPart,dependencies=depends,direct,downstream,representation=raw,source_relationship=sourceRelationship});
        byteBudget.Add(stored);string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Wire.Json)));
        var read=new ModelDatumPlaneRead353(Key(plane.ID),plane.Name,Key(document.ID),token,plane.DefinitionType.ToString(),frame,parameter,Id(variable),driver,definition,
            Id(plane.HostSolidID),member,Id(plane.OrderObjBefore),plane.Status.ToString(),plane.IsSuppressed,plane.IsBeyondEndOfPart,depends,direct,downstream,raw,
            "oriented_native_physical_plane; native_origin_U_V_are_observed_representation; no_persistent_frame_axis_or_source_subentity_identity_claim",sourceRelationship);
        return new(plane,document,Key(drawing.ID),body.ID,read,stored,design,token,representation);
    }
    internal static void MirrorAffinity(Snapshot datum,Mc3dSolid body)
    {
        ModelFaceDatumPlaneState356.MirrorScope(datum,body);
        Need(datum.Read.status=="Ok"&&!datum.Read.suppressed,"MODEL_STATE_UNAVAILABLE","The selected native datum must be healthy and active.");
        Need(datum.Plane.HostSolidID.IsNull||datum.Plane.HostSolidID==body.ID&&datum.Read.in_selected_body_contents&&!datum.Read.beyond_end_of_part,
            "DATUM_HOST_MISMATCH","Use native document construction or actual active datum affinity to this selected body; another part's host is not reassigned.");
    }
    internal static void VerifyFixed(Snapshot actual,ModelDatumPlaneCreate353 edit)
    {
        Need(actual.Read.name==edit.Name&&actual.Plane.DefinitionType==WPLDefinitionType.Fixed&&actual.Read.status=="Ok"&&!actual.Read.suppressed&&actual.Plane.ParamVarID.IsNull,
            "NATIVE_DATUM_MISMATCH","The new native plane name/fixed definition/health differs from the request.");
        var normal=DrawingWorkplane.Normal(edit.Plane);var desired=new[]{normal.X,normal.Y,normal.Z};var observed=actual.Read.plane_wcs.GetProperty("normal_wcs").EnumerateArray().Select(item=>item.GetDouble()).ToArray();
        Need(desired.Zip(observed).All(pair=>Requested(pair.First,pair.Second))&&Requested(actual.Read.plane_wcs.GetProperty("oriented_plane_offset").GetDouble(),DrawingWorkplane.Dot(edit.Plane.Origin,normal)),
            "NATIVE_DATUM_MISMATCH","The fixed native datum's actual oriented physical plane differs from the requested location/normal.");
    }
    internal static void VerifyCreatedPart(Part before,Part after,Snapshot datum,Action cancel)
    {
        Need(before.DrawingDocument==after.DrawingDocument&&before.OwnerDocument==after.OwnerDocument&&before.Representation==after.Representation,
            "OBJECT_IDENTITY_MISMATCH","Creating fixed native construction changed its selected part representation or actual document.");
        Need(before.Contents.All(after.Contents.Contains)&&after.Contents.Except(before.Contents).All(id=>id==datum.Plane.ID)&&
            before.Body.LastPartItemID==before.Views.LastHistoryItem&&after.Body.LastPartItemID==before.Views.LastHistoryItem,
            "MODEL_STATE_UNAVAILABLE","Creating fixed native construction changed existing part history/terminal.");
        Need(datum.Plane.HostSolidID.IsNull||datum.Plane.HostSolidID==after.Body.ID&&datum.Read.in_selected_body_contents,"DATUM_HOST_MISMATCH","The actual new datum host is unavailable or belongs to another body.");
        var a=JsonNode.Parse(before.Parameters.GetRawText())!.AsObject();var b=JsonNode.Parse(after.Parameters.GetRawText())!.AsObject();a.Remove("handles");b.Remove("handles");
        if(b["nodes"] is JsonArray nodes)for(int i=nodes.Count-1;i>=0;i--){cancel();if(nodes[i]!["mc_id"]!.GetValue<string>().Equals(Key(datum.Plane.ID),StringComparison.OrdinalIgnoreCase))nodes.RemoveAt(i);}
        Need(ModelParameterSnapshot.Equivalent(Wire.Element(a),Wire.Element(b))&&Exact(before.Parameters.GetProperty("variables"),after.Parameters.GetProperty("variables"))&&
            Exact(before.Metadata,after.Metadata)&&Exact(before.Display,after.Display),"MODEL_STATE_UNAVAILABLE","Creating fixed construction changed retained part geometry/design/drivers/sources/display/readable owned state.");
        Need(before.Inventory.All(after.Inventory.Contains)&&after.Inventory.Except(before.Inventory).All(id=>datum.Representation==id),"MODEL_STATE_UNAVAILABLE","Creating a native plane changed unrelated drawing membership.");
        Need(datum.Read.direct_consumer_ids.Length==0&&datum.Read.downstream_consumer_ids.Length==0,"DATUM_DEPENDENCY_CHANGED","The newly created datum acquired an unrequested consumer.");
        ModelViewFollowers.Verify(after.Body,before.Views,cancel);
    }
    internal static void VerifyMirror(Snapshot before,Snapshot after,McObjectId feature,Action cancel)
    {
        Need(before.Plane.ID==after.Plane.ID&&before.Document.ID==after.Document.ID&&before.DrawingDocument==after.DrawingDocument&&before.Body==after.Body,
            "DATUM_DOCUMENT_MISMATCH","The retained mirror datum identity/document changed.");MirrorAffinity(after,after.Body.GetObjectOfType<Mc3dSolid>()!);
        var target=feature.GetObjectOfType<HistoryItem>();Need(target!=null&&target.HostSolidID==after.Body&&target.GetDependsOn(false,false)?.Contains(after.Plane.ID)==true,
            "DATUM_DEPENDENCY_CHANGED","The actual mirror feature does not retain this native plane ID.");
        var allowed=Sorted(target.GetAllDependent(false),cancel).Append(Key(feature)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldDesign=JsonNode.Parse(before.Design.GetRawText())!.AsObject();var newDesign=JsonNode.Parse(after.Design.GetRawText())!.AsObject();
        Need(Requested(before.Design.GetProperty("oriented_plane_offset").GetDouble(),after.Design.GetProperty("oriented_plane_offset").GetDouble())&&
            before.Design.GetProperty("normal").EnumerateArray().Zip(after.Design.GetProperty("normal").EnumerateArray()).All(pair=>Requested(pair.First.GetDouble(),pair.Second.GetDouble())),
            "DATUM_DEFINITION_CHANGED","Native mirror reuse moved its retained physical plane.");
        oldDesign.Remove("normal");newDesign.Remove("normal");oldDesign.Remove("oriented_plane_offset");newDesign.Remove("oriented_plane_offset");
        if(oldDesign["driver"] is JsonObject oldDriver&&newDesign["driver"] is JsonObject newDriver)
        {
            var priorObjects=oldDriver["downstream_objects"]!.AsArray().Select(item=>item!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualObjects=newDriver["downstream_objects"]!.AsArray().Select(item=>item!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Need(priorObjects.IsSubsetOf(actualObjects)&&actualObjects.Except(priorObjects,StringComparer.OrdinalIgnoreCase).All(allowed.Contains),
                "MODEL_DRIVER_CHANGED","Native datum reuse lost or acquired an unrelated owning driver consumer.");
            oldDriver.Remove("downstream_objects");newDriver.Remove("downstream_objects");
        }
        Need(Exact(Wire.Element(oldDesign),Wire.Element(newDesign)),"DATUM_DEFINITION_CHANGED","Native mirror reuse changed its retained name/definition/source/driver/constraint state.");
        if(before.Read.native_order_before_id!=after.Read.native_order_before_id)
            Need(before.Read.host_solid_id==null&&after.Plane.HostSolidID==after.Body&&after.Read.in_selected_body_contents&&
                after.Read.native_order_before_id!=null&&ModelInventoryBudget.History(after.Body.GetObjectOfType<Mc3dSolid>()!,cancel).Any(id=>Key(id).Equals(after.Read.native_order_before_id,StringComparison.OrdinalIgnoreCase)),
                "DATUM_ORDER_CHANGED","Only actual document-datum acquisition into the selected native body can add its verified part predecessor.");
        var prior=before.Read.dependency_ids.Where(id=>!id.Equals(before.Read.native_order_before_id,StringComparison.OrdinalIgnoreCase)).ToArray();
        var current=after.Read.dependency_ids.Where(id=>!id.Equals(after.Read.native_order_before_id,StringComparison.OrdinalIgnoreCase)).ToArray();
        Need(prior.SequenceEqual(current,StringComparer.OrdinalIgnoreCase),"DATUM_DEPENDENCY_CHANGED","Native plane reuse changed an explicit non-order source dependency.");
        Need(before.Read.direct_consumer_ids.All(id=>after.Read.direct_consumer_ids.Contains(id,StringComparer.OrdinalIgnoreCase))&&
            before.Read.downstream_consumer_ids.All(id=>after.Read.downstream_consumer_ids.Contains(id,StringComparer.OrdinalIgnoreCase))&&
            after.Read.direct_consumer_ids.Contains(Key(feature),StringComparer.OrdinalIgnoreCase),"DATUM_DEPENDENCY_CHANGED","Native mirror reuse lost prior datum consumers or lacks its exact new feature relationship.");
        Need(after.Read.direct_consumer_ids.Except(before.Read.direct_consumer_ids,StringComparer.OrdinalIgnoreCase).All(allowed.Contains)&&
            after.Read.downstream_consumer_ids.Except(before.Read.downstream_consumer_ids,StringComparer.OrdinalIgnoreCase).All(allowed.Contains),"DATUM_DEPENDENCY_CHANGED","Native datum reuse acquired an unrelated consumer.");
        if(before.Read.representation is {} old&&after.Read.representation is {} next)
            Need(Exact(old,next),"DATUM_METADATA_CHANGED","Reusing a retained represented plane changed its display/readable owned metadata.");
        else Need(before.Read.representation==null,"DATUM_METADATA_CHANGED","The retained datum representation disappeared.");
    }
}
