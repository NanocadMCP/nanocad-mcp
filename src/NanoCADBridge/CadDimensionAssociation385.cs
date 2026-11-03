using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Ordinary DWG object-snap associations. No projected DrawingView alias,
// viewport/trans-spatial path or guessed Polyline subentity marker is used.
internal static class CadDimensionAssociation385
{
    internal const string Warning="NATIVE_DIMENSION_ASSOCIATION: Native source ObjectIds and object-snap references are retained, rather than replacing measured geometry with coordinates. Native updates can reflow generated dimension graphics/text. Resident native manager anchors and readable owned data are observed; private manager internals are not completely inventoried. Recovery/Undo is not guaranteed; refresh the association token and inspect the result. Pending/unknown never means success or rollback.";
    internal sealed record PointState(string Role,ObjectId Source,ObjectSnapModes Snap,Point3d Pick,Point3d Evaluated,double NearParameter);
    internal sealed record Snapshot(string Token,ObjectId Association,int Flags,string Kind,PointState[] Points,JsonElement Dimension,JsonElement Sources,JsonElement AssociationData,JsonElement Metadata,string[] Containers);
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool condition,string message,string code="UNSUPPORTED_DIMENSION_ASSOCIATION")
    {if(!condition)throw new DomainException(code,message);}
    static void Match(bool condition,string path,object? expected,object? actual)
    {
        if(condition)return;var details=new{path,expected,current=actual};object receipt=details;
        var bytes=JsonSerializer.SerializeToUtf8Bytes(details,Wire.Json);
        if(bytes.Length>4096){var first=JsonSerializer.SerializeToUtf8Bytes(expected,Wire.Json);var second=JsonSerializer.SerializeToUtf8Bytes(actual,Wire.Json);
            receipt=new{path,expected_bytes=first.Length,current_bytes=second.Length,expected_sha256=Convert.ToHexString(SHA256.HashData(first)),current_sha256=Convert.ToHexString(SHA256.HashData(second)),values_omitted_reason="Actual changed component exceeds4096 diagnostic bytes."};}
        throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH","Native association differs at "+path,false,receipt);
    }
    internal static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static bool Local(DBObject item,Database db)=>item.Database?.UnmanagedObject==db.UnmanagedObject&&db.UnmanagedObject!=IntPtr.Zero;
    internal static bool Supported(Type type)=>type==typeof(AlignedDimension)||type==typeof(RotatedDimension)||type==typeof(RadialDimension)||type==typeof(DiametricDimension);
    static double[] Point(Point3d p)
    {Need(double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z),"A source/reference point is non-finite.");return[p.X,p.Y,p.Z];}
    static bool Same(Point3d a,Point3d b)=>a.DistanceTo(b)<=1e-7;
    internal static (string First,string Second) Roles(string kind)=>kind switch
    {
        "aligned" or "rotated" or "AlignedDimension" or "RotatedDimension"=>("xline1","xline2"),
        "radial" or "RadialDimension"=>("center","chord"),"diametric" or "DiametricDimension"=>("chord","far_chord"),
        _=>throw new DomainException("UNSUPPORTED_DIMENSION_ASSOCIATION","The dimension has no supported ordinary two-point association adapter.")
    };
    static DimAssocPointType NativeRole(string role)=>role switch
    {"xline1"=>DimAssocPointType.Xline1Point,"xline2"=>DimAssocPointType.Xline2Point,"center"=>DimAssocPointType.CenterPoint,"chord"=>DimAssocPointType.ChordPoint,"far_chord"=>DimAssocPointType.FarChordPoint,_=>throw new DomainException("INVALID_ARGUMENT","Unknown dimension point role.")};
    internal static Point3d DefinitionPoint(Dimension d,string role)=>d switch
    {
        AlignedDimension value=>role=="xline1"?value.XLine1Point:value.XLine2Point,
        RotatedDimension value=>role=="xline1"?value.XLine1Point:value.XLine2Point,
        RadialDimension value=>role=="center"?value.Center:value.ChordPoint,
        DiametricDimension value=>role=="chord"?value.ChordPoint:value.FarChordPoint,
        _=>throw new DomainException("UNSUPPORTED_DIMENSION_ASSOCIATION","Unsupported dimension subtype.")
    };
    internal static Entity Source(ObjectId id,ObjectId space,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(id),"A dimension source ObjectId is unavailable.");var entity=tr.GetObject(id,OpenMode.ForRead) as Entity;
        Need(entity!=null&&Local(entity,db)&&entity.ObjectId==id&&entity.OwnerId==space,"Sources must be actual resident entities in the same requested ordinary drawing space.","OUT_OF_SCOPE");
        var owner=tr.GetObject(space,OpenMode.ForRead) as BlockTableRecord;
        Need(owner!=null&&owner.GetType()==typeof(BlockTableRecord)&&Local(owner,db)&&owner.OwnerId==db.BlockTableId&&!owner.IsDependent&&!owner.IsFromExternalReference&&!owner.IsFromOverlayReference,
            "Source ownership is not an ordinary local drawing BlockTableRecord.");
        Need(entity!.GetType()==typeof(Line)||entity.GetType()==typeof(Circle)||entity.GetType()==typeof(Arc)||entity.GetType()==typeof(Polyline)||entity.GetType()==typeof(Polyline2d),
            "Use an ordinary native Line, Circle, Arc or open Polyline/Polyline2d; projected DrawingView geometry and nested paths need their own adapter.");
        Need(!entity.HasFields&&entity.Annotative!=AnnotativeStates.True,"Field-bearing/annotative source geometry needs its own context adapter.");
        if(entity is Circle circle)Need(circle.Normal.IsEqualTo(Vector3d.ZAxis)&&circle.Thickness==0,"Circle sources must lie in WCS XY with zero thickness.");
        if(entity is Arc arc)Need(arc.Normal.IsEqualTo(Vector3d.ZAxis)&&arc.Thickness==0,"Arc sources must lie in WCS XY with zero thickness.");
        if(entity is Polyline polyline)Need(!polyline.Closed&&polyline.NumberOfVertices>=2&&polyline.Normal.IsEqualTo(Vector3d.ZAxis)&&polyline.Thickness==0,
            "This Polyline route uses its actual open whole-curve start/end snaps; closed/interior-vertex subentity references are not inferred.");
        if(entity is Polyline2d legacy)
        {
            var shape=CadLegacyPolyline408.Read(legacy,tr,cancel);CadLegacyPolyline408.CheckGeometry(shape,cancel);
            Need(!legacy.Closed,"This legacy route retains actual open whole-curve start/end snaps; closed/interior-vertex subentity references are not inferred.");
        }
        return entity;
    }
    internal static (ObjectSnapModes Mode,Point3d Point) Snap(Entity entity,string snap)
    {
        Point3d value;ObjectSnapModes mode;
        if(snap is "start" or "end")
        {
            Need(entity is Line or Arc or Polyline or Polyline2d,"Start/end snaps require a Line, Arc or open native Polyline/Polyline2d.");
            value=snap=="start"?((Curve)entity).StartPoint:((Curve)entity).EndPoint;
            // The native reference has distinct start/end modes: a pickup and
            // NearPointParam do not turn ModeEnd into a start-point reference.
            mode=snap=="start"?ObjectSnapModes.ModeStartpoint:ObjectSnapModes.ModeEnd;
        }
        else if(snap=="midpoint")
        {Need(entity is Line,"The unambiguous whole-Line midpoint is supported; Polyline segment indices are not guessed.");var line=(Line)entity;value=line.StartPoint+(line.EndPoint-line.StartPoint)*.5;mode=ObjectSnapModes.ModeMid;}
        else if(snap=="center")
        {Need(entity is Circle or Arc,"Center snaps require a Circle or Arc.");value=entity is Circle circle?circle.Center:((Arc)entity).Center;mode=ObjectSnapModes.ModeCenter;}
        else
        {
            Need(entity is Circle,"Quadrant snaps use an ordinary whole Circle.");var circle=(Circle)entity;
            var offset=snap switch{"quadrant_x_positive"=>new Vector3d(circle.Radius,0,0),"quadrant_y_positive"=>new Vector3d(0,circle.Radius,0),
                "quadrant_x_negative"=>new Vector3d(-circle.Radius,0,0),"quadrant_y_negative"=>new Vector3d(0,-circle.Radius,0),_=>throw new DomainException("INVALID_ARGUMENT","Unknown source snap.")};
            value=circle.Center+offset;mode=ObjectSnapModes.ModeQuad;
        }
        Point(value);Need(Math.Abs(value.Z)<=1e-9,"This ordinary2D association route uses WCS XY/Z0 sources.");return(mode,value);
    }
    static void ValidateKind(string kind,PointState[] points,Database db,Transaction tr,Action cancel)
    {
        var roles=Roles(kind);Need(points.Length==2&&points.Count(item=>item.Role==roles.First)==1&&points.Count(item=>item.Role==roles.Second)==1,"Supply exactly the defining roles of this native dimension.");
        if(kind is "RadialDimension" or "radial")
        {
            var center=points.Single(item=>item.Role=="center");var chord=points.Single(item=>item.Role=="chord");
            Need(center.Source==chord.Source&&center.Snap==ObjectSnapModes.ModeCenter&&chord.Snap is ObjectSnapModes.ModeQuad or ObjectSnapModes.ModeStartpoint or ObjectSnapModes.ModeEnd,
                "A radial dimension binds center and curve point of the same actual Circle or Arc.");
        }
        if(kind is "DiametricDimension" or "diametric")
        {
            var first=points[0];var second=points[1];cancel();var circle=tr.GetObject(first.Source,OpenMode.ForRead) as Circle;
            Need(first.Source==second.Source&&circle!=null&&circle.GetType()==typeof(Circle)&&Local(circle,db)&&first.Snap==ObjectSnapModes.ModeQuad&&second.Snap==ObjectSnapModes.ModeQuad&&
                Same(circle.Center+(first.Evaluated-circle.Center)*-1,second.Evaluated),"A diametric dimension binds opposite native quadrants of one actual Circle.");
        }
    }
    internal static PointState[] Bindings(DimensionSourcePoint385[] bindings,string kind,ObjectId space,Database db,Transaction tr,Action cancel)
    {
        var result=bindings.Select(binding=>
        {
            cancel();var id=CadStructureWriter.Find(db,binding.SourceHandle);var source=Source(id,space,db,tr,cancel);var snap=Snap(source,binding.Snap);
            using var reference=Reference(source,snap.Mode,snap.Point);
            var evaluated=reference.EvalPoint();
            if(!Same(evaluated,snap.Point))throw new DomainException("DIMENSION_SOURCE_POINT_MISMATCH","Native snap did not evaluate to the selected source point before dimension mutation.",false,
                new{role=binding.Role,source_handle=Id(id),osnap_type=snap.Mode.ToString(),expected_point=Point(snap.Point),evaluated_point=Point(evaluated),near_point_parameter=reference.NearPointParam});
            return new PointState(binding.Role,id,snap.Mode,snap.Point,evaluated,reference.NearPointParam);
        }).ToArray();ValidateKind(kind,result,db,tr,cancel);return result.OrderBy(item=>(int)NativeRole(item.Role)).ToArray();
    }
    static void Ownership(DimAssoc association,Dimension dimension,Database db,Transaction tr,Action cancel)
    {
        Need(Live(association.ObjectId)&&Local(association,db)&&association.DimObjId==dimension.ObjectId,"The native association does not identify this resident dimension.");
        var visited=new HashSet<ObjectId>();DBObject child=association;
        while(child.OwnerId!=dimension.ObjectId&&child.OwnerId!=db.NamedObjectsDictionaryId)
        {
            cancel();Need(Live(child.OwnerId)&&visited.Add(child.OwnerId),"Association ownership path is unavailable or cyclic.");
            var owner=tr.GetObject(child.OwnerId,OpenMode.ForRead) as DBDictionary;
            Need(owner!=null&&owner.GetType()==typeof(DBDictionary)&&Local(owner,db),"Association ownership needs an actual local dictionary path.");
            int matches=0;foreach(DBDictionaryEntry entry in owner!){cancel();if(entry.Value==child.ObjectId)matches++;}
            Need(matches==1,"Association owner dictionary does not uniquely contain its actual native child.");child=owner;
        }
        if(child.OwnerId==dimension.ObjectId)Need(child.ObjectId==dimension.ExtensionDictionary||child.ObjectId==association.ObjectId,
            "Association-owned path is not the dimension's actual direct owner or extension graph.");
        else
        {
            var nod=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);int matches=0;
            foreach(DBDictionaryEntry entry in nod){cancel();if(entry.Value==child.ObjectId)matches++;}Need(matches==1,"The association dictionary path is not actually in this document's NOD.");
        }
    }
    static CadNativeDimAssocBinding434? Find(Dimension dimension,Database db,Transaction tr,Action cancel,ObjectId retiring)
    {
        var candidates=new HashSet<ObjectId>();var pending=new Stack<ObjectId>();var visited=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        using(var reactors=dimension.GetPersistentReactorIds())foreach(ObjectId id in reactors)
        {
            cancel();if(id==retiring&&!Live(id))continue;Need(Live(id),"A dimension reactor identity is unavailable.");
            using var observed=CadNativeDimAssocBinding434.TryBind(tr.GetObject(id,OpenMode.ForRead));
            if(observed?.Value.DimObjId==dimension.ObjectId)candidates.Add(id);
        }
        if(!dimension.ExtensionDictionary.IsNull)pending.Push(dimension.ExtensionDictionary);
        while(pending.Count!=0)
        {
            cancel();var id=pending.Pop();budget.Add(new{handle=Id(id)});Need(Live(id)&&visited.Add(id),"The dimension extension graph is unavailable or repeats an ownership path.");var item=tr.GetObject(id,OpenMode.ForRead);
            using(var observed=CadNativeDimAssocBinding434.TryBind(item))if(observed?.Value.DimObjId==dimension.ObjectId)candidates.Add(id);
            if(item is DBDictionary dictionary&&item.GetType()==typeof(DBDictionary))foreach(DBDictionaryEntry entry in dictionary)
            {cancel();budget.Add(new{name=entry.Key,handle=Id(entry.Value)});if(entry.Value==retiring&&!Live(entry.Value))continue;Need(Live(entry.Value),"A dimension dictionary entry is unavailable.");var child=tr.GetObject(entry.Value,OpenMode.ForRead);if(Local(child,db)&&child.OwnerId==id)pending.Push(entry.Value);}
            if(!item.ExtensionDictionary.IsNull)pending.Push(item.ExtensionDictionary);
        }
        Need(candidates.Count<=1,"Multiple actual dimension associations require their owning adapter.");
        if(candidates.Count==0)return null;
        var result=CadNativeDimAssocBinding434.TryBind(tr.GetObject(candidates.Single(),OpenMode.ForRead));
        Need(result!=null,"The selected native association descriptor changed.");
        try{Ownership(result!.Value,dimension,db,tr,cancel);return result;}catch{result!.Dispose();throw;}
    }
    // Retain all unrelated ordinary owned records. Only the proved selected
    // DimAssoc record and its direct native reactor link are intentional edits.
    static JsonElement Metadata(DBObject root,ObjectId association,Database db,Transaction tr,Action cancel)
    {
        var records=new List<object>();var pending=new Stack<(ObjectId Id,ObjectId Owner)>();var visited=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        object Common(DBObject item)
        {
            using var data=item.XData;using var reactors=item.GetPersistentReactorIds();var links=new List<string?>();
            foreach(ObjectId link in reactors){cancel();if(link==association)continue;Need(Live(link),"A retained native reactor identity is unavailable.");links.Add(Id(link));}
            return new{handle=Id(item.ObjectId),owner=Id(item.OwnerId),runtime_type=item.GetType().FullName,extension=Id(item.ExtensionDictionary),
                xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),reactors=links.OrderBy(item=>item,StringComparer.Ordinal).ToArray()};
        }
        if(!root.ExtensionDictionary.IsNull)pending.Push((root.ExtensionDictionary,root.ObjectId));
        while(pending.Count!=0)
        {
            cancel();var next=pending.Pop();Need(Live(next.Id)&&visited.Add(next.Id),"A retained owned record is unavailable or cyclic.");
            var item=tr.GetObject(next.Id,OpenMode.ForRead);Need(Local(item,db)&&item.OwnerId==next.Owner,"A retained owned record has foreign or inconsistent ownership.");
            if(next.Id==association)continue;
            Need(!item.HasFields,"Field-bearing dimension/source owned data needs its own adapter.");object record;
            if(item is DBDictionary dictionary&&item.GetType()==typeof(DBDictionary))
            {
                var entries=new List<object>();foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();if(entry.Value==association)continue;Need(Live(entry.Value),"A retained dictionary reference is unavailable.");
                    var child=tr.GetObject(entry.Value,OpenMode.ForRead);bool owned=Local(child,db)&&child.OwnerId==item.ObjectId;
                    entries.Add(new{name=entry.Key,handle=Id(entry.Value),owner=Id(child.OwnerId),runtime_type=child.GetType().FullName,owned});if(owned)pending.Push((entry.Value,item.ObjectId));
                }
                record=new{common=Common(item),dictionary_hard_owned=dictionary.TreatElementsAsHard,entries};
            }
            else if(item is Xrecord xrecord&&item.GetType()==typeof(Xrecord))
            {using var data=xrecord.Data;record=new{common=Common(item),xlate_references=xrecord.XlateReferences,merge_style=xrecord.MergeStyle.ToString(),data=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add)};}
            else if(item.GetType()==typeof(AssocManager)&&item is AssocManager manager)
            {
                // The exact local owner path was proved above. This native
                // service record has no public per-instance payload getter;
                // observe its anchor/readable metadata, without inventing a
                // private network inventory or evaluating another subsystem.
                NativeAssocManagerObservation356.Capture(manager,root,cancel);
                record=new{common=Common(item),assoc_manager_observation=new{adapter="resident_dimassoc_manager_anchor431",private_state_complete=false,
                    observation_scope="actual_native_anchor_owner_XData_reactors_and_readable_owned_records; private_manager_state_unobserved; DimAssoc_points_sources_and_flags_checked_separately"}};
            }
            else throw new DomainException("UNSUPPORTED_DIMENSION_ASSOCIATION","Unrelated owned data requires its typed adapter.",false,new{handle=Id(item.ObjectId),owner_handle=Id(item.OwnerId),root_handle=Id(root.ObjectId),runtime_type=item.GetType().FullName});
            budget.Add(record);records.Add(record);if(!item.ExtensionDictionary.IsNull)pending.Push((item.ExtensionDictionary,item.ObjectId));
        }
        var result=new{root=Common(root),records};budget.Add(result);return Wire.Element(result);
    }
    static JsonElement SourceData(PointState[] points,ObjectId space,ObjectId association,Database db,Transaction tr,Action cancel)
    {
        var result=points.Select(point=>point.Source).Distinct().OrderBy(id=>Id(id),StringComparer.Ordinal).Select(id=>
        {
            var source=Source(id,space,db,tr,cancel);object geometry=source switch
            {Line line=>new{start=Point(line.StartPoint),end=Point(line.EndPoint),line.Thickness},Circle circle=>new{center=Point(circle.Center),circle.Radius,normal=new[]{circle.Normal.X,circle.Normal.Y,circle.Normal.Z},circle.Thickness},
                Arc arc=>new{center=Point(arc.Center),arc.Radius,arc.StartAngle,arc.EndAngle,normal=new[]{arc.Normal.X,arc.Normal.Y,arc.Normal.Z},arc.Thickness},
                Polyline polyline=>new{polyline.Closed,polyline.Elevation,polyline.Thickness,vertices=Enumerable.Range(0,polyline.NumberOfVertices).Select(index=>new{point=Point(polyline.GetPoint3dAt(index)),bulge=polyline.GetBulgeAt(index),start_width=polyline.GetStartWidthAt(index),end_width=polyline.GetEndWidthAt(index)}).ToArray()},
                Polyline2d legacy=>CadLegacyPolyline408.Geometry(legacy,tr,cancel),
                _=>throw new DomainException("UNSUPPORTED_DIMENSION_ASSOCIATION","Unclassified source geometry.")};
            return new{handle=Id(id),geometry,appearance=Appearance(source),metadata=Metadata(source,association,db,tr,cancel)};
        }).ToArray();new ModelInventoryBudget(cancel).Add(result);return Wire.Element(result);
    }
    static object Appearance(Entity entity)
    {
        using var color=entity.Color;var transparency=entity.Transparency;
        return new{layer=Id(entity.LayerId),entity.Visible,color=new{method=color.ColorMethod.ToString(),color.ColorIndex,color.Red,color.Green,color.Blue,color.ColorName,color.BookName},
            linetype=Id(entity.LinetypeId),entity.LinetypeScale,weight=(int)entity.LineWeight,transparency=new{transparency.Alpha,transparency.IsByAlpha,transparency.IsByLayer,transparency.IsByBlock}};
    }
    internal static Snapshot Capture(Dimension dimension,Database db,Transaction tr,Action cancel,ObjectId retiring=default)
    {
        cancel();Need(Supported(dimension.GetType())&&Local(dimension,db)&&Live(dimension.ObjectId),"Use a supported exact resident native dimension.");
        Need(!dimension.HasFields&&dimension.Annotative!=AnnotativeStates.True&&dimension.Normal.IsEqualTo(Vector3d.ZAxis)&&Math.Abs(dimension.Elevation)<=1e-9,
            "This ordinary2D route needs a nonannotative literal dimension in WCS XY/Z0.");
        using var nativeAssociation=Find(dimension,db,tr,cancel,retiring);var assoc=nativeAssociation?.Value;ObjectId association=assoc?.ObjectId??ObjectId.Null;var points=new List<PointState>();int flags=0;object associationData=new{association_handle=(string?)null};
        if(assoc!=null)
        {
            Need(!assoc.TransSpatial&&!assoc.HasFields&&assoc.ExtensionDictionary.IsNull,"Trans-spatial, Field-bearing or extended association storage needs its own adapter.");
            using(var data=assoc.XData)Need(data==null,"Custom association XData needs its own adapter.");flags=(int)assoc.AssocFlag;
            if(flags is not(0 or 3))throw new DomainException("UNSUPPORTED_DIMENSION_ASSOCIATION","This two-point route requires exactly the native first/second flags, or an observed inactive native record.",false,
                new{association_handle=Id(association),dimension_handle=Id(dimension.ObjectId),actual_flags=flags});var roles=Roles(dimension.GetType().Name);
            foreach(string role in flags==3?new[]{roles.First,roles.Second}:Array.Empty<string>())
            {
                cancel();using var reference=assoc.PointRef(NativeRole(role));Need(reference!=null&&reference.GetType()==typeof(OsnapPointRef),"The selected native point reference is unavailable or not an ordinary OsnapPointRef.");
                var path=reference!.IdPath;var ids=path.GetObjectIds();Need(ids.Length==1&&path.SubentId.Type==SubentityType.Null&&path.SubentId.IndexPtr==IntPtr.Zero,
                    "Only whole local source paths are supported; nested/trans-spatial/projected or subentity aliases are not inferred.");
                var source=Source(ids[0],dimension.OwnerId,db,tr,cancel);var snap=reference.OsnapType;
                Need(snap is ObjectSnapModes.ModeStartpoint or ObjectSnapModes.ModeEnd or ObjectSnapModes.ModeMid or ObjectSnapModes.ModeCenter or ObjectSnapModes.ModeQuad,"This native snap type needs its own measured-point adapter.");
                Need(snap!=ObjectSnapModes.ModeMid||source is Line,"A segment-qualified midpoint needs its native segment adapter.");
                Need(snap!=ObjectSnapModes.ModeCenter||source is Circle or Arc,"Center association is not on a Circle/Arc.");
                Need(snap!=ObjectSnapModes.ModeQuad||source is Circle,"Quadrant association is not on a Circle.");
                var pick=reference.Point;var evaluated=reference.EvalPoint();Point(pick);Point(evaluated);points.Add(new(role,ids[0],snap,pick,evaluated,reference.NearPointParam));
            }
            if(flags==3)
            {
                ValidateKind(dimension.GetType().Name,points.ToArray(),db,tr,cancel);
                using var geometryIds=new ObjectIdCollection();assoc.GetDimAssocGeomIds(geometryIds);
                Need(geometryIds.Cast<ObjectId>().Distinct().ToHashSet().SetEquals(points.Select(point=>point.Source)),"Native association geometry inventory differs from its complete ordinary point paths.");
            }
            associationData=new{association_handle=Id(association),owner_handle=Id(assoc.OwnerId),dimension_handle=Id(assoc.DimObjId),flags,trans_spatial=assoc.TransSpatial,rotated_type=assoc.RotatedType.ToString(),
                points=points.Select(point=>new{role=point.Role,source_handle=Id(point.Source),osnap_type=point.Snap.ToString(),point=Point(point.Pick),evaluated_point=Point(point.Evaluated),near_point_parameter=point.NearParameter}).ToArray()};
        }
        var definition=Wire.Element(new{kind=dimension.GetType().Name,definition=DimensionGeometryReader.Definition(dimension),style=Id(dimension.DimensionStyle),text=dimension.DimensionText,
            normal=new[]{dimension.Normal.X,dimension.Normal.Y,dimension.Normal.Z},dimension.Elevation,appearance=Appearance(dimension)});
        var metadata=Metadata(dimension,association.IsNull?retiring:association,db,tr,cancel);var sources=SourceData(points.ToArray(),dimension.OwnerId,association,db,tr,cancel);var details=Wire.Element(associationData);
        var containers=new List<string>();if(assoc!=null)
        {
            ObjectId owner=assoc.OwnerId;while(Live(owner)&&tr.GetObject(owner,OpenMode.ForRead) is DBDictionary dictionary)
            {cancel();Need(!containers.Contains(Id(owner)!),"Native association dictionary ownership is cyclic.");containers.Add(Id(owner)!);owner=dictionary.OwnerId;}
        }
        var payload=new{definition,metadata,sources,association=details};var budget=new ModelInventoryBudget(cancel);budget.Add(payload);
        return new(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload,Wire.Json))),association,flags,dimension.GetType().Name,points.ToArray(),definition,sources,details,metadata,containers.ToArray());
    }
    internal static object TryRead(Dimension dimension,Action cancel)
    {
        try
        {
            var db=dimension.Database;Need(db!=null&&db.UnmanagedObject!=IntPtr.Zero,"Dimension database is unavailable.");using var tr=db!.TransactionManager.StartTransaction();
            var current=(Dimension)tr.GetObject(dimension.ObjectId,OpenMode.ForRead);var snapshot=Capture(current,db,tr,cancel);
            return new{association_token=snapshot.Token,association_handle=Id(snapshot.Association),active=snapshot.Flags==3,points=snapshot.AssociationData,
                source_handles=snapshot.Points.Select(point=>Id(point.Source)).Distinct().ToArray(),editing_unavailable_reason=(string?)null,
                scope="ordinary_local_DWG_whole_entity_OsnapPointRef; resident_manager_anchors_and_readable_owned_data_observed_private_manager_internals_unobserved; no_DrawingView_alias_or_trans_spatial_binding"};
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            string reason=error is DomainException domain?domain.Error.code+": "+domain.Error.message:error is Teigha.Runtime.Exception native?native.ErrorStatus+": "+native.Message:error.GetType().Name+": "+error.Message;
            if(System.Text.Encoding.UTF8.GetByteCount(reason)>2048)reason=error.GetType().Name+": native association observation exceeded its diagnostic budget.";
            return new{association_token=(string?)null,association_handle=(string?)null,active=(bool?)null,points=(object?)null,source_handles=(string[]?)null,editing_unavailable_reason=reason,
                scope="Association unavailable for this object; stored dimension geometry remains independently readable."};
        }
    }
    internal static void MatchPoints(Dimension dimension,PointState[] points)
    {foreach(var point in points)Match(Same(DefinitionPoint(dimension,point.Role),point.Evaluated),"/defining_points/"+point.Role,Point(point.Evaluated),Point(DefinitionPoint(dimension,point.Role)));}
    // OsnapPointRef stores both its pick and the native curve parameter. A default
    // zero parameter cannot identify the far endpoint of an ordinary source Line.
    // Use Curve.GetParameterAtPoint, never a guessed endpoint/subentity marker.
    static OsnapPointRef Reference(Entity source,ObjectSnapModes mode,Point3d point)
    {
        var reference=new OsnapPointRef();
        try
        {
            reference.IdPath=new FullSubentityPath([source.ObjectId],new SubentityId(SubentityType.Null,IntPtr.Zero));
            reference.OsnapType=mode;reference.Point=point;
            if(mode!=ObjectSnapModes.ModeCenter)
            {
                Need(source is Curve,"Selected ordinary snap requires its actual native Curve.");
                double parameter=((Curve)source).GetParameterAtPoint(point);Need(double.IsFinite(parameter),"Native source parameter is non-finite.");reference.NearPointParam=parameter;
            }
            return reference;
        }
        catch{reference.Dispose();throw;}
    }
    internal sealed record PostObservation434(ObjectId Association, object BeforeUpdate, object AfterUpdate);
    internal static string? SafeId434(ObjectId id)
    {try{return Id(id);}catch{return null;}}
    internal static object ObservePosted434(Dimension dimension,PointState[] points,ObjectId association,Transaction tr,string stage)
    {
        // Bounded diagnostic reads of already-known identities only. An
        // observation failure must never replace the original write outcome.
        string? knownHandle=SafeId434(association);
        try
        {
            int Links(DBObject value)
            {using var ids=value.GetPersistentReactorIds();int count=0;foreach(ObjectId id in ids)if(id==association)count++;return count;}
            bool valid=!association.IsNull&&association.IsValid;bool ownErased=valid&&association.IsErased;bool effectivelyErased=valid&&association.IsEffectivelyErased;bool erased=ownErased||effectivelyErased;
            object? native=null;int? flags=null;string? dimensionId=null;string? owner=null;
            if(valid&&!erased)
            {
                var value=tr.GetObject(association,OpenMode.ForRead);native=CadNativeDimAssocBinding434.Observe(value);owner=Id(value.OwnerId);
                using var binding=CadNativeDimAssocBinding434.TryBind(value);
                if(binding!=null){flags=(int)binding.Value.AssocFlag;dimensionId=Id(binding.Value.DimObjId);}
            }
            return new{stage,association_handle=knownHandle,valid,erased,is_erased=ownErased,is_effectively_erased=effectivelyErased,owner_handle=owner,native,flags,dimension_handle=dimensionId,
                dimension_reverse_links=Links(dimension),source_reverse_links=points.Select(point=>point.Source).Distinct().Select(id=>new
                {source_handle=Id(id),count=Live(id)?(int?)Links(tr.GetObject(id,OpenMode.ForRead)):null}).ToArray()};
        }
        catch(Exception error)
        {return new{stage,association_handle=knownHandle,observation_error=error.GetType().Name,observation_message=error.Message.Length<=512?error.Message:error.Message[..512]};}
    }
    internal static PostObservation434 Post(Dimension dimension,PointState[] points,Database db,Transaction tr,Action cancel)
    {
        var assoc=new DimAssoc();var references=new List<OsnapPointRef>();bool resident=false,postEntered=false;
        try
        {
            foreach(var point in points)
            {
                cancel();var reference=Reference(Source(point.Source,dimension.OwnerId,db,tr,cancel),point.Snap,point.Pick);
                references.Add(reference);Match(Same(reference.EvalPoint(),point.Evaluated),"/prepared_source_point/"+point.Role,Point(point.Evaluated),Point(reference.EvalPoint()));assoc.SetPointRef(NativeRole(point.Role),reference);
            }
            cancel();postEntered=true;assoc.Post(dimension.ObjectId,ObjectId.Null,true);resident=Live(assoc.ObjectId)&&Local(assoc,db);
            Need(resident,"Native Post did not expose this association's actual resident ObjectId; its by-value output argument is not assumed.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
            var postedId=assoc.ObjectId;
            tr.AddNewlyCreatedDBObject(assoc,true);Ownership(assoc,dimension,db,tr,cancel);
            bool Linked(DBObject value)
            {
                using var reactors=value.GetPersistentReactorIds();int count=0;foreach(ObjectId id in reactors){cancel();if(id==assoc.ObjectId)count++;}
                if(count>1)throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH","A native dimension dependency supplied duplicate reverse reactor links.",false,
                    new{association_handle=Id(assoc.ObjectId),dimension_handle=Id(dimension.ObjectId),object_handle=Id(value.ObjectId),actual_flags=(int)assoc.AssocFlag,actual_source_reactor_count=count,stage="observe_native_reactor_membership"});
                return count==1;
            }
            // Posting and reactor registration are distinct public operations
            // in the installed SDK. Complete any missing actual registrations
            // explicitly; do not search the NOD or invent a private lookup.
            (bool Dimension,string[] Sources) MissingLinks()
            {
                var missingSources=new List<string>();foreach(var id in points.Select(point=>point.Source).Distinct())
                {cancel();if(!Linked(Source(id,dimension.OwnerId,db,tr,cancel)))missingSources.Add(Id(id)!);}
                return(!Linked(dimension),missingSources.ToArray());
            }
            var missing=MissingLinks();int flags=(int)assoc.AssocFlag;
            try
            {
                if(missing.Dimension){cancel();assoc.AddToDimensionReactor(true);}
                if(missing.Sources.Length!=0)
                {
                    var sourceIds=points.Select(point=>point.Source).Distinct().ToArray();
                    foreach(var id in sourceIds)
                    {
                        cancel();Source(id,dimension.OwnerId,db,tr,cancel);var writable=tr.GetObject(id,OpenMode.ForWrite);
                        Need(writable.ObjectId==id&&Local(writable,db)&&writable.OwnerId==dimension.OwnerId&&writable.IsWriteEnabled,
                            "A verified native dimension source could not be opened for persistent-reactor registration.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
                    }
                    cancel();assoc.AddToPointRefReactor();
                    // Complete only an absent native reverse link, using the
                    // same public DBObject API as associative Hatch sources.
                    // Forward references already retain these actual source
                    // ObjectIds; this is not a coordinate-derived association.
                    foreach(var id in sourceIds)
                    {
                        cancel();var source=tr.GetObject(id,OpenMode.ForWrite);
                        int CountOwnLinks(){using var links=source.GetPersistentReactorIds();int found=0;foreach(ObjectId link in links){cancel();if(link==assoc.ObjectId)found++;}return found;}
                        int count=CountOwnLinks();
                        if(count==0){cancel();source.AddPersistentReactor(assoc.ObjectId);count=CountOwnLinks();}
                        if(count!=1)throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH","A native dimension source did not retain exactly one reverse reactor link.",false,
                            new{association_handle=Id(assoc.ObjectId),dimension_handle=Id(dimension.ObjectId),source_handle=Id(id),actual_flags=(int)assoc.AssocFlag,actual_source_reactor_count=count,stage="public_source_reverse_link_completion"});
                    }
                }
                missing=MissingLinks();flags=(int)assoc.AssocFlag;
            }
            catch(Teigha.Runtime.Exception error)
            {
                throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH","Native association reactor registration failed.",false,
                    new{association_handle=Id(assoc.ObjectId),dimension_handle=Id(dimension.ObjectId),actual_flags=flags,missing_dimension_reactor=missing.Dimension,
                        missing_source_reactor_handles=missing.Sources,native_status=error.ErrorStatus.ToString(),stage="last_observation_before_native_registration_error"});
            }
            if(missing.Dimension||missing.Sources.Length!=0||flags!=3)throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH","Native point association did not retain both active references and their actual dimension/source reactors before update.",false,
                new{association_handle=Id(assoc.ObjectId),dimension_handle=Id(dimension.ObjectId),actual_flags=flags,expected_flags=3,missing_dimension_reactor=missing.Dimension,
                    missing_source_reactor_handles=missing.Sources,stage="after_native_reactor_registration"});
            var beforeUpdate=ObservePosted434(dimension,points,postedId,tr,"before_UpdateDimension");
            cancel();assoc.UpdateDimension(true,false);cancel();
            var afterUpdate=ObservePosted434(dimension,points,postedId,tr,"after_UpdateDimension_before_PointRef_cleanup");
            MatchPoints(dimension,points);
            return new(postedId,beforeUpdate,afterUpdate);
        }
        finally
        {
            foreach(var reference in references)reference.Dispose();
            if(!resident){if(!postEntered)assoc.Dispose();else GC.SuppressFinalize(assoc);}
        }
    }
    internal static void Refresh(Dimension dimension,Snapshot before,Database db,Transaction tr,Action cancel)
    {Need(before.Flags==3,"The dimension has no current supported active association.");using var native=CadNativeDimAssocBinding434.TryBind(tr.GetObject(before.Association,OpenMode.ForWrite));Need(native!=null,"The selected native association descriptor changed.");var assoc=native!.Value;Ownership(assoc,dimension,db,tr,cancel);cancel();assoc.UpdateDimension(true,false);cancel();MatchPoints(dimension,before.Points);}
    internal static void Detach(Dimension dimension,Snapshot before,Database db,Transaction tr,Action cancel)
    {Need(before.Flags==3,"The dimension has no current supported active association.");using var native=CadNativeDimAssocBinding434.TryBind(tr.GetObject(before.Association,OpenMode.ForWrite));Need(native!=null,"The selected native association descriptor changed.");var assoc=native!.Value;Ownership(assoc,dimension,db,tr,cancel);cancel();assoc.RemoveAssociativity(true);cancel();}

    internal static void Retained(JsonElement before,JsonElement after,ObjectId selected,Transaction tr,Database db,Action cancel,string[]? previousContainers=null)
    {
        // Newly created, otherwise empty extension containers may solely carry
        // the selected native association. Preserve every prior record exactly.
        var first=before.GetProperty("records").EnumerateArray().ToDictionary(record=>record.GetProperty("common").GetProperty("handle").GetString()!,StringComparer.Ordinal);
        var second=after.GetProperty("records").EnumerateArray().ToDictionary(record=>record.GetProperty("common").GetProperty("handle").GetString()!,StringComparer.Ordinal);
        bool PlainContainer(JsonElement record)
        {
            var common=record.GetProperty("common");return record.TryGetProperty("entries",out var entries)&&entries.GetArrayLength()==0&&common.GetProperty("xdata").ValueKind==JsonValueKind.Null&&
                common.GetProperty("extension").ValueKind==JsonValueKind.Null&&common.GetProperty("reactors").EnumerateArray().All(link=>link.GetRawText()==common.GetProperty("owner").GetRawText());
        }
        var added=second.Keys.Except(first.Keys,StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);var ownerPath=new HashSet<string>(StringComparer.Ordinal);
        bool selectedNative=false;
        if(!selected.IsNull&&Live(selected))
        {
            var association=tr.GetObject(selected,OpenMode.ForRead);using var observed=CadNativeDimAssocBinding434.TryBind(association);selectedNative=observed!=null&&Local(association,db);ObjectId owner=association.OwnerId;
            while(Live(owner)&&tr.GetObject(owner,OpenMode.ForRead) is DBDictionary dictionary)
            {cancel();Need(ownerPath.Add(Id(owner)!),"Native association dictionary ownership is cyclic.");owner=dictionary.OwnerId;}
        }
        // Native Post can allocate an AssocManager anchor beside its DimAssoc.
        // The owner walker has already proved every actual local dictionary
        // membership. Admit that typed service anchor and its readable owned
        // records, while retaining every prior ordinary record/entry exactly.
        var managers=added.Where(handle=>selectedNative&&second[handle].TryGetProperty("assoc_manager_observation",out var observed)&&
            observed.GetProperty("adapter").GetString()=="resident_dimassoc_manager_anchor431"&&observed.GetProperty("private_state_complete").ValueKind==JsonValueKind.False&&
            second[handle].GetProperty("common").GetProperty("runtime_type").GetString()==typeof(AssocManager).FullName).ToHashSet(StringComparer.Ordinal);
        var admitted=new HashSet<string>(managers,StringComparer.Ordinal);
        bool PlainCommon(JsonElement record)
        {
            var common=record.GetProperty("common");return common.GetProperty("xdata").ValueKind==JsonValueKind.Null&&common.GetProperty("extension").ValueKind==JsonValueKind.Null&&
                common.GetProperty("reactors").EnumerateArray().All(link=>link.GetRawText()==common.GetProperty("owner").GetRawText());
        }
        bool progress;
        do
        {
            progress=false;
            foreach(string handle in added.Except(admitted,StringComparer.Ordinal).ToArray())
            {
                cancel();var record=second[handle];var owner=record.GetProperty("common").GetProperty("owner").GetString();
                bool nativeChild=owner!=null&&admitted.Contains(owner);
                bool carrier=record.TryGetProperty("entries",out var entries)&&PlainCommon(record)&&
                    entries.EnumerateArray().All(entry=>entry.GetProperty("owned").ValueKind==JsonValueKind.True&&entry.GetProperty("owner").GetString()==handle&&admitted.Contains(entry.GetProperty("handle").GetString()!))&&
                    (ownerPath.Contains(handle)||entries.GetArrayLength()!=0);
                if(nativeChild||carrier){admitted.Add(handle);progress=true;}
            }
        }while(progress);
        var removed=new HashSet<string>(StringComparer.Ordinal);
        foreach(var record in first)
        {
            cancel();if(!second.TryGetValue(record.Key,out var current)&&previousContainers?.Contains(record.Key)==true&&PlainContainer(record.Value)){removed.Add(record.Key);continue;}
            bool same=second.ContainsKey(record.Key)&&record.Value.GetRawText()==current.GetRawText();
            if(!same&&second.ContainsKey(record.Key)&&record.Value.TryGetProperty("entries",out var previousEntries)&&current.TryGetProperty("entries",out var currentEntries))
            {
                bool retainedFields=record.Value.EnumerateObject().All(field=>field.Name=="entries"||current.TryGetProperty(field.Name,out var actual)&&field.Value.GetRawText()==actual.GetRawText())&&
                    record.Value.EnumerateObject().Count()==current.EnumerateObject().Count();
                var retainedEntries=currentEntries.EnumerateArray().Where(entry=>!admitted.Contains(entry.GetProperty("handle").GetString()!)).Select(entry=>entry.GetRawText());
                bool actualNewMembership=currentEntries.EnumerateArray().Where(entry=>admitted.Contains(entry.GetProperty("handle").GetString()!)).All(entry=>
                    entry.GetProperty("owned").ValueKind==JsonValueKind.True&&entry.GetProperty("owner").GetString()==record.Key&&second[entry.GetProperty("handle").GetString()!].GetProperty("common").GetProperty("owner").GetString()==record.Key);
                same=retainedFields&&actualNewMembership&&previousEntries.EnumerateArray().Select(entry=>entry.GetRawText()).SequenceEqual(retainedEntries);
            }
            Match(same,"/retained_owned_records/"+record.Key,record.Value,second.ContainsKey(record.Key)?current:null);
        }
        foreach(string handle in added)
        {
            cancel();Need(admitted.Contains(handle),
                "Native association introduced unrelated/unproved owned metadata.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
        }
        var rootBefore=before.GetProperty("root");var rootAfter=after.GetProperty("root");
        foreach(var property in rootBefore.EnumerateObject())
        {
            cancel();var actual=rootAfter.GetProperty(property.Name);
            if(property.Name=="extension"&&property.Value.ValueKind==JsonValueKind.Null&&actual.ValueKind==JsonValueKind.String&&added.Contains(actual.GetString()!))continue;
            if(property.Name=="extension"&&actual.ValueKind==JsonValueKind.Null&&property.Value.ValueKind==JsonValueKind.String&&removed.Contains(property.Value.GetString()!))continue;
            if(property.Name=="reactors"&&property.Value.EnumerateArray().Select(link=>link.GetRawText()).SequenceEqual(actual.EnumerateArray().Where(link=>!managers.Contains(link.GetString()!)).Select(link=>link.GetRawText())))continue;
            Match(property.Value.GetRawText()==actual.GetRawText(),"/retained_root/"+property.Name,property.Value,actual);
        }
    }
    internal static void VerifySources(JsonElement before,JsonElement after,ObjectId selected,Transaction tr,Database db,Action cancel)
    {
        var initial=before.EnumerateArray().ToDictionary(item=>item.GetProperty("handle").GetString()!,StringComparer.Ordinal);
        var current=after.EnumerateArray().ToDictionary(item=>item.GetProperty("handle").GetString()!,StringComparer.Ordinal);
        Match(initial.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(current.Keys),"/retained_source_ids",initial.Keys,current.Keys);
        foreach(var source in initial)
        {
            cancel();var actual=current[source.Key];foreach(var field in source.Value.EnumerateObject())
                if(field.Name!="metadata")Match(field.Value.GetRawText()==actual.GetProperty(field.Name).GetRawText(),"/retained_sources/"+source.Key+"/"+field.Name,field.Value,actual.GetProperty(field.Name));
            Retained(source.Value.GetProperty("metadata"),actual.GetProperty("metadata"),selected,tr,db,cancel);
        }
    }
    internal static JsonElement SourcesFor(Dimension dimension,PointState[] points,ObjectId association,Database db,Transaction tr,Action cancel)=>SourceData(points,dimension.OwnerId,association,db,tr,cancel);
    internal static JsonElement MetadataFor(Dimension dimension,ObjectId association,Database db,Transaction tr,Action cancel)=>Metadata(dimension,association,db,tr,cancel);
    internal static void VerifyDefinition(Snapshot before,Snapshot after,bool updating)
    {
        foreach(var field in before.Dimension.EnumerateObject())
        {
            if(updating&&field.Name=="definition")continue;
            Match(field.Value.GetRawText()==after.Dimension.GetProperty(field.Name).GetRawText(),"/dimension/"+field.Name,field.Value,after.Dimension.GetProperty(field.Name));
        }
        if(updating)
        {
            var roles=Roles(before.Kind);string first=roles.First=="center"?"center":roles.First=="chord"?"chord_point":"xline1_point";
            string second=roles.Second=="chord"?"chord_point":roles.Second=="far_chord"?"far_chord_point":"xline2_point";
            foreach(var field in before.Dimension.GetProperty("definition").EnumerateObject())
                if(field.Name!=first&&field.Name!=second&&field.Name!="dimline_point")Need(field.Value.GetRawText()==after.Dimension.GetProperty("definition").GetProperty(field.Name).GetRawText(),"Native update changed an unrequested dimension definition field.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
        }
    }
    sealed record Follower434(ObjectId Dimension,Snapshot Before);
    static Follower434[] Followers(Entity source,Database db,Transaction tr,Action cancel)
    {
        var result=new Dictionary<ObjectId,Snapshot>();using var reactors=source.GetPersistentReactorIds();
        foreach(ObjectId id in reactors)
        {
            cancel();Need(Live(id),"A source persistent dependency is unavailable.");var reactor=tr.GetObject(id,OpenMode.ForRead);
            using var native=CadNativeDimAssocBinding434.TryBind(reactor);if(native==null)continue;var association=native.Value;
            Need(Live(association.DimObjId),"A dependent native dimension is unavailable.");var dimension=tr.GetObject(association.DimObjId,OpenMode.ForRead) as Dimension;
            Need(dimension!=null&&dimension.OwnerId==source.OwnerId&&Local(dimension,db),"Native dimension dependency has a foreign source/space ownership path.");
            var snapshot=Capture(dimension!,db,tr,cancel);Need(snapshot.Association==id,"The source reactor does not identify this dimension's current supported native association.");
            if(snapshot.Flags==0)continue;
            Need(snapshot.Flags==3&&snapshot.Points.Any(point=>point.Source==source.ObjectId),
                "The source reactor is not this dimension's complete current supported source binding.");result.TryAdd(dimension!.ObjectId,snapshot);
        }
        return result.Select(item=>new Follower434(item.Key,item.Value)).ToArray();
    }
    internal static void PreflightFollowers(Entity source,Database db,Transaction tr,Action cancel)=>Followers(source,db,tr,cancel);
    internal static bool RefreshFollowers(Entity source,Database db,Transaction tr,Action cancel)
    {
        var followers=Followers(source,db,tr,cancel);if(followers.Length==0)return false;
        Need(source.IsWriteEnabled,"A modified associative source must have its actual write-open state before native notification.");
        try
        {
            // Installed core UpdateDimension only recomputes the graphics block.
            // Give the host its real source write→read transition (including
            // its database-reactor hook) before opening any dimension for write.
            // Do not invoke callbacks manually or assign inferred coordinates.
            cancel();source.DowngradeOpen();cancel();Need(source.IsReadEnabled&&!source.IsWriteEnabled,"Native source notification did not leave read access.");
            foreach(var follower in followers)
            {
                cancel();var dimension=(Dimension)tr.GetObject(follower.Dimension,OpenMode.ForWrite);var before=follower.Before;
                Refresh(dimension,before,db,tr,cancel);var after=Capture(dimension,db,tr,cancel);MatchPoints(dimension,after.Points);
                Need(before.Association==after.Association&&before.AssociationData.GetProperty("flags").GetRawText()==after.AssociationData.GetProperty("flags").GetRawText(),
                    "Native source update replaced the retained association identity/flags.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
                foreach(var point in before.Points)
                {var current=after.Points.SingleOrDefault(item=>item.Role==point.Role);Need(current!=null&&current.Source==point.Source&&current.Snap==point.Snap&&Same(current.Evaluated,point.Evaluated),
                    "Native source update changed the retained point role/source/snap or measured source point.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");}
                VerifyDefinition(before,after,true);Retained(before.Metadata,after.Metadata,after.Association,tr,db,cancel,before.Containers);VerifySources(before.Sources,after.Sources,after.Association,tr,db,cancel);
            }
            return true;
        }
        finally{if(!source.IsDisposed&&!source.IsWriteEnabled)source.UpgradeOpen();}
    }
    // Whole-curve ModeStartpoint/ModeEnd references keep their explicit native
    // endpoint mode; no legacy vertex subentity marker is guessed. Retain the
    // association/path/role and refresh the pickup and actual curve parameter
    // to the surviving Vertex2d endpoint identified before the contour edits.
    internal static void RefreshRetainedLegacyEndpoints408(Dimension dimension,Snapshot original,
        IReadOnlyDictionary<string,Point3d> endpointPoints,Database db,Transaction tr,Action cancel)
    {
        Need(original.Flags==3&&Live(original.Association),"The retained legacy follower must have its active ordinary native association.");
        var current=Capture(dimension,db,tr,cancel);
        Need(current.Association==original.Association&&current.Flags==original.Flags&&current.Kind==original.Kind,
            "Contour editing replaced the retained dimension association identity/kind/flags.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
        using var nativeAssociation=CadNativeDimAssocBinding434.TryBind(tr.GetObject(original.Association,OpenMode.ForWrite));Need(nativeAssociation!=null,"The selected native association descriptor changed.");var association=nativeAssociation!.Value;Ownership(association,dimension,db,tr,cancel);
        foreach(var point in original.Points)
        {
            cancel();var actual=current.Points.SingleOrDefault(value=>value.Role==point.Role);
            Need(actual!=null&&actual.Source==point.Source&&actual.Snap==point.Snap,
                "Contour editing changed a retained dimension source path or snap role.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
            if(!endpointPoints.TryGetValue(point.Role,out var endpoint))continue;
            Need((point.Snap is ObjectSnapModes.ModeStartpoint or ObjectSnapModes.ModeEnd)&&tr.GetObject(point.Source,OpenMode.ForRead) is Polyline2d,
                "Explicit retained pickup adjustment applies to whole legacy source endpoints only.");
            using var reference=association.PointRef(NativeRole(point.Role));
            Need(reference!=null&&reference.GetType()==typeof(OsnapPointRef),"The retained native endpoint point reference is unavailable.");
            reference!.Point=endpoint;reference.NearPointParam=((Curve)tr.GetObject(point.Source,OpenMode.ForRead)).GetParameterAtPoint(endpoint);association.SetPointRef(NativeRole(point.Role),reference);
            Need(Same(reference.EvalPoint(),endpoint),"Native endpoint pickup did not resolve to the requested surviving Vertex2d endpoint.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
        }
        var prepared=Capture(dimension,db,tr,cancel);cancel();association.UpdateDimension(true,false);cancel();
        var after=Capture(dimension,db,tr,cancel);MatchPoints(dimension,after.Points);
        Need(after.Association==original.Association&&after.Flags==original.Flags,
            "Native refresh changed the retained dimension association identity/flags.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
        foreach(var point in original.Points)
        {
            var actual=after.Points.Single(value=>value.Role==point.Role);
            Need(actual.Source==point.Source&&actual.Snap==point.Snap&&(!endpointPoints.TryGetValue(point.Role,out var endpoint)||Same(actual.Evaluated,endpoint)),
                "Native refresh changed the retained role/source/snap or missed the requested endpoint.","NATIVE_DIMENSION_ASSOCIATION_MISMATCH");
        }
        VerifyDefinition(original,after,true);Retained(original.Metadata,after.Metadata,after.Association,tr,db,cancel,original.Containers);
        VerifySources(prepared.Sources,after.Sources,after.Association,tr,db,cancel);
    }
}
