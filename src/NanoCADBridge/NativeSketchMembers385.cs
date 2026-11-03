using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// 3DCore.h documents real objects, projections and abstract GUID items. Exact
// contained item IDs use GetItemGeometry in WCS; none are inspection-ID aliases.
internal static class NativeSketchMembers385
{
    internal sealed record Member(McObjectId Id,McObjectId ObjectId,int? Segment,string NativeType,JsonElement Geometry,SketchConstraintCurve Curve,bool Owned,string? Reason,bool ReferenceOnly=false);
    internal sealed record Construction(McObjectId Id,JsonElement Geometry);
    internal sealed record Snapshot(PlanarSketch Sketch,SketchPlaneSpec Plane,Member[] Members,JsonElement Stored,string Token,Construction[] Construction,McObjectId[] Helpers);
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static string Key(McObjectId id)=>id.ToString();
    internal static bool IsPoint(G.EntityGeometry geometry)=>geometry.GeometryType is G.EntityGeomType.kPoint or G.EntityGeomType.kVertex;
    static Point3 P(G.Point3d p)=>new(p.X,p.Y,p.Z);
    internal static G.Point3d World(SketchPlaneSpec plane,Point2 local)=>DrawingWorkplaneNative.Point(plane,local);
    internal static Snapshot Capture(PlanarSketch sketch,DocumentState state,Db.Transaction tx,Action cancel,
        Constraints2D? observedManager=null,IEnumerable<McObjectId>? observedConstraints=null)
    {
        var origin=sketch.TransformPointSketchToWorld(G.Point3d.Origin);var x=sketch.TransformPointSketchToWorld(new G.Point3d(1,0,0))-origin;var y=sketch.TransformPointSketchToWorld(new G.Point3d(0,1,0))-origin;
        var plane=new SketchPlaneSpec(P(origin),new(x.X,x.Y,x.Z),new(y.X,y.Y,y.Z));
        Need(Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.XAxis)-1)<1e-9&&Math.Abs(DrawingWorkplane.Dot(plane.YAxis,plane.YAxis)-1)<1e-9&&Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.YAxis))<1e-9,"SKETCH_FRAME_UNAVAILABLE","The native sketch frame must be orthonormal.");
        ModelProfileDiscovery348.DrawingDocument(state);var owner=sketch.DbEntity.Document;var context=sketch.IsInEditMode?McDocumentsManager.GetActiveSheet():owner;
        Need(owner!=null&&!owner.ID.IsNull,"SKETCH_BINDING_UNAVAILABLE","Actual native sketch owner document is unavailable.");
        Need(context!=null&&!context.ID.IsNull,"SKETCH_BINDING_UNAVAILABLE","Actual native sketch input context is unavailable.");
        Constraints2D? OptionalManager()
        {try{return Manager(sketch,allowUnlinkedEditScope:sketch.IsInEditMode);}catch(OperationCanceledException){throw;}catch(Exception){return null;}}
        using var acquiredManager=observedManager==null?OptionalManager():null;var manager=observedManager??acquiredManager;
        if(manager!=null)RequireManagerScope(manager,sketch,context,allowUnlinkedEditScope:sketch.IsInEditMode);
        var helpers=new HashSet<McObjectId>();var helperRoles=new List<(McObjectId Id,string Role,McObjectId Relation,string Type)>();var fixedPoints=new HashSet<McObjectId>();
        IEnumerable<McObjectId>? relations=observedConstraints;
        if(relations==null&&manager!=null)try{relations=manager.GetAllConstraints();}
        catch(OperationCanceledException){throw;}
        catch(Exception)when(observedManager==null){/* Optional acquisition/inventory absence is not an empty network. */}
        if(relations!=null)
        {
            Need(manager!=null,"CONSTRAINTS_UNAVAILABLE","An observed relation inventory requires its exact native manager.");ModelInventoryBudget.Ids(relations,cancel);
            foreach(var relation in relations)
            {
                cancel();using var info=manager.GetConstraintInfo(relation);Need(info!=null,"CONSTRAINTS_UNAVAILABLE","A native relation helper lacks actual metadata in the complete current inventory.");
                foreach(var value in new[]{(Id:relation,Role:"relation"),(Id:info.DimId,Role:"dimension"),(Id:info.VarId,Role:"parameter")})
                {if(value.Id.IsNull)continue;helpers.Add(value.Id);helperRoles.Add((value.Id,value.Role,relation,info.Type.ToString()));}
                if(info.Type==ConstraintType.GeomFix)foreach(var reference in new[]{info.FirstObject,info.SecondObject,info.ThirdObject})if(reference!=null&&!reference.ObjectId.IsNull)fixedPoints.Add(reference.ObjectId);
            }
        }
        var objectMembers=sketch.GetContainingObjects(true,false,false);var projections=sketch.GetContainingObjects(false,true,false);var all=sketch.GetContainingObjects(true,true,true);
        Need(objectMembers!=null&&projections!=null&&all!=null,"SKETCH_BINDING_UNAVAILABLE","The native member/projection/construction inventories are unavailable.");ModelInventoryBudget.Ids(objectMembers,cancel);ModelInventoryBudget.Ids(projections,cancel);ModelInventoryBudget.Ids(all,cancel);
        // The SDK separately exposes included objects and the complete geometry
        // ID map. Only actual nonresident Spline keys extend effective members;
        // this does not claim ContainsObject returned true for those IDs.
        var excluded=helpers.Concat(projections).ToHashSet();var abstractSplines=NativeSketchMembership445.AbstractSplines(sketch,cancel,excluded);
        var containing=objectMembers.Concat(abstractSplines.Keys).Distinct().ToArray();
        // Native document residency is required by AddObject. A resident DWG
        // alias is not proof that a direct member is an external source.
        var projectedSources=new Dictionary<McObjectId,McObjectId>();
        foreach(var projectionId in ModelProfileDiscovery348.Ids<ProjectionSketchEnt>(owner,cancel))
        {
            cancel();var projection=projectionId.GetObjectOfType<ProjectionSketchEnt>();
            if(projection==null||!containing.Contains(projection.SrcObjectID))continue;
            Need(projection.ID==projectionId&&projection.DbEntity.Document!=null&&projection.DbEntity.Document.ID==owner.ID&&!projection.HostSketchID.IsNull,
                "SKETCH_BINDING_UNAVAILABLE","A current source projection lacks its exact native identity, owner or sketch binding.");
            projectedSources.TryAdd(projection.SrcObjectID,projectionId);
        }
        var sharedMembers=new Dictionary<McObjectId,McObjectId>();
        foreach(var peerId in ModelProfileDiscovery348.Ids<PlanarSketch>(owner,cancel))
        {
            cancel();if(peerId==sketch.ID)continue;var peer=peerId.GetObjectOfType<PlanarSketch>();
            bool validPeer=peer!=null&&peer.ID==peerId&&peer.DbEntity.Document!=null&&peer.DbEntity.Document.ID==owner.ID;
            if(!validPeer){Need(abstractSplines.Count==0,"SKETCH_BINDING_UNAVAILABLE","An abstract Spline's peer-sketch owner inventory is unavailable.");continue;}
            foreach(var memberId in objectMembers){cancel();if(peer!.ContainsObject(memberId))sharedMembers.TryAdd(memberId,peerId);}
            if(abstractSplines.Count>0)
            {
                var peerGeometry=peer!.GetAllItemsGeometry(false);Need(peerGeometry!=null,"SKETCH_BINDING_UNAVAILABLE","An abstract Spline's peer geometry-ID inventory is unavailable; nonsharing is not inferred.");
                try
                {
                    ModelInventoryBudget.Ids(peerGeometry.Keys,cancel);
                    foreach(var memberId in abstractSplines.Keys){cancel();if(peerGeometry.ContainsKey(memberId))sharedMembers.TryAdd(memberId,peerId);}
                }
                finally{foreach(var value in peerGeometry.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
            }
        }
        Need(context!=null&&!context.ID.IsNull,"SKETCH_BINDING_UNAVAILABLE","Actual native sketch input context is unavailable.");var members=new List<Member>();var objectStates=new List<object>();var objectStateIds=new HashSet<McObjectId>();var construction=new List<Construction>();var budget=new ModelInventoryBudget(cancel);
        void StoreObject(McObjectId id,DbGeometry? value,bool abstractItem)
        {
            if(!objectStateIds.Add(id))return;
            object row=value!=null?new{id=Key(id),owner_document_id=Key(value.DbEntity.Document.ID),owner_sketch_id=Key(sketch.ID),storage="resident_native_geometry",actual_native_type=value.GetType().FullName,resident_native_object_absent=false,layer=value.DbEntity.Layer,visibility=value.DbEntity.Visibility,
                native_metadata=ModelSketchLifecycleReader362.Metadata(id,value.DbEntity,value.DbEntity.Document,state,tx,cancel)}:
                new{id=Key(id),owner_document_id=Key(owner.ID),owner_sketch_id=Key(sketch.ID),storage="abstract_native_sketch_item",actual_native_type=(string?)null,resident_native_object_absent=abstractItem,
                    native_metadata=Wire.Element(new{scope=abstractSplines.ContainsKey(id)?"exact_native_WCS_geometry_map_and_GetItemGeometry; actual_resident_native_object_absence_observed":"exact_contained_abstract_sketch_geometry; actual_resident_native_object_absence_observed"})};budget.Add(row);objectStates.Add(row);
        }
        void ConstructionPoint(McObjectId id,G.EntityGeometry geometry)
        {
            var p=geometry.Point;Need(double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z),"SKETCH_GEOMETRY_UNAVAILABLE","Native construction point is nonfinite.");
            if(!objectStateIds.Contains(id))
            {
                var value=id.GetObjectOfType<DbGeometry>();var actual=value??id.GetObject();Need(actual==null||actual.ID==id,"OBJECT_IDENTITY_MISMATCH","A native construction reference resolved a different object.");
                if(value!=null){Need(value.ID==id&&value.DbEntity.Document!=null,"SKETCH_BINDING_UNAVAILABLE","A resident construction point lost its actual native identity/document.");StoreObject(id,value,false);}
                else if(actual==null)StoreObject(id,null,true);
                else
                {
                    McDbObject? nativeEntity=actual is McEntity entity?entity.DbObject:null;var nativeOwner=nativeEntity?.Document;
                    var observation=new{id=Key(id),owner_document_id=nativeOwner==null?null:Key(nativeOwner.ID),owner_sketch_id=Key(sketch.ID),storage="native_construction_object",actual_native_type=actual.GetType().FullName,resident_native_object_absent=false,
                        native_metadata=nativeEntity!=null&&nativeOwner!=null?ModelSketchLifecycleReader362.Metadata(id,nativeEntity,nativeOwner,state,tx,cancel):Wire.Element(new{scope="construction_native_residence_metadata_unavailable"})};
                    objectStateIds.Add(id);budget.Add(observation);objectStates.Add(observation);
                }
            }
            var row=new Construction(id,Wire.Element(new{kind="point",point=new[]{p.X,p.Y,p.Z}}));budget.Add(new{id=Key(id),geometry=row.Geometry});construction.Add(row);
        }
        void Add(McObjectId member,McObjectId objectId,int? segment,string type,JsonElement geometry,bool owned,string? reason,bool referenceOnly=false)
        {
            SketchConstraintCurve Curve()
            {
                var g=geometry;Point3 Q(JsonElement p)=>new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble());string id=Key(member);
                if(g.GetProperty("kind").GetString()=="line")return new(id,"line",DrawingWorkplane.Local(plane,Q(g.GetProperty("start"))),DrawingWorkplane.Local(plane,Q(g.GetProperty("end"))));
                if(g.GetProperty("kind").GetString() is "spline" or "elliptical_arc")return NativeSketchCurveGeometry445.Curve(id,g,plane,cancel);
                return ModelPolycurve.FromSketchArc(id,Q(g.GetProperty("center")),g.GetProperty("radius").GetDouble(),Q(g.GetProperty("normal")),Q(g.GetProperty("reference_direction")),g.GetProperty("start_angle").GetDouble(),g.GetProperty("end_angle").GetDouble(),plane);
            }
            var curve=Curve();bool Finite(Point2? p)=>p==null||double.IsFinite(p.X)&&double.IsFinite(p.Y);
            Need(Finite(curve.Start)&&Finite(curve.End)&&Finite(curve.Center)&&double.IsFinite(curve.Radius)&&double.IsFinite(curve.Sweep),"SKETCH_GEOMETRY_UNAVAILABLE","Native sketch curve has nonfinite geometry.");
            var row=new Member(member,objectId,segment,type,geometry,curve,owned,reason,referenceOnly);budget.Add(new{member_id=Key(member),object_id=Key(objectId),segment,geometry,owned,reason,reference_only=referenceOnly});members.Add(row);
        }
        foreach(var id in containing.Distinct().OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();if(helpers.Contains(id)||projections.Contains(id)||id.GetObjectOfType<SketchProfile>()!=null||id.GetObjectOfType<HistoryItem>()!=null)continue;
            var value=id.GetObjectOfType<DbGeometry>();
            bool abstractItem=value==null&&id.GetObject()==null;
            bool abstractSpline=abstractSplines.ContainsKey(id);
            Need(!id.IsNull&&(abstractSpline?abstractItem:sketch.ContainsObject(id)),"SKETCH_BINDING_UNAVAILABLE","A native input lost its exact selected-sketch object membership or observed nonresident Spline geometry identity.");
            if(!(abstractItem||value!=null&&value.ID==id&&value.DbEntity.Document!=null))
            {
                object? itemGeometry=null,lookupError=null;string? nativeType=null,nativeClass=null;
                try{nativeClass=id.ClassName;nativeType=id.GetObject()?.GetType().FullName;using var actual=sketch.GetItemGeometry(id,false);itemGeometry=new{returned=actual!=null,kind=actual?.GeometryType.ToString()};}
                catch(OperationCanceledException){throw;}catch(Exception error){lookupError=new{exception_type=error.GetType().Name,exception_hresult=error.HResult};}
                throw new DomainException("SKETCH_BINDING_UNAVAILABLE","A containing input lacks its exact current DbGeometry identity/document/member binding.",false,new{sketch_id=Key(sketch.ID),sketch_edit_mode=sketch.IsInEditMode,object_id=Key(id),native_id_class_name=nativeClass,actual_native_type=nativeType,
                    known_helper=false,role="unclassified_containing_item",get_item_geometry_wcs=itemGeometry,lookup_error=lookupError});
            }
            StoreObject(id,value,abstractItem);
            bool owned=abstractItem||value!.DbEntity.Document.ID==context.ID;string? reason=owned?null:"native_member_document_differs_from_actual_sketch_context; ownership_not_inferred";
            // Edit only a direct member in its actual sketch context, never a
            // current projected source or an input shared by another sketch.
            if(projectedSources.TryGetValue(id,out var projectionId))
            {owned=false;reason="actual_current_projection_source: "+Key(projectionId)+"; use source drawing constraints";}
            if(sharedMembers.TryGetValue(id,out var peerId))
            {owned=false;reason="actual_other_sketch_member: "+Key(peerId)+"; shared input is outside this edit";}
            // The selected-sketch getter explicitly defines false as WCS.
            // A resident DbGeometry copy does not establish the same frame
            // during editor materialization, including its construction point.
            using var geometry=sketch.GetItemGeometry(id,false);Need(geometry!=null,"SKETCH_GEOMETRY_UNAVAILABLE","Actual native item WCS geometry is unavailable.");
            Need(!abstractSpline||geometry.GeometryType==G.EntityGeomType.kSpline,"SKETCH_GEOMETRY_UNAVAILABLE","An abstract Spline changed its native geometry type during current observation.");
            string geometryType=geometry.GeometryType.ToString();
            if(IsPoint(geometry))
            {Need(abstractItem||value!.DbEntity.Document.ID==context.ID,"SKETCH_BINDING_UNAVAILABLE","A native construction point belongs to another edit context.");ConstructionPoint(id,geometry);continue;}
            if(geometry.GeometryType==G.EntityGeomType.kLine)
            {using var line=geometry.LineSeg;Add(id,id,null,geometryType,Wire.Element(SketchGeometryReader.Line(line)),owned,reason);}
            else if(geometry.GeometryType==G.EntityGeomType.kCircArc)
            {using var arc=geometry.CircArc;Add(id,id,null,geometryType,Wire.Element(SketchGeometryReader.Arc(arc)),owned,reason);}
            else if(geometry.GeometryType==G.EntityGeomType.kSpline)
            {
                using var spline=geometry.Nurb;var observed=NativeSketchCurveGeometry445.Spline(spline,cancel);
                Need(!abstractSpline||ModelParameterSnapshot.Equivalent(abstractSplines[id],observed),"SKETCH_GEOMETRY_UNAVAILABLE","The abstract Spline's full WCS item geometry differs from its actual geometry-ID map observation.");
                Add(id,id,null,geometryType,observed,owned,reason);
            }
            else if(geometry.GeometryType==G.EntityGeomType.kEllipticArc)
            {using var ellipse=geometry.EllipArc;Add(id,id,null,geometryType,NativeSketchCurveGeometry445.Ellipse(ellipse,cancel),owned,reason);}
            else if(geometry.GeometryType==G.EntityGeomType.kPolyline)
            {
                using var polyline=geometry.Polyline;using var segments=polyline.Segments;
                for(int index=0;index<segments.Count;index++)
                {
                    cancel();var member=ConstraintInfo.MakePolylineSegmentId(id,index);Need(!member.IsNull,"SKETCH_BINDING_UNAVAILABLE","SDK segment reference is unavailable.");
                    if(segments.SegType((uint)index)==G.SegPlType.sgLine){using var line=segments.GetLineSegAt((uint)index);Add(member,id,index,geometryType,Wire.Element(SketchGeometryReader.Line(line)),owned,reason);}
                    else if(segments.SegType((uint)index)==G.SegPlType.sgArc){using var arc=segments.GetArcSegAt((uint)index);Add(member,id,index,geometryType,Wire.Element(SketchGeometryReader.Arc(arc)),owned,reason);}
                    else if(segments.SegType((uint)index)==G.SegPlType.sgNurb){using var spline=segments.GetNurbSegAt((uint)index);Add(member,id,index,geometryType,NativeSketchCurveGeometry445.Spline(spline,cancel),owned,reason);}
                    else if(segments.SegType((uint)index)==G.SegPlType.sgEllipse){using var ellipse=segments.GetEllipsSegAt((uint)index);Add(member,id,index,geometryType,NativeSketchCurveGeometry445.Ellipse(ellipse,cancel),owned,reason);}
                    else throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE","A native polyline segment lacks its supported Line/Arc adapter.");
                }
            }
            else throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE","Native input is outside the supported sketch curve adapter.");
        }
        foreach(var id in projections.Distinct().OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var projection=id.GetObjectOfType<ProjectionSketchEnt>();
            Need(projection!=null&&projection.ID==id&&projection.HostSketchID==sketch.ID&&projection.DbEntity.Document?.ID==owner.ID&&!projection.SrcObjectID.IsNull&&projection.Status.ToString()=="Ok"&&!projection.IsSuppressed,
                "SKETCH_BINDING_UNAVAILABLE","A reference projection lacks its exact selected-sketch/source/owner binding.");
            using var geometry=sketch.GetItemGeometry(id,false);Need(geometry!=null,"SKETCH_GEOMETRY_UNAVAILABLE","Reference projection geometry is unavailable in WCS.");
            var objectState=new{id=Key(id),owner_document_id=Key(owner.ID),source_object_id=Key(projection.SrcObjectID),layer=projection.DbEntity.Layer,visibility=projection.DbEntity.Visibility,
                native_metadata=ModelSketchLifecycleReader362.Metadata(id,projection.DbEntity,owner,state,tx,cancel)};budget.Add(objectState);objectStates.Add(objectState);objectStateIds.Add(id);
            const string reason="retained_same_sketch_projection; reference_only; native_solve_must_preserve_its_geometry_and_source";
            if(geometry.GeometryType==G.EntityGeomType.kLine){using var line=geometry.LineSeg;Add(id,id,null,geometry.GeometryType.ToString(),Wire.Element(SketchGeometryReader.Line(line)),false,reason,true);}
            else if(geometry.GeometryType==G.EntityGeomType.kCircArc){using var arc=geometry.CircArc;Add(id,id,null,geometry.GeometryType.ToString(),Wire.Element(SketchGeometryReader.Arc(arc)),false,reason,true);}
            else throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE","This reference projection needs its own supported native geometry adapter.");
        }
        foreach(var id in all.Except(containing).Except(projections).Except(helpers).Distinct().OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();using var geometry=sketch.GetItemGeometry(id,false);if(geometry==null)continue;
            // The SDK can create an origin point on editor entry. Observe its
            // actual construction identity and geometry; never alias its ID to
            // the origin merely because a relation happens to be GeomFix.
            if(IsPoint(geometry))ConstructionPoint(id,geometry);
        }
        // Some hosts expose the fixed origin only through relation references,
        // rather than the construction-list overload. GetItemGeometry is the
        // documented exact selected-sketch lookup, not a WCS-point alias or a
        // document-wide geometry lookup. It must return actual point geometry.
        foreach(var id in fixedPoints.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();if(construction.Any(c=>c.Id==id)||members.Any(m=>m.Id==id||m.ObjectId==id))continue;
            using var geometry=sketch.GetItemGeometry(id,false);if(geometry!=null&&IsPoint(geometry)&&sketch.ContainsObject(id))ConstructionPoint(id,geometry);
        }
        var helperStates=new List<object>();
        foreach(var helper in helperRoles.OrderBy(row=>Key(row.Id),StringComparer.OrdinalIgnoreCase).ThenBy(row=>row.Role,StringComparer.Ordinal).ThenBy(row=>Key(row.Relation),StringComparer.OrdinalIgnoreCase))
        {
            cancel();var helperManager=manager??throw new DomainException("CONSTRAINTS_UNAVAILABLE","An actual helper role lacks its scoped native manager.");JsonElement metadata;string? nativeType=null,nativeDocument=null;
            try
            {
                var value=helper.Id.GetObject();nativeType=value?.GetType().FullName;
                if(value!=null)Need(value.ID==helper.Id,"OBJECT_IDENTITY_MISMATCH","A native relation helper resolves to a different actual object.");
                McDbObject? nativeEntity=value is McEntity entity?entity.DbObject:value is DbGeometry geometry?geometry.DbEntity:null;
                var entityOwner=nativeEntity?.Document;nativeDocument=entityOwner?.ID.ToString();
                if(nativeEntity==null||entityOwner==null)metadata=Wire.Element(new{scope="no_proved_resident_native_helper_metadata"});
                else
                    metadata=ModelSketchLifecycleReader362.Metadata(helper.Id,nativeEntity,entityOwner,state,tx,cancel);
            }
            catch(OperationCanceledException){throw;}
            catch(DomainException){throw;}
            catch(Exception error){metadata=Wire.Element(new{scope="native_helper_metadata_unavailable",exception_type=error.GetType().Name,exception_hresult=error.HResult});}
            var row=new{id=Key(helper.Id),role=helper.Role,constraint_id=Key(helper.Relation),constraint_type=helper.Type,document_id=Key(helperManager.DocumentId),sketch_id=Key(sketch.ID),scope_id=helperManager.ScopeId.IsNull?null:Key(helperManager.ScopeId),network=helperManager.ConstraintsNetworkType.ToString(),
                containing_member=containing.Contains(helper.Id),including_construction=all.Contains(helper.Id),actual_native_type=nativeType,actual_native_document_id=nativeDocument,native_metadata=metadata};
            budget.Add(row);helperStates.Add(row);
        }
        Need(members.Count>0&&members.Select(row=>row.Id).Distinct().Count()==members.Count,"SKETCH_BINDING_UNAVAILABLE","No distinct current native inputs were observed.");
        var stored=Wire.Element(new{sketch_id=Key(sketch.ID),owner_document_id=Key(sketch.DbEntity.Document.ID),member_context_document_id=Key(context.ID),plane,members=members.Select(Describe).ToArray(),objects=objectStates.Select(value=>Wire.Element(value)).OrderBy(row=>row.GetProperty("id").GetString(),StringComparer.OrdinalIgnoreCase).ToArray(),construction=construction.Select(c=>new{id=Key(c.Id),geometry=c.Geometry}).ToArray(),constraint_helpers=helperStates,projection_ids=projections.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray()});budget.Add(stored);
        return new(sketch,plane,members.ToArray(),stored,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Wire.Json))),construction.ToArray(),helpers.OrderBy(Key,StringComparer.OrdinalIgnoreCase).ToArray());
    }
    internal static NativeSketchMember385 Describe(Member value)=>new(Key(value.Id),Key(value.ObjectId),value.NativeType,value.Segment,value.Geometry,value.Owned,value.Reason,reference_only:value.ReferenceOnly);
    internal static NativeSketchBindings385 Observe(PlanarSketch sketch,DocumentState state,Db.Transaction tx,Action cancel)
    {
        try{var value=Capture(sketch,state,tx,cancel);return new(true,null,value.Token,value.Members.Select(Describe).ToArray(),"bridge_document_revision","exact native sketch item IDs and WCS geometry, resident or abstract; segments use SDK MakePolylineSegmentId; no inspection-ID alias");}
        catch(OperationCanceledException){throw;}
        catch(Exception error){return new(false,error is DomainException d?d.Error.code+": "+d.Message:error.GetType().Name,"",[],"bridge_document_revision","native inputs unavailable; no edit readiness inferred");}
    }
    internal static Constraints2D Manager(PlanarSketch sketch,bool allowUnlinkedEditScope=false)
    {
        var owner=sketch.IsInEditMode?McDocumentsManager.GetActiveSheet():sketch.DbEntity.Document;
        Need(owner!=null&&!owner.ID.IsNull,"CONSTRAINT_SCOPE_MISMATCH","The actual current sketch sheet/document is unavailable.");using var plane=sketch.Plane;
        var manager=owner.Get2dConstraintsManager(sketch.IsInEditMode?Constraint2dNetType.MCS:Constraint2dNetType.Auto,sketch.ID,plane);
        Need(manager!=null,"CONSTRAINTS_UNAVAILABLE","The actual current sketch manager is unavailable.");
        try{RequireManagerScope(manager,sketch,owner,allowUnlinkedEditScope);return manager;}
        catch{manager.Dispose();throw;}
    }
    internal static void RequireManagerScope(Constraints2D manager,PlanarSketch sketch,McDocument owner,bool allowUnlinkedEditScope=false)
    {
        bool documentMatches=manager.DocumentId==owner.ID,sketchMatches=manager.PlanarSketchId==sketch.ID;
        bool networkMatches=!sketch.IsInEditMode||manager.ConstraintsNetworkType==Constraint2dNetType.MCS;
        // The managed getter used by HelloMultiCAD3dSheetMetal obtains a
        // manager before the first AddConstraint. It does not expose the
        // native force-create-data argument. PlanarSketchId describes a linked
        // relation network, not a guaranteed pre-creation acquisition receipt.
        // Managed acquisitions have been observed with missing linkage even
        // when fixed construction exists; no public pre-add guarantee was found.
        // Only an explicit editor caller may admit
        // this acquisition; every baseline/requested reference is independently
        // witnessed by exact current sketch geometry before relation writes.
        // A different non-null sketch fails. Persistent Auto reads stay strict.
        bool scopePresent=!manager.ScopeId.IsNull;
        bool freshAllowance=allowUnlinkedEditScope&&sketch.IsInEditMode&&
            (sketchMatches||manager.PlanarSketchId.IsNull);
        if(!documentMatches||!networkMatches||!(sketchMatches&&scopePresent||freshAllowance))
            throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","The native manager does not bind the exact current sheet, sketch and requested relation scope.",false,new{
                expected_document_id=Key(owner.ID),actual_document_id=Key(manager.DocumentId),document_matches=documentMatches,
                expected_sketch_id=Key(sketch.ID),actual_sketch_id=Key(manager.PlanarSketchId),sketch_matches=sketchMatches,
                actual_scope_id=Key(manager.ScopeId),scope_present=scopePresent,
                actual_network_type=manager.ConstraintsNetworkType.ToString(),network_matches=networkMatches,
                sketch_edit_mode=sketch.IsInEditMode,unlinked_edit_scope_allowed=freshAllowance});
    }
    // Add(false) editor batch only: preserve the exact acquired sheet/MCS/sketch
    // context while native linkage is materialized. Full relation/member proofs
    // precede writes and the persistent post-exit manager remains strict.
    internal static void RequireEditBatchContext(Constraints2D manager,PlanarSketch sketch,McDocument owner)
    {
        bool documentMatches=manager.DocumentId==owner.ID;
        bool networkMatches=manager.ConstraintsNetworkType==Constraint2dNetType.MCS;
        bool sketchCompatible=manager.PlanarSketchId.IsNull||manager.PlanarSketchId==sketch.ID;
        if(!sketch.IsInEditMode||!documentMatches||!networkMatches||!sketchCompatible)
            throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","The relation batch left its actual edit sheet, MCS network or requested sketch context.",false,new{
                expected_document_id=Key(owner.ID),actual_document_id=Key(manager.DocumentId),document_matches=documentMatches,
                expected_sketch_id=Key(sketch.ID),actual_sketch_id=Key(manager.PlanarSketchId),sketch_compatible=sketchCompatible,
                actual_scope_id=Key(manager.ScopeId),actual_network_type=manager.ConstraintsNetworkType.ToString(),network_matches=networkMatches,
                sketch_edit_mode=sketch.IsInEditMode});
    }
    internal static ConstraintType Type(string kind)=>kind switch
    {"horizontal"=>ConstraintType.GeomHorizontal,"vertical"=>ConstraintType.GeomVertical,"parallel"=>ConstraintType.GeomParallel,"perpendicular"=>ConstraintType.GeomPerpendicular,"collinear"=>ConstraintType.GeomCollinear,"tangent"=>ConstraintType.GeomTangent,"smooth"=>ConstraintType.GeomSmooth,"equal"=>ConstraintType.GeomEqual,"concentric"=>ConstraintType.GeomConcentric,"coincident"=>ConstraintType.GeomCoincident,"fix_point" or "fix"=>ConstraintType.GeomFix,"symmetric"=>ConstraintType.GeomSymmetric,"radius"=>ConstraintType.DimRadial,"diameter"=>ConstraintType.DimDiametrical,"length" or "distance"=>ConstraintType.DimAligned,"distance_x"=>ConstraintType.DimHorizontal,"distance_y"=>ConstraintType.DimVertical,"angle"=>ConstraintType.DimAngular,_=>throw new DomainException("INVALID_ARGUMENT","Unsupported internal relation.")};
    // Boundary curves are normalized counterclockwise. Constraint point names
    // are actual native endpoints; an opposing native arc normal reverses the
    // boundary order, so map only endpoint names, never the geometry itself.
    internal static string? EndpointName(Snapshot inputs,Member member,string? name)
    {
        if(member.Curve.Kind!="arc"||name is not ("start" or "end"))return name;
        var normal=DrawingWorkplane.Normal(inputs.Plane);var native=member.Geometry.GetProperty("normal");
        double orientation=native[0].GetDouble()*normal.X+native[1].GetDouble()*normal.Y+native[2].GetDouble()*normal.Z;
        return orientation<0?(name=="start"?"end":"start"):name;
    }
    internal static Point2 PointLocal(Snapshot inputs,Member member,string? name)=>DrawingProfileGeometry.Point(member.Curve,EndpointName(inputs,member,name));
    internal static G.Point3d Point(Snapshot inputs,Member member,string? name)=>World(inputs.Plane,PointLocal(inputs,member,name));
    internal static string? PointRole(SubentSubType type)=>type switch
    {SubentSubType.Start=>"start",SubentSubType.End=>"end",SubentSubType.Center=>"center",_=>null};
    internal static string? PointRole(Snapshot inputs,Member member,IConstraintObjectInfo reference)
    {
        var point=reference.PointRefOnObject;
        return ResolvePointRole(inputs,member,!reference.SubentId.IsNull,reference.SubentId.IsNull?null:ConstraintInfo.GetSubentSubType(reference.SubentId).ToString(),
            new(point.X,point.Y,point.Z));
    }
    internal static string? PointRole(Snapshot inputs,Member member,JsonElement reference)
    {
        var point=reference.GetProperty("point");Point3? raw=point.ValueKind==JsonValueKind.Array&&point.GetArrayLength()==3?new(point[0].GetDouble(),point[1].GetDouble(),point[2].GetDouble()):null;
        bool hasSubentity=reference.GetProperty("subentity_id").ValueKind!=JsonValueKind.Null;
        return ResolvePointRole(inputs,member,hasSubentity,hasSubentity?reference.GetProperty("subentity_type").GetString():null,raw);
    }
    static string? ResolvePointRole(Snapshot inputs,Member member,bool hasSubentity,string? subtype,Point3? reported)
    {
        var actual=new Dictionary<string,Point3>(StringComparer.Ordinal);
        foreach(string role in new[]{"start","end","center"})
        {try{var point=Point(inputs,member,role);actual.Add(role,new(point.X,point.Y,point.Z));}catch(DomainException){}}
        return NativeCurvePointBinding445.Resolve(hasSubentity,subtype,member.Curve.Kind=="spline",reported,actual);
    }
    internal static DrawingAngularGeometry.Sector Angular(SketchConstraintCurve first,SketchConstraintCurve second)
    {
        Need(first.Kind=="line"&&second.Kind=="line","OUT_OF_SCOPE","Angular controls need two actual Lines.");var a=first.Start!;var b=second.Start!;
        double ux=first.End!.X-a.X,uy=first.End.Y-a.Y,vx=second.End!.X-b.X,vy=second.End.Y-b.Y,la=Math.Sqrt(ux*ux+uy*uy),lb=Math.Sqrt(vx*vx+vy*vy);
        Need(double.IsFinite(la)&&double.IsFinite(lb)&&la>1e-7&&lb>1e-7,"INVALID_GEOMETRY","Angular native inputs are degenerate.");ux/=la;uy/=la;vx/=lb;vy/=lb;double cross=ux*vy-uy*vx,angle=Math.Atan2(Math.Abs(cross),Math.Clamp(ux*vx+uy*vy,-1,1));
        Need(angle>0&&angle<Math.PI,"OUT_OF_SCOPE","Current parallel/opposite rays have no observed finite angular sector for annotation.");double along=((b.X-a.X)*vy-(b.Y-a.Y)*vx)/cross;
        var center=new Point2(a.X+along*ux,a.Y+along*uy);double sx=ux+vx,sy=uy+vy,length=Math.Sqrt(sx*sx+sy*sy),radius=Math.Min(la,lb)*.4;
        var annotation=new Point2(center.X+radius*sx/length,center.Y+radius*sy/length);Need(new[]{center.X,center.Y,annotation.X,annotation.Y}.All(double.IsFinite),"OUT_OF_SCOPE","The actual angular sector has no finite annotation position.");return new(angle,center,annotation);
    }
    static SketchConstraintSpec NativeSmoothSpec(SketchConstraintSpec spec,Snapshot inputs)
    {
        if(spec.Kind!="smooth")return spec;
        string Kind(string id)=>inputs.Members.Single(row=>Key(row.Id).Equals(id,StringComparison.OrdinalIgnoreCase)).Curve.Kind;
        return SplineConstraintGeometry445.NativeOrder(spec,Kind(spec.First),Kind(spec.Second!));
    }
    internal static ConstraintInfo Info(SketchConstraintSpec spec,Snapshot inputs)
    {
        spec=NativeSmoothSpec(spec,inputs);
        var a=inputs.Members.Single(row=>Key(row.Id).Equals(spec.First,StringComparison.OrdinalIgnoreCase));var b=spec.Second==null?null:inputs.Members.Single(row=>Key(row.Id).Equals(spec.Second,StringComparison.OrdinalIgnoreCase));
        var c=spec.Third==null?null:inputs.Members.Single(row=>Key(row.Id).Equals(spec.Third,StringComparison.OrdinalIgnoreCase));
        if(!(a.Owned||a.ReferenceOnly)||b!=null&&!(b.Owned||b.ReferenceOnly)||c!=null&&!(c.Owned||c.ReferenceOnly)||!a.Owned&&(b==null||!b.Owned))
        {
            // Three requested inputs at most; reasons are fixed text plus actual
            // native IDs. Never embed geometry or complete metadata in errors.
            object DescribeUnavailable(Member value)=>new{member_id=Key(value.Id),object_id=Key(value.ObjectId),editable_internal=value.Owned,
                unavailable_reason=value.Reason,owner_document_id=inputs.Stored.GetProperty("objects").EnumerateArray().Single(row=>row.GetProperty("id").GetString()==Key(value.ObjectId)).GetProperty("owner_document_id").GetString()};
            throw new DomainException("OUT_OF_SCOPE","A relation must include an owned internal input; exact same-sketch projections are immutable references, never drawing-source edit targets.",false,
                new{component="internal_native_input_binding",sketch_id=Key(inputs.Sketch.ID),sketch_owner_document_id=inputs.Stored.GetProperty("owner_document_id").GetString(),
                    member_context_document_id=inputs.Stored.GetProperty("member_context_document_id").GetString(),first=DescribeUnavailable(a),second=b==null?null:DescribeUnavailable(b),third=c==null?null:DescribeUnavailable(c)});
        }
        Need(spec.Kind!="fix"||a.Owned&&!a.ReferenceOnly&&a.Id==a.ObjectId&&a.Segment==null,"OUT_OF_SCOPE","Whole-object fix requires one complete independently owned native Line/Circle/Arc; a polyline segment or projection is not a complete owned object.");
        if(spec.Kind=="symmetric")
        {Need(b!=null&&c!=null,"INVALID_ARGUMENT","Symmetry requires two members and its actual Line axis.");SketchSymmetryGeometry414.Types(a.Curve,b.Curve,c.Curve);}
        var type=Type(spec.Kind);ConstraintInfo info;
        if(spec.Kind=="length")info=new(type,a.Id,a.Id,Point(inputs,a,"start"),Point(inputs,a,"end"));
        else if(spec.Kind is "coincident" or "smooth" or "distance" or "distance_x" or "distance_y")info=new(type,a.Id,b!.Id,Point(inputs,a,spec.FirstPoint),Point(inputs,b,spec.SecondPoint));
        else if(spec.Kind=="fix_point")info=new(type,a.Id,Point(inputs,a,spec.FirstPoint));
        // Installed AddGeomFix(id) uses this one-object constructor. Installed
        // AddGeomSymmetric(a,b,axis) uses the pair constructor plus ThirdObject.
        // Keep the existing AddConstraint(false) batch and one grouped solve.
        else if(spec.Kind=="symmetric"){info=new(type,a.Id,b!.Id);info.ThirdObject.ObjectId=c!.Id;}
        else if(spec.Kind=="angle")info=new(type,a.Id,b!.Id){PointDimPosition=World(inputs.Plane,Angular(a.Curve,b.Curve).Annotation)};
        else info=b==null?new(type,a.Id):new(type,a.Id,b.Id);
        if(spec.Value.HasValue)info.Param=spec.Value.Value;return info;
    }
    internal static void ValidateInputs(SketchConstraintSpec[] specs,Snapshot inputs)
    {
        foreach(var spec in specs)
        {
            using var info=Info(spec,inputs);var a=inputs.Members.Single(row=>Key(row.Id).Equals(spec.First,StringComparison.OrdinalIgnoreCase)).Curve;
            var b=spec.Second==null?null:inputs.Members.Single(row=>Key(row.Id).Equals(spec.Second,StringComparison.OrdinalIgnoreCase)).Curve;
            bool line=spec.Kind is "horizontal" or "vertical" or "parallel" or "perpendicular" or "collinear" or "length" or "angle";
            Need(!line||a.Kind=="line"&&(b==null||b.Kind=="line"),"OUT_OF_SCOPE","This relation requires native Line members.");
            Need(spec.Kind is not ("radius" or "diameter")||a.Kind is "circle" or "arc","OUT_OF_SCOPE","Radial controls require native Circle/Arc geometry.");
            Need(spec.Kind!="concentric"||(a.Kind is "circle" or "arc")&&(b!.Kind is "circle" or "arc"),"OUT_OF_SCOPE","Concentricity requires circular members.");
            Need(spec.Kind!="equal"||a.Kind=="line"&&b!.Kind=="line"||(a.Kind is "circle" or "arc")&&(b!.Kind is "circle" or "arc"),"OUT_OF_SCOPE","Equality requires two Line or two circular members.");
            Need(spec.Kind!="tangent"||(a.Kind is "line" or "circle" or "arc")&&(b!.Kind is "line" or "circle" or "arc"),"OUT_OF_SCOPE","Whole-curve Tangent uses analytic Line/Circle/Arc members; use endpoint-bound Smooth for a native Spline.");
            if(spec.Kind=="smooth")ValidateSolved([spec],inputs);
        }
    }
    internal static bool SameReferences(ConstraintInfo actual,ConstraintInfo expected,bool comparePoints=false)
    {
        bool Same(IConstraintObjectInfo? a,IConstraintObjectInfo? b)
        {if(a==null||a.ObjectId.IsNull)return b==null||b.ObjectId.IsNull;return b!=null&&a.ObjectId==b.ObjectId&&a.SubentId==b.SubentId&&a.PolylineSegmentIndex==b.PolylineSegmentIndex&&(!comparePoints||NativeSketchRelations410.SamePoint(a.PointRefOnObject,b.PointRefOnObject));}
        return actual.Type==expected.Type&&Same(actual.FirstObject,expected.FirstObject)&&Same(actual.SecondObject,expected.SecondObject)&&Same(actual.ThirdObject,expected.ThirdObject);
    }
    internal static bool DirectCurveReference(Snapshot inputs,string id)
    {
        var matches=inputs.Members.Where(member=>Key(member.Id).Equals(id,StringComparison.OrdinalIgnoreCase)||Key(member.ObjectId).Equals(id,StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length==1&&matches[0].Id==matches[0].ObjectId&&matches[0].Segment==null&&matches[0].Curve.Kind is "line" or "arc" or "circle" or "spline" or "ellipse";
    }
    internal static bool RequestedReferences(ConstraintInfo actual,ConstraintInfo expected,SketchConstraintSpec spec,Snapshot inputs,bool comparePoints=false)
    {
        // Info uses Spline-first native order. Compare the same complete roles
        // even when the public request placed the neighbouring curve first.
        spec=NativeSmoothSpec(spec,inputs);
        string? first=spec.Kind=="length"?"start":spec.FirstPoint,second=spec.Kind=="length"?"end":spec.SecondPoint;
        bool Same(IConstraintObjectInfo? a,IConstraintObjectInfo? b,string? role)
        {
            if(a==null||a.ObjectId.IsNull)return b==null||b.ObjectId.IsNull;
            if(b==null||a.ObjectId!=b.ObjectId)return false;
            // SDK PolylineSegmentIndex applies only when the curve is a
            // polyline. Direct native Line/Circle/Arc receipts can contain0
            // while the request ctor has-1; these are not segment identities.
            // Real polyline members retain their exact index/parent binding.
            if(a.PolylineSegmentIndex!=b.PolylineSegmentIndex&&!DirectCurveReference(inputs,Key(a.ObjectId)))return false;
            if(!b.SubentId.IsNull&&a.SubentId!=b.SubentId)return false;
            if(role==null)return a.SubentId==b.SubentId&&(!(comparePoints||spec.Kind=="fix")||NativeSketchRelations410.SamePoint(a.PointRefOnObject,b.PointRefOnObject));
            var members=inputs.Members.Where(m=>m.Id==a.ObjectId||m.ObjectId==a.ObjectId&&m.Segment==a.PolylineSegmentIndex).ToArray();
            if(members.Length!=1||PointRole(inputs,members[0],a)!=role)return false;
            // SDK SubentId identifies the native endpoint/center. Returned
            // PointRefOnObject has no documented WCS frame guarantee and was
            // observed in the persistent sketch plane. Validate the decoded
            // role against actual WCS geometry; retain raw coordinates only as
            // diagnostics. Null selectors still require strict WCS pick proof.
            return NativeSketchRelations410.SamePoint(Point(inputs,members[0],role),b.PointRefOnObject);
        }
        return actual.Type==expected.Type&&Same(actual.ThirdObject,expected.ThirdObject,null)&&
            (Same(actual.FirstObject,expected.FirstObject,first)&&Same(actual.SecondObject,expected.SecondObject,second)||
             spec.Kind=="smooth"&&Same(actual.FirstObject,expected.SecondObject,second)&&Same(actual.SecondObject,expected.FirstObject,first));
    }
    internal static JsonElement DescribeRelation(ConstraintInfo info)
    {
        object? Reference(IConstraintObjectInfo? value)
        {if(value==null||value.ObjectId.IsNull)return null;var p=value.PointRefOnObject;return new{object_id=Key(value.ObjectId),subentity_id=value.SubentId.IsNull?null:value.SubentId.ToString(),subentity_type=value.SubentId.IsNull?null:ConstraintInfo.GetSubentSubType(value.SubentId).ToString(),segment=value.PolylineSegmentIndex,point=double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)?new[]{p.X,p.Y,p.Z}:null,point_coordinate_basis="native_reference_frame_unspecified"};}
        return Wire.Element(new{type=info.Type.ToString(),first=Reference(info.FirstObject),second=Reference(info.SecondObject),third=Reference(info.ThirdObject),value=double.IsFinite(info.Param)?(double?)info.Param:null});
    }
    internal static void ValidateSolved(SketchConstraintSpec[] specs,Snapshot inputs)
    {
        foreach(var spec in specs)
        {
            if(spec.Kind=="fix")continue; // complete before/after geometry is checked by FixedRequested
            if(spec.Kind=="symmetric")
            {SketchConstraintCurve Curve(string id)=>inputs.Members.Single(m=>Key(m.Id).Equals(id,StringComparison.OrdinalIgnoreCase)).Curve;SketchSymmetryGeometry414.Validate(Curve(spec.First),Curve(spec.Second!),Curve(spec.Third!));}
            else if(spec.Kind=="angle")
            {var a=inputs.Members.Single(m=>Key(m.Id).Equals(spec.First,StringComparison.OrdinalIgnoreCase));var b=inputs.Members.Single(m=>Key(m.Id).Equals(spec.Second,StringComparison.OrdinalIgnoreCase));Need(ModelParameterSnapshot.Near(Angular(a.Curve,b.Curve).Radians,spec.Value!.Value),"CONSTRAINT_SOLVE_FAILED","Native angular geometry does not match the requested sector in radians.");}
            else
            {
                var first=inputs.Members.Single(m=>Key(m.Id).Equals(spec.First,StringComparison.OrdinalIgnoreCase));
                var second=spec.Second==null?null:inputs.Members.Single(m=>Key(m.Id).Equals(spec.Second,StringComparison.OrdinalIgnoreCase));
                var canonical=spec with{FirstPoint=EndpointName(inputs,first,spec.FirstPoint),SecondPoint=second==null?null:EndpointName(inputs,second,spec.SecondPoint)};
                DrawingProfileGeometry.Validate([canonical],inputs.Members.Select(row=>row.Curve).ToArray());
            }
        }
    }
    internal static void ValidateSmoothRelations(JsonElement relations,Snapshot inputs,Action cancel)
    {
        foreach(var row in relations.GetProperty("constraints").EnumerateArray())
        {
            cancel();if(row.GetProperty("type").GetString()!="GeomSmooth")continue;
            (Member Member,string Role) Endpoint(string field)
            {
                var reference=row.GetProperty(field);
                Need(reference.ValueKind==JsonValueKind.Object,"CONSTRAINT_POINT_REFERENCE_CHANGED","A native Smooth operand is unavailable.");
                string id=reference.GetProperty("object_id").GetString()!;int segment=reference.GetProperty("segment").GetInt32();
                var members=inputs.Members.Where(m=>Key(m.Id).Equals(id,StringComparison.OrdinalIgnoreCase)||Key(m.ObjectId).Equals(id,StringComparison.OrdinalIgnoreCase)&&(m.Segment==null||m.Segment==segment)).ToArray();
                Need(members.Length==1,"CONSTRAINT_POINT_REFERENCE_CHANGED","A native Smooth operand is not one exact current sketch member.");
                var role=PointRole(inputs,members[0],reference);
                if(role is not ("start" or "end"))
                {
                    var member=members[0];
                    object Endpoint(string name)
                    {
                        try{var point=Point(inputs,member,name);return new{role=name,wcs=new[]{point.X,point.Y,point.Z}};}
                        catch(DomainException){return new{role=name,wcs=(double[]?)null};}
                    }
                    throw new DomainException("CONSTRAINT_POINT_REFERENCE_CHANGED","A native Smooth operand lost its actual endpoint selector.",false,
                        new{sketch_id=Key(inputs.Sketch.ID),constraint_id=row.GetProperty("id").GetString(),native_type=row.GetProperty("type").GetString(),operand=field,
                            native_first=row.GetProperty("first"),native_second=row.GetProperty("second"),member_id=Key(member.Id),curve_kind=member.Curve.Kind,
                            actual_endpoints=new[]{Endpoint("start"),Endpoint("end")},actual_wcs_frame=inputs.Plane,
                            verification="decoded native selector or strict unique finite WCS Spline endpoint pick; no frame transform or arbitrary endpoint inferred"});
                }
                return(members[0],role);
            }
            var a=Endpoint("first");var b=Endpoint("second");
            ValidateSolved([new("smooth",Key(a.Member.Id),Key(b.Member.Id),a.Role,b.Role,null)],inputs);
        }
    }
    internal static void FixedRequested(SketchConstraintSpec[] specs,Snapshot before,Snapshot after)
    {
        foreach(var spec in specs.Where(s=>s.Kind is "fix_point" or "fix"))
        {
            var old=before.Members.Single(m=>m.Id.ToString().Equals(spec.First,StringComparison.OrdinalIgnoreCase));var current=after.Members.Single(m=>m.Id==old.Id);
            if(spec.Kind=="fix")
            {Need(SketchGeometryComparison.Same(old.Geometry,current.Geometry),"CONSTRAINT_SOLVE_FAILED","The requested whole-object fixed geometry changed during native solve.");continue;}
            Need(DrawingProfileGeometry.Same(PointLocal(before,old,spec.FirstPoint),PointLocal(after,current,spec.FirstPoint)),"CONSTRAINT_SOLVE_FAILED","The requested fixed native point moved during solve.");
        }
    }
}
