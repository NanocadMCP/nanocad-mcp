using Multicad;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;

namespace NanoCADBridge;

// Optional current history source classification. It does not assign a
// HistoryItem status, create a relation, enter edit mode or solve a manager.
internal static class NativeSketchHistory385
{
    internal static NativePathSources.Inspection Complete(McObjectId[] contents,NativePathSources.Inspection prior,Action? cancel)
    {
        var remaining=contents.Where(id=>id.GetObjectOfType<HistoryItem>()==null&&!prior.Sources.Contains(id.ToString())&&!prior.Failures.ContainsKey(id.ToString())).ToHashSet();
        if(remaining.Count==0)return prior;Action check=cancel??(()=>{});var sources=new HashSet<string>(prior.Sources,StringComparer.OrdinalIgnoreCase);var details=new Dictionary<string,object>(prior.Details,StringComparer.OrdinalIgnoreCase);var bytes=new ModelInventoryBudget(check);
        void Add(McObjectId id,object value)
        {check();bytes.Add(value);string key=id.ToString();sources.Add(key);details[key]=value;remaining.Remove(id);}
        foreach(var sketchId in contents)
        {
            check();var sketch=sketchId.GetObjectOfType<PlanarSketch>();if(sketch==null||sketch.ID!=sketchId||sketch.IsInEditMode||sketch.DbEntity.Document==null)continue;
            var objectMembers=sketch.GetContainingObjects(true,false,false);if(objectMembers==null)continue;ModelInventoryBudget.Ids(objectMembers,check);
            Dictionary<McObjectId,System.Text.Json.JsonElement>? abstractSplines=null;
            try{abstractSplines=NativeSketchMembership445.AbstractSplines(sketch,check);}
            catch(OperationCanceledException){throw;}
            catch(Exception){/* Optional source classification leaves unknown geometry unverified. */}
            var containing=objectMembers.Concat(abstractSplines?.Keys.AsEnumerable()??Enumerable.Empty<McObjectId>()).Distinct().ToArray();
            var includingConstruction=sketch.GetContainingObjects(true,true,true);if(includingConstruction==null)continue;ModelInventoryBudget.Ids(includingConstruction,check);
            var constructionPoints=new HashSet<McObjectId>();
            foreach(var id in includingConstruction.Except(containing))
            {
                check();using var geometry=sketch.GetItemGeometry(id,false);if(geometry==null||!NativeSketchMembers385.IsPoint(geometry))continue;var p=geometry.Point;
                if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z))continue;constructionPoints.Add(id);
                if(remaining.Contains(id))Add(id,new{kind="native_sketch_construction_point",source_id=id.ToString(),sketch_id=sketchId.ToString(),point_wcs=new[]{p.X,p.Y,p.Z},verification="actual_native_construction_membership_and_GetItemGeometry; no_BasePointID_alias_or_edit_readiness"});
            }
            foreach(var id in containing.Where(remaining.Contains))
            {
                check();var value=id.GetObjectOfType<DbGeometry>();bool abstractItem=value==null&&id.GetObject()==null;
                bool abstractSpline=abstractSplines?.ContainsKey(id)==true;
                if(id.IsNull||!(abstractSpline?abstractItem:sketch.ContainsObject(id))||!(abstractItem||value!=null&&value.ID==id&&value.DbEntity.Document!=null))continue;
                using var geometry=sketch.GetItemGeometry(id,false);if(geometry==null||(geometry.GeometryType&G.EntityGeomType.kCurveEntities)==0)continue;
                if(abstractSpline)
                {
                    if(geometry.GeometryType!=G.EntityGeomType.kSpline)continue;using var spline=geometry.Nurb;
                    if(!ModelParameterSnapshot.Equivalent(abstractSplines![id],NativeSketchCurveGeometry445.Spline(spline,check)))continue;
                }
                Add(id,new{kind="native_sketch_input_source",source_id=id.ToString(),sketch_id=sketchId.ToString(),owner_document_id=(value?.DbEntity.Document??sketch.DbEntity.Document).ID.ToString(),sketch_document_id=sketch.DbEntity.Document.ID.ToString(),runtime_type=value?.GetType().FullName,storage=abstractItem?"abstract_native_sketch_item":"resident_native_geometry",geometry_type=geometry.GeometryType.ToString(),verification=abstractSpline?"actual_WCS_geometry_map_ID_and_independent_GetItemGeometry; resident_object_absent; not_edit_readiness":"actual_containing_item_ID_and_current_WCS_curve; real_or_abstract_as_documented; not_edit_readiness"});
            }
            if(remaining.Count==0)break;
            // Auto observes the actual existing network. A missing inventory
            // leaves those nodes unverified; it is never inferred empty.
            try
            {
                using var manager=NativeSketchMembers385.Manager(sketch);var ids=manager.GetAllConstraints();if(ids==null)continue;ModelInventoryBudget.Ids(ids,check);
                foreach(var id in ids)
                {
                    check();using var info=manager.GetConstraintInfo(id);if(info==null)continue;
                    var refs=new[]{info.FirstObject,info.SecondObject,info.ThirdObject}.Where(r=>r!=null&&!r.ObjectId.IsNull).ToArray();
                    if(info.Type==Multicad.DatabaseServices.Constraints.ConstraintType.GeomFix)foreach(var reference in refs.Where(r=>!containing.Contains(r!.ObjectId)&&!constructionPoints.Contains(r!.ObjectId)))
                    {
                        check();using var geometry=sketch.GetItemGeometry(reference!.ObjectId,false);if(geometry==null||!NativeSketchMembers385.IsPoint(geometry))continue;var p=geometry.Point;
                        if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z))continue;constructionPoints.Add(reference.ObjectId);
                        if(remaining.Contains(reference.ObjectId))Add(reference.ObjectId,new{kind="native_sketch_construction_point",source_id=reference.ObjectId.ToString(),sketch_id=sketchId.ToString(),point_wcs=new[]{p.X,p.Y,p.Z},verification="actual_native_GeomFix_reference_and_selected_sketch_GetItemGeometry; no_coordinate_ID_alias"});
                    }
                    bool Member(Multicad.DatabaseServices.Constraints.IConstraintObjectInfo reference)
                    {
                        if(containing.Contains(reference.ObjectId)||constructionPoints.Contains(reference.ObjectId))return true;if(reference.PolylineSegmentIndex<0)return false;
                        foreach(var parent in containing)
                        {check();if(!sketch.ContainsObject(parent))continue;using var geometry=sketch.GetItemGeometry(parent,false);if(geometry==null||geometry.GeometryType!=G.EntityGeomType.kPolyline)continue;using var value=geometry.Polyline;using var segments=value.Segments;if(reference.PolylineSegmentIndex<segments.Count&&reference.ObjectId==Multicad.DatabaseServices.Constraints.ConstraintInfo.MakePolylineSegmentId(parent,reference.PolylineSegmentIndex))return true;}
                        return false;
                    }
                    if(refs.Length==0||refs.Any(r=>!Member(r!)))continue;
                    foreach(var role in new[]{(Id:id,Role:"relation"),(Id:info.DimId,Role:"dimension"),(Id:info.VarId,Role:"parameter")})
                    {
                        if(role.Id.IsNull||!remaining.Contains(role.Id))continue;
                        if(role.Role=="parameter"&&manager.GetConstraintParamId(id)!=role.Id)continue;
                        Add(role.Id,new{kind="native_sketch_relation_source",source_id=role.Id.ToString(),role=role.Role,sketch_id=sketchId.ToString(),constraint_id=id.ToString(),constraint_type=info.Type.ToString(),document_id=manager.DocumentId.ToString(),scope_id=manager.ScopeId.ToString(),network=manager.ConstraintsNetworkType.ToString(),member_ids=refs.Select(r=>r!.ObjectId.ToString()).ToArray(),verification="actual_current_constraint_info_binding; not_HistoryItem_or_edit_readiness"});
                    }
                }
            }
            catch(OperationCanceledException){throw;}
            catch(Exception){/* The ordinary reader reports remaining unverified nodes explicitly. */}
        }
        return new(sources,details,prior.Failures);
    }
}
