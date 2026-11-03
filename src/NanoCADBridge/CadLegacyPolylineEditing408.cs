using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Native Polyline2d/Vertex2d resident editing. Lightweight entities below are
// detached exact line/arc observations used solely for planning and topology;
// they are never appended, substituted for, or converted from a resident root.
internal static class CadLegacyPolyline408
{
    internal sealed record Vertex(ObjectId Id,string Handle,Point3d Wcs,Point3d Stored,string Kind,
        double Bulge,double StartWidth,double EndWidth,double Tangent,bool TangentUsed);
    internal sealed record Shape(string Kind,bool Closed,double Elevation,Vector3d Normal,double Thickness,
        double DefaultStartWidth,double DefaultEndWidth,Vertex[] Vertices);
    internal sealed record State(Shape Geometry,JsonElement Root,Dictionary<string,JsonElement> Children,string Token);
    internal sealed record HatchState(ObjectId Id,ObjectId[][] Groups,CadHatchEditingReader.StoredValue Before);
    internal sealed record Endpoint(string Role,ObjectId Source,string VertexHandle);
    internal sealed record DimensionState(ObjectId Id,CadDimensionAssociation385.Snapshot Before,Endpoint[] Endpoints);
    internal sealed record Source(ObjectId Id,State Before,Shape Planned,bool Erased);
    internal sealed record Plan(Dictionary<string,Source> Sources,HatchState[] Hatches,DimensionState[] Dimensions);
    private static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    private static void Need([DoesNotReturnIf(false)]bool condition,string message,string code="UNSUPPORTED_LEGACY_POLYLINE")
    {if(!condition)throw new DomainException(code,message);}
    private static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    private static double Finite(double value)
    {Need(double.IsFinite(value),"Legacy geometry contains a nonfinite native value.","INVALID_GEOMETRY");return value;}
    private static double[] Point(Point3d value)=>[Finite(value.X),Finite(value.Y),Finite(value.Z)];
    private static double[] Vector(Vector3d value)=>[Finite(value.X),Finite(value.Y),Finite(value.Z)];
    internal static bool IsLegacy(Entity source)=>source.GetType()==typeof(Polyline2d);
    internal static bool Compatible(EditSpec edit)=>edit is LegacyPolylineEdit408 or EraseSpec or PropertiesSpec or CopyTransform2d407 or PolarRepeat2d407 ||
        edit is MoveSpec or RotateSpec or ScaleSpec or CopySpec or Mirror2dSpec && PlaneCurveTransforms394.Frame(edit)==null;
    private static bool Copying(EditSpec edit)=>edit is CopySpec or CopyTransform2d407 or PolarRepeat2d407 or Mirror2dSpec{Copy:true};
    private static bool Changing(EditSpec edit)=>edit is LegacyPolylineEdit408 or MoveSpec or RotateSpec or ScaleSpec or Mirror2dSpec{Copy:false};

    internal static Shape Read(Polyline2d source,Transaction tr,Action cancel)
    {
        Need(IsLegacy(source),"Select an exact native Polyline2d.","UNSUPPORTED_ENTITY_TYPE");
        var vertices=new List<Vertex>();var capacity=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in source)
        {
            cancel();Need(!id.IsNull&&id.IsValid,"Legacy root contains an invalid child identity.","OBJECT_READ_FAILED");
            if(id.IsErased||id.IsEffectivelyErased)continue;
            var value=tr.GetObject(id,OpenMode.ForRead) as Vertex2d;
            Need(value!=null&&value.GetType()==typeof(Vertex2d)&&value.OwnerId==source.ObjectId&&value.Database==source.Database,
                "Legacy child must be an exact local Vertex2d owned by this root.","INVALID_SOURCE_OWNERSHIP");
            var vertex=new Vertex(id,Id(id),source.VertexPosition(value!),value!.Position,value.VertexType.ToString(),
                Finite(value.Bulge),Finite(value.StartWidth),Finite(value.EndWidth),Finite(value.Tangent),value.TangentUsed);
            // ObjectId is private; transport accounting uses its stable handle.
            capacity.Add(VertexData(vertex));vertices.Add(vertex);
        }
        var result=new Shape(source.PolyType.ToString(),source.Closed,Finite(source.Elevation),source.Normal,Finite(source.Thickness),
            Finite(source.DefaultStartWidth),Finite(source.DefaultEndWidth),vertices.ToArray());
        capacity.Add(Geometry(result));return result;
    }
    private static object VertexData(Vertex vertex)=>new{handle=vertex.Handle,point_wcs=Point(vertex.Wcs),stored_position_ocs=Point(vertex.Stored),
        vertex_type=vertex.Kind,bulge=vertex.Bulge,start_width=vertex.StartWidth,end_width=vertex.EndWidth,tangent=vertex.Tangent,tangent_used=vertex.TangentUsed};
    internal static object Geometry(Shape shape)=>new{polyline_type=shape.Kind,closed=shape.Closed,elevation=shape.Elevation,normal=Vector(shape.Normal),
        thickness=shape.Thickness,default_start_width=shape.DefaultStartWidth,default_end_width=shape.DefaultEndWidth,
        vertex_count=shape.Vertices.Length,vertices=shape.Vertices.Select(VertexData).ToArray()};
    internal static object Geometry(Polyline2d source,Transaction tr,Action cancel)=>Geometry(Read(source,tr,cancel));
    private static JsonElement Metadata(Entity source,Transaction tr,Action cancel)
    {
        var capacity=new ModelInventoryBudget(cancel);using var data=source.XData;using var links=source.GetPersistentReactorIds();
        var reactors=new List<string>();foreach(ObjectId link in links){cancel();Need(Live(link),"A retained persistent dependency is unavailable.");reactors.Add(Id(link));}
        var owned=NativeOwnedMetadata.Capture(source,tr,cancel);
        foreach(var record in owned.EnumerateArray())
        {cancel();Need(record.GetProperty("common").GetProperty("reactors").GetArrayLength()==0,"Owned persistent dependency data needs its owning adapter.","UNSUPPORTED_CURVE_DEPENDENCIES");}
        var result=Wire.Element(new{handle=Id(source.ObjectId),owner_handle=Id(source.OwnerId),runtime_type=source.GetType().FullName,source.HasFields,
            extension_handle=source.ExtensionDictionary.IsNull?null:Id(source.ExtensionDictionary),
            xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,capacity.Add),reactors=reactors.OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
            layer_handle=Id(source.LayerId),properties=EntityAdapter.StoredProperties(source),owned});
        capacity.Add(result);return result;
    }
    internal static State Capture(Polyline2d source,Transaction tr,Action cancel)
    {
        var shape=Read(source,tr,cancel);var root=Metadata(source,tr,cancel);var children=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        var capacity=new ModelInventoryBudget(cancel);capacity.Add(root);
        foreach(var vertex in shape.Vertices){cancel();var metadata=Metadata((Vertex2d)tr.GetObject(vertex.Id,OpenMode.ForRead),tr,cancel);capacity.Add(metadata);children.Add(vertex.Handle,metadata);}
        var payload=new{geometry=Geometry(shape),root,children};capacity.Add(payload);
        string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload,Wire.Json)));
        return new(shape,root,children,token);
    }
    internal static void CheckGeometry(Shape shape,Action cancel)
    {
        Need(shape.Kind=="SimplePoly"&&shape.Vertices.All(vertex=>vertex.Kind=="SimpleVertex"),
            "Fitted/control legacy forms require explicit native fitted semantics; this route never straightens or flattens them.");
        Need(shape.Vertices.Length>=2,"A legacy contour needs at least two remaining vertices.","INVALID_GEOMETRY");
        Need(Math.Abs(shape.Normal.X)<=1e-9&&Math.Abs(shape.Normal.Y)<=1e-9&&Math.Abs(Math.Abs(shape.Normal.Z)-1)<=1e-9&&
            Math.Abs(shape.Elevation)<=1e-9&&Math.Abs(shape.Thickness)<=1e-9,
            "Ordinary legacy edits use the actual WCS XY/Z0 plane with zero thickness; no projection occurs.","PLANAR_GEOMETRY_REQUIRED");
        foreach(var vertex in shape.Vertices)
        {
            cancel();Point(vertex.Wcs);Point(vertex.Stored);
            Need(Math.Abs(vertex.Wcs.Z)<=1e-9&&Math.Abs(vertex.Wcs.X)<=1e9&&Math.Abs(vertex.Wcs.Y)<=1e9&&
                Math.Abs(vertex.Bulge)<=1e9&&vertex.StartWidth>=0&&vertex.EndWidth>=0&&vertex.StartWidth<=1e9&&vertex.EndWidth<=1e9,
                "Legacy vertex geometry must be finite in the supported drawing bounds.","INVALID_GEOMETRY");
        }
        for(int index=0;index<(shape.Closed?shape.Vertices.Length:shape.Vertices.Length-1);index++)
        {cancel();Need(shape.Vertices[index].Wcs.DistanceTo(shape.Vertices[(index+1)%shape.Vertices.Length].Wcs)>1e-9,
            "The requested legacy edit would retain a collapsed segment.","DEGENERATE_GEOMETRY");}
    }
    internal static void CheckGeometry(Polyline2d source,Transaction tr,Action cancel)=>CheckGeometry(Read(source,tr,cancel),cancel);
    private static Polyline Proxy(Shape shape)
    {
        var result=new Polyline(shape.Vertices.Length){Normal=shape.Normal,Elevation=shape.Elevation,Thickness=shape.Thickness,Closed=shape.Closed};
        try{for(int index=0;index<shape.Vertices.Length;index++){var vertex=shape.Vertices[index];result.AddVertexAt(index,new(vertex.Stored.X,vertex.Stored.Y),vertex.Bulge,vertex.StartWidth,vertex.EndWidth);}return result;}
        catch{result.Dispose();throw;}
    }
    private static Shape TransformShape(Shape shape,EditSpec edit,Action cancel)
    {
        using var proxy=Proxy(shape);proxy.TransformBy(CadModifier.Matrix(edit));var vertices=new Vertex[shape.Vertices.Length];
        for(int index=0;index<vertices.Length;index++)
        {
            cancel();var vertex=shape.Vertices[index];var point=proxy.GetPoint2dAt(index);double tangent=vertex.Tangent;
            if(vertex.TangentUsed)
            {
                var direction=new Vector3d(Math.Cos(tangent),Math.Sin(tangent),0).TransformBy(Matrix3d.PlaneToWorld(shape.Normal))
                    .TransformBy(CadModifier.Matrix(edit)).TransformBy(Matrix3d.WorldToPlane(proxy.Normal));
                tangent=Math.Atan2(direction.Y,direction.X);
            }
            vertices[index]=vertex with{Wcs=proxy.GetPoint3dAt(index),Stored=new(point.X,point.Y,vertex.Stored.Z),
                Bulge=proxy.GetBulgeAt(index),StartWidth=proxy.GetStartWidthAt(index),EndWidth=proxy.GetEndWidthAt(index),Tangent=tangent};
        }
        double scale=edit switch{ScaleSpec value=>value.Factor,CopyTransform2d407 value=>value.Scale,_=>1};
        var result=shape with{Normal=proxy.Normal,Elevation=proxy.Elevation,Thickness=proxy.Thickness,
            DefaultStartWidth=shape.DefaultStartWidth*scale,DefaultEndWidth=shape.DefaultEndWidth*scale,Vertices=vertices};
        CheckGeometry(result,cancel);return result;
    }
    private static Point3d Stored(Point2 point,Shape shape,double storedZ)
    {
        var value=new Point3d(point.X,point.Y,0).TransformBy(Matrix3d.WorldToPlane(shape.Normal));
        Need(Math.Abs(value.Z-shape.Elevation)<=1e-7,"Requested vertex does not belong to the retained legacy plane.","PLANAR_GEOMETRY_REQUIRED");
        return new(value.X,value.Y,storedZ);
    }
    private static Shape Edited(Shape shape,LegacyPolylineEdit408 edit,Action cancel)
    {
        var vertices=shape.Vertices.ToList();var original=vertices.ToDictionary(vertex=>vertex.Handle,StringComparer.Ordinal);
        foreach(var change in edit.Vertices)
        {
            cancel();Need(original.TryGetValue(change.VertexHandle,out var before),"Selected vertex does not belong to this root.","VERTEX_NOT_FOUND");
            int index=vertices.FindIndex(vertex=>vertex.Handle==change.VertexHandle);
            vertices[index]=before! with{Wcs=change.Position is {} point?new(point.X,point.Y,0):before!.Wcs,
                Stored=change.Position is {} position?Stored(position,shape,before!.Stored.Z):before!.Stored,
                Bulge=change.Bulge??before!.Bulge,StartWidth=change.StartWidth??before!.StartWidth,EndWidth=change.EndWidth??before!.EndWidth};
        }
        var anchors=new Dictionary<string,string>(StringComparer.Ordinal);int ordinal=0;
        foreach(var insert in edit.Inserts)
        {
            cancel();Need(original.ContainsKey(insert.AfterVertexHandle),"Insertion anchor must be an existing surviving vertex of this root.","VERTEX_NOT_FOUND");
            string anchor=anchors.TryGetValue(insert.AfterVertexHandle,out var last)?last:insert.AfterVertexHandle;
            int index=vertices.FindIndex(vertex=>vertex.Handle==anchor);Need(index>=0,"Insertion anchor is unavailable.","VERTEX_NOT_FOUND");
            string handle="new:"+ordinal++;var stored=Stored(insert.Position,shape,0);
            vertices.Insert(index+1,new(ObjectId.Null,handle,new(insert.Position.X,insert.Position.Y,0),stored,"SimpleVertex",insert.Bulge,insert.StartWidth,insert.EndWidth,0,false));
            anchors[insert.AfterVertexHandle]=handle;
        }
        foreach(string handle in edit.RemoveVertices)
        {cancel();Need(original.ContainsKey(handle),"Removed vertex does not belong to this root.","VERTEX_NOT_FOUND");vertices.RemoveAll(vertex=>vertex.Handle==handle);}
        var result=shape with{Closed=edit.Closed??shape.Closed,Vertices=vertices.ToArray()};CheckGeometry(result,cancel);return result;
    }
    private static HatchState CaptureHatch(Hatch hatch,Polyline2d source,Transaction tr,Action cancel)
    {
        Need(hatch.GetType()==typeof(Hatch)&&hatch.Database==source.Database&&hatch.OwnerId==source.OwnerId&&hatch.Associative&&
            !hatch.HasFields&&hatch.Annotative!=AnnotativeStates.True,"The legacy contour's Hatch relationship needs its owning context.","UNSUPPORTED_CURVE_DEPENDENCIES");
        var layer=(LayerTableRecord)tr.GetObject(hatch.LayerId,OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsDependent,"Dependent Hatch must have an unlocked local layer.","LAYER_UNAVAILABLE");
        var groups=new List<(ObjectId[] Ids,bool Outer)>();bool found=false;
        for(int index=0;index<hatch.NumberOfLoops;index++)
        {
            cancel();using var ids=hatch.GetAssociatedObjectIdsAt(index);var group=ids.Cast<ObjectId>().ToArray();
            Need(group.Length>0&&group.All(Live)&&group.Distinct().Count()==group.Length,
                "Each retained Hatch loop needs its complete live source group.","UNSUPPORTED_CURVE_DEPENDENCIES");
            var loop=hatch.GetLoopAt(index);Need((loop.LoopType&(HatchLoopTypes.NotClosed|HatchLoopTypes.SelfIntersecting|HatchLoopTypes.Duplicate))==0,
                "The retained Hatch has an invalid or duplicate loop requiring its owning edit.","UNSUPPORTED_CURVE_DEPENDENCIES");
            found|=group.Contains(source.ObjectId);groups.Add((group,(loop.LoopType&HatchLoopTypes.External)!=0));
        }
        Need(found,"Hatch reverse dependency lacks its matching native forward source ID.","UNSUPPORTED_CURVE_DEPENDENCIES");
        Need(groups.Count(group=>group.Outer)==1,"Retained Hatch needs one classified native outer loop and its separate holes.","UNSUPPORTED_CURVE_DEPENDENCIES");
        return new(hatch.ObjectId,groups.OrderByDescending(group=>group.Outer).Select(group=>group.Ids).ToArray(),CadHatchEditingReader.Stored(hatch,cancel,tr));
    }
    internal static State CheckSource(Polyline2d source,IReadOnlyCollection<EditSpec> operations,Database db,Transaction tr,Action cancel)
    {
        Need(operations.Count>0&&operations.All(Compatible),"This Polyline2d supports ordinary placement/copy/erase/properties and set_legacy_polyline.","UNSUPPORTED_ENTITY_TYPE");
        Need(source.Database==db&&Live(source.ObjectId)&&tr.GetObject(source.OwnerId,OpenMode.ForRead) is BlockTableRecord{IsLayout:true},
            "Legacy editing requires a live top-level root in the selected ordinary drawing space.","OUT_OF_SCOPE");
        Need(!source.HasFields&&source.Annotative!=AnnotativeStates.True,"Field-bearing or annotative legacy roots need their owning adapter.","UNSUPPORTED_CURVE_DEPENDENCIES");
        var snapshot=Capture(source,tr,cancel);CheckGeometry(snapshot.Geometry,cancel);
        CadIndependentCurve.RequireModelIndependence(source,db,tr,cancel);
        using var links=source.GetPersistentReactorIds();
        foreach(ObjectId id in links)
        {
            cancel();Need(Live(id),"A legacy dependency is unavailable.","UNSUPPORTED_CURVE_DEPENDENCIES");var value=tr.GetObject(id,OpenMode.ForRead);
            if(value is Hatch hatch)_=CaptureHatch(hatch,source,tr,cancel);
            else if(value.GetType()==typeof(DimAssoc)&&value is DimAssoc)CadDimensionAssociation385.PreflightFollowers(source,db,tr,cancel);
            else if(!CadDrawingGroups431.OrganizationalReactor(source,id,tr,cancel))throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES","The legacy contour has a geometric persistent dependency requiring its owning adapter.",false,new{runtime_type=value.GetType().FullName});
        }
        Need(!operations.Any(edit=>edit is EraseSpec)||!CadDrawingGroups431.HasNonOrganizationalReactors(source,tr,cancel),"Erasing a source with required native Hatch/dimension links needs an explicit owning relationship edit.","UNSUPPORTED_CURVE_DEPENDENCIES");
        foreach(var vertex in snapshot.Geometry.Vertices)
        {
            cancel();var metadata=snapshot.Children[vertex.Handle];
            Need(!metadata.GetProperty("HasFields").GetBoolean()&&metadata.GetProperty("reactors").GetArrayLength()==0,
                "A Field-bearing or linked Vertex2d needs its native subentity adapter.","UNSUPPORTED_CURVE_DEPENDENCIES");
        }
        foreach(var edit in operations.OfType<LegacyPolylineEdit408>())
        {
            Need(snapshot.Token==edit.PolylineToken,"Legacy root/vertex/dependency data differs from polyline_token at request entry.","POLYLINE_STATE_CHANGED");
            foreach(string handle in edit.RemoveVertices)
            {
                Need(snapshot.Children.TryGetValue(handle,out var metadata),"Removed vertex does not belong to this root.","VERTEX_NOT_FOUND");
                Need(metadata.GetProperty("xdata").ValueKind==JsonValueKind.Null&&metadata.GetProperty("extension_handle").ValueKind==JsonValueKind.Null,
                    "Removing this vertex would discard application/owned data; use an explicit owning edit.","UNSUPPORTED_CURVE_DEPENDENCIES");
            }
        }
        return snapshot;
    }
    internal static Plan Prepare(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,Action cancel,IReadOnlyDictionary<string,State>? initialStates=null)
    {
        var sources=new Dictionary<string,Source>(StringComparer.Ordinal);var hatches=new Dictionary<ObjectId,HatchState>();var dimensions=new HashSet<ObjectId>();
        foreach(var pair in resolved.Sources)
        {
            cancel();if(tr.GetObject(pair.Value,OpenMode.ForRead) is not Polyline2d source||!IsLegacy(source))continue;
            var operations=args.Operations.Where(edit=>edit.Handles.Contains(pair.Key)).ToArray();
            var before=initialStates!=null&&initialStates.TryGetValue(pair.Key,out var observed)?observed:CheckSource(source,operations,source.Database,tr,cancel);var planned=before.Geometry;
            foreach(var edit in operations)
            {
                cancel();if(edit is LegacyPolylineEdit408 sparse)planned=Edited(planned,sparse,cancel);
                else if(Changing(edit))planned=TransformShape(planned,edit,cancel);
                else if(Copying(edit))
                {
                    if(CopyTransforms2d407.Handles(edit))foreach(var copy in CopyTransforms2d407.Expand(edit,cancel))_=TransformShape(planned,copy,cancel);
                    else _=TransformShape(planned,edit,cancel);
                }
            }
            sources.Add(pair.Key,new(pair.Value,before,planned,operations.Any(edit=>edit is EraseSpec)));
            if(!operations.Any(Changing))continue;
            using var links=source.GetPersistentReactorIds();
            foreach(ObjectId id in links)
            {
                cancel();var value=tr.GetObject(id,OpenMode.ForRead);
                if(value is Hatch hatch){if(!hatches.ContainsKey(id))hatches.Add(id,CaptureHatch(hatch,source,tr,cancel));}
                else if(value is DimAssoc association)
                {
                    Need(planned.Closed==before.Geometry.Closed&&planned.Vertices[0].Handle==before.Geometry.Vertices[0].Handle&&
                        planned.Vertices[^1].Handle==before.Geometry.Vertices[^1].Handle,
                        "A retained whole-endpoint dimension needs the same open source endpoints; closure or endpoint removal requires rebinding.","UNSUPPORTED_CURVE_DEPENDENCIES");
                    dimensions.Add(association.DimObjId);
                }
            }
        }
        var dimensionStates=new List<DimensionState>();
        foreach(ObjectId id in dimensions)
        {
            cancel();var dimension=(Dimension)tr.GetObject(id,OpenMode.ForRead);
            var layer=(LayerTableRecord)tr.GetObject(dimension.LayerId,OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsDependent,"Dependent dimension must have an unlocked local layer.","LAYER_UNAVAILABLE");
            var before=CadDimensionAssociation385.Capture(dimension,dimension.Database,tr,cancel);
            if(before.Flags==0)continue;var endpoints=new List<Endpoint>();
            foreach(var point in before.Points)
            {
                cancel();if(!sources.TryGetValue(Id(point.Source),out var source))continue;
                var original=source.Before.Geometry;
                Need(point.Snap is ObjectSnapModes.ModeStartpoint or ObjectSnapModes.ModeEnd,"A legacy dimension source needs its actual whole-curve endpoint snap.","UNSUPPORTED_CURVE_DEPENDENCIES");
                bool first=point.Evaluated.DistanceTo(original.Vertices[0].Wcs)<=1e-7,last=point.Evaluated.DistanceTo(original.Vertices[^1].Wcs)<=1e-7;
                Need(first!=last,"The legacy dimension snap is not an unambiguous retained source endpoint; coincident start/end coordinates do not identify a Vertex2d.","UNSUPPORTED_CURVE_DEPENDENCIES");
                string vertex=first?original.Vertices[0].Handle:original.Vertices[^1].Handle;
                endpoints.Add(new(point.Role,point.Source,vertex));
            }
            dimensionStates.Add(new(id,before,endpoints.ToArray()));
        }
        var plan=new Plan(sources,hatches.Values.ToArray(),dimensionStates.ToArray());
        var followers=plan.Hatches.Select(hatch=>Id(hatch.Id)).Concat(plan.Dimensions.Select(dimension=>Id(dimension.Id))).ToHashSet(StringComparer.Ordinal);
        Need(!args.Operations.Any(edit=>edit.Handles.Any(followers.Contains)&&!Copying(edit)),
            "A retained contour follower cannot also be edited or erased directly in this request. Refresh the Hatch/dimension after the contour batch, then apply its separate requested edit.","INVALID_OPERATION_ORDER");
        foreach(var hatch in plan.Hatches)
            foreach(ObjectId id in hatch.Groups.SelectMany(group=>group))
                Need(plan.Sources.ContainsKey(Id(id))||!args.Operations.Any(edit=>edit.Handles.Contains(Id(id))&&!Copying(edit)&&edit is not PropertiesSpec),
                    "A shared Hatch boundary changed through another representation needs its complete owning batch route.","UNSUPPORTED_CURVE_DEPENDENCIES");
        foreach(var hatch in plan.Hatches)_=HatchGeometry(hatch,plan,tr,cancel,false);
        return plan;
    }
    internal static void VerifyInitial(Plan plan,Transaction tr,Action cancel)
    {
        foreach(var source in plan.Sources.Values)
        {
            cancel();Need(Capture((Polyline2d)tr.GetObject(source.Id,OpenMode.ForRead),tr,cancel).Token==source.Before.Token,
                "Native preview/preparation changed the retained original legacy root or vertex data.","NATIVE_LEGACY_POLYLINE_SOURCE_CHANGED");
        }
    }
    private static CadHatchAssociationGeometry362.Boundary[] HatchGeometry(HatchState before,Plan plan,Transaction tr,Action cancel,bool actual)
    {
        var hatch=(Hatch)tr.GetObject(before.Id,OpenMode.ForRead);var proxies=new List<Polyline>();
        var observations=new Dictionary<Entity,Entity>(ReferenceEqualityComparer.Instance);
        try
        {
            var groups=before.Groups.Select(group=>group.Select(id=>
            {
                cancel();var value=(Entity)tr.GetObject(id,OpenMode.ForRead);
                Need(value.Database==hatch.Database&&value.OwnerId==hatch.OwnerId,"Dependent Hatch boundary has foreign ownership.","OUT_OF_SCOPE");
                if(value is not Polyline2d legacy)return value;
                var shape=!actual&&plan.Sources.TryGetValue(Id(id),out var source)?source.Planned:Read(legacy,tr,cancel);CheckGeometry(shape,cancel);
                var proxy=Proxy(shape);proxies.Add(proxy);observations.Add(proxy,legacy);return (Entity)proxy;
            }).ToArray()).ToArray();
            var result=CadHatchAssociationTopology362.Validate(groups,hatch,cancel,observations);
            for(int index=0;index<result.Length;index++)result[index]=result[index] with{Handle=Id(before.Groups[index][0]),Handles=before.Groups[index].Select(Id).ToArray()};
            return result;
        }
        finally{foreach(var proxy in proxies)proxy.Dispose();}
    }
    private static bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-8+Math.Max(Math.Abs(a),Math.Abs(b))*1e-12;
    private static void VerifyShape(Shape expected,Shape actual,Action cancel,bool identities=true)
    {
        Need(expected.Kind==actual.Kind&&expected.Closed==actual.Closed&&expected.Vertices.Length==actual.Vertices.Length&&
            expected.Normal.IsEqualTo(actual.Normal)&&Near(expected.Elevation,actual.Elevation)&&Near(expected.Thickness,actual.Thickness)&&
            Near(expected.DefaultStartWidth,actual.DefaultStartWidth)&&Near(expected.DefaultEndWidth,actual.DefaultEndWidth),
            "Native legacy editing changed the requested topology, plane or default widths.","NATIVE_LEGACY_POLYLINE_MISMATCH");
        for(int index=0;index<expected.Vertices.Length;index++)
        {
            cancel();var a=expected.Vertices[index];var b=actual.Vertices[index];
            Need((!identities||a.Handle.StartsWith("new:",StringComparison.Ordinal)||a.Handle==b.Handle)&&a.Kind==b.Kind&&a.Wcs.DistanceTo(b.Wcs)<=1e-7&&
                a.Stored.DistanceTo(b.Stored)<=1e-7&&Near(a.Bulge,b.Bulge)&&Near(a.StartWidth,b.StartWidth)&&Near(a.EndWidth,b.EndWidth)&&
                a.TangentUsed==b.TangentUsed&&(!a.TangentUsed||Math.Abs(Math.IEEERemainder(a.Tangent-b.Tangent,2*Math.PI))<=1e-8),
                "Native legacy edit did not retain expected vertex identities, positions, bulges, widths and tangent data.","NATIVE_LEGACY_POLYLINE_MISMATCH");
        }
    }
    private static void Retained(State before,State after,Action cancel,bool properties=false,bool transformed=false)
    {
        JsonElement Filter(JsonElement metadata,bool root)=>Wire.Element(metadata.EnumerateObject().Where(property=>
            !(root&&properties&&(property.Name is "properties" or "layer_handle"))&&!(transformed&&property.Name=="xdata")).ToDictionary(property=>property.Name,property=>property.Value.Clone(),StringComparer.Ordinal));
        Need(Exact(Filter(before.Root,true),Filter(after.Root,true)),"Legacy edit changed unrequested root identity, owner, display or owned data.","NATIVE_LEGACY_POLYLINE_MISMATCH");
        foreach(var pair in after.Children)
        {
            cancel();if(!before.Children.TryGetValue(pair.Key,out var old))continue;
            Need(Exact(Filter(old,false),Filter(pair.Value,false)),"Legacy edit changed a surviving vertex's unrequested metadata or display.","NATIVE_LEGACY_POLYLINE_MISMATCH");
        }
    }
    private static void Sparse(Polyline2d source,LegacyPolylineEdit408 edit,Transaction tr,Action cancel)
    {
        var before=Capture(source,tr,cancel);var expected=Edited(before.Geometry,edit,cancel);var vertices=before.Geometry.Vertices.ToDictionary(vertex=>vertex.Handle,StringComparer.Ordinal);
        foreach(var change in edit.Vertices)
        {
            cancel();var vertex=(Vertex2d)tr.GetObject(vertices[change.VertexHandle].Id,OpenMode.ForWrite);
            if(change.Position is {} point)vertex.Position=Stored(point,before.Geometry,vertex.Position.Z);
            if(change.Bulge is {} bulge)vertex.Bulge=bulge;if(change.StartWidth is {} start)vertex.StartWidth=start;if(change.EndWidth is {} end)vertex.EndWidth=end;
        }
        var anchors=new Dictionary<string,ObjectId>(StringComparer.Ordinal);
        foreach(var insert in edit.Inserts)
        {
            cancel();var anchor=anchors.TryGetValue(insert.AfterVertexHandle,out var last)?last:vertices[insert.AfterVertexHandle].Id;
            var vertex=new Vertex2d(Stored(insert.Position,before.Geometry,0),insert.Bulge,insert.StartWidth,insert.EndWidth,0){TangentUsed=false};
            try
            {
                var id=source.InsertVertexAt(anchor,vertex);Need(Live(id)&&vertex.ObjectId==id&&vertex.OwnerId==source.ObjectId,
                    "Native InsertVertexAt did not return a live Vertex2d owned by the retained root.","NATIVE_LEGACY_POLYLINE_MISMATCH");
                tr.AddNewlyCreatedDBObject(vertex,true);anchors[insert.AfterVertexHandle]=id;
            }
            catch{if(vertex.ObjectId.IsNull)vertex.Dispose();throw;}
        }
        foreach(string handle in edit.RemoveVertices){cancel();tr.GetObject(vertices[handle].Id,OpenMode.ForWrite).Erase(true);}
        if(edit.Closed is {} closed)source.Closed=closed;
        source.RecordGraphicsModified(true);var after=Capture(source,tr,cancel);VerifyShape(expected,after.Geometry,cancel);Retained(before,after,cancel);
    }
    private static void Transform(Polyline2d source,EditSpec edit,Transaction tr,Action cancel)
    {
        var before=Capture(source,tr,cancel);var expected=TransformShape(before.Geometry,edit,cancel);
        var matrix=CadModifier.Matrix(edit);var data=new Dictionary<ObjectId,(JsonElement Before,JsonElement Expected)>();
        void PrepareData(Entity value,JsonElement metadata)
        {
            cancel();var original=metadata.GetProperty("xdata");if(original.ValueKind==JsonValueKind.Null){data.Add(value.ObjectId,(original,original));return;}
            using var copy=value.Clone() as Entity;
            Need(copy!=null&&copy.GetType()==value.GetType()&&copy.ObjectId.IsNull&&copy.OwnerId.IsNull&&copy.UnmanagedObject!=value.UnmanagedObject,
                "Native XData preparation requires an independent detached entity.","UNSAFE_NATIVE_CLONE");
            copy!.XDataTransformBy(matrix);data.Add(value.ObjectId,(original,XData(copy,cancel)));
        }
        PrepareData(source,before.Root);
        foreach(var vertex in before.Geometry.Vertices)PrepareData((Vertex2d)tr.GetObject(vertex.Id,OpenMode.ForRead),before.Children[vertex.Handle]);
        Need(Capture(source,tr,cancel).Token==before.Token,"Native XData preparation changed the retained original legacy data.","NATIVE_LEGACY_POLYLINE_SOURCE_CHANGED");
        cancel();source.TransformBy(matrix);
        foreach(var pair in data)
        {
            cancel();var value=(Entity)tr.GetObject(pair.Key,OpenMode.ForRead);var current=XData(value,cancel);
            if(!Exact(current,pair.Value.Expected)&&Exact(current,pair.Value.Before))
            {value=(Entity)tr.GetObject(pair.Key,OpenMode.ForWrite);value.XDataTransformBy(matrix);}
            Need(Exact(XData(value,cancel),pair.Value.Expected),"Native legacy placement did not retain the requested world-XData transform.","NATIVE_LEGACY_POLYLINE_MISMATCH");
        }
        source.RecordGraphicsModified(true);var after=Capture(source,tr,cancel);
        VerifyShape(expected,after.Geometry,cancel);Retained(before,after,cancel,transformed:true);
    }
    private static JsonElement XData(Entity source,Action cancel)
    {
        using var data=source.XData;var capacity=new ModelInventoryBudget(cancel);
        return JsonSerializer.SerializeToElement<object?>(data==null?null:NativeOwnedMetadata.Buffer(data,cancel,capacity.Add),Wire.Json);
    }
    private static Polyline2d Copy(Polyline2d source,EditSpec edit,ObjectId owner,Transaction tr,Action cancel)
    {
        var before=Capture(source,tr,cancel);using var ids=new ObjectIdCollection([source.ObjectId]);using var mapping=new IdMapping();
        source.Database.DeepCloneObjects(ids,owner,mapping,false);cancel();Need(mapping.Contains(source.ObjectId),"Native copy returned no mapped legacy root.","UNSAFE_NATIVE_CLONE");
        var mapped=mapping.Lookup(source.ObjectId);Need(mapped.IsCloned&&Live(mapped.Value)&&mapped.Value!=source.ObjectId,"Native legacy copy must have a distinct resident identity.","UNSAFE_NATIVE_CLONE");
        var copy=tr.GetObject(mapped.Value,OpenMode.ForWrite) as Polyline2d;
        Need(copy!=null&&IsLegacy(copy)&&copy.OwnerId==owner&&copy.Database==source.Database&&copy.UnmanagedObject!=source.UnmanagedObject,
            "Native legacy copy has an unexpected type/owner or aliases its source.","UNSAFE_NATIVE_CLONE");
        var remap=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(IdPair pair in mapping)
        {
            cancel();if(!pair.Key.IsNull&&!pair.Value.IsNull)remap[Id(pair.Key)]=Id(pair.Value);
            if(pair.IsCloned&&pair.Value!=copy!.ObjectId&&tr.GetObject(pair.Value,OpenMode.ForRead) is Entity child)
                Need(child.OwnerId!=owner,"Native legacy copy produced an additional unreported top-level entity.","UNSAFE_NATIVE_CLONE");
        }
        // A one-time contour copy is independent. Never retain reverse links
        // that still identify another Hatch/dimension's original source.
        using(var links=copy!.GetPersistentReactorIds())foreach(ObjectId id in links)
        {
            cancel();var dependency=tr.GetObject(id,OpenMode.ForRead);
            Need(dependency is Hatch or DimAssoc,"Native copy introduced an unclassified persistent dependency.","UNSAFE_NATIVE_CLONE");copy.RemovePersistentReactor(id);
        }
        var after=Capture(copy!,tr,cancel);VerifyShape(before.Geometry,after.Geometry,cancel,false);
        JsonElement WithoutLinks(JsonElement metadata)=>Wire.Element(metadata.EnumerateObject().ToDictionary(property=>property.Name,
            property=>property.Name=="reactors"?Wire.Element(Array.Empty<string>()):property.Value.Clone(),StringComparer.Ordinal));
        Need(Exact(CadAnnotationMirrorEditor340.Remap(WithoutLinks(before.Root),remap,cancel),after.Root),
            "Native legacy copy lost mapped root display/application/owned data.","UNSAFE_NATIVE_CLONE");
        foreach(var vertex in before.Geometry.Vertices)
        {
            cancel();Need(remap.TryGetValue(vertex.Handle,out var handle),"Native legacy copy did not map every surviving Vertex2d.","UNSAFE_NATIVE_CLONE");
            Need(after.Children.TryGetValue(handle,out var metadata),"Native legacy copy did not retain its mapped Vertex2d.","UNSAFE_NATIVE_CLONE");
            Need(Exact(CadAnnotationMirrorEditor340.Remap(before.Children[vertex.Handle],remap,cancel),metadata),
                "Native legacy copy lost a vertex's mapped display/application/owned data.","UNSAFE_NATIVE_CLONE");
        }
        Need(Capture(source,tr,cancel).Token==before.Token,"Native legacy copy preparation changed its original source.","NATIVE_LEGACY_POLYLINE_SOURCE_CHANGED");
        Transform(copy!,edit,tr,cancel);return copy!;
    }
    internal static Polyline2d[] ApplyOperation(Polyline2d source,EditSpec edit,ObjectId owner,Transaction tr,
        CadModifier.PropertyIds? propertyIds,Action cancel,List<string>? warnings=null)
    {
        Need(Compatible(edit),"Unsupported legacy editing operation.","UNSUPPORTED_ENTITY_TYPE");
        if(edit is LegacyPolylineEdit408 sparse){Sparse(source,sparse,tr,cancel);return [];}
        if(edit is PropertiesSpec properties)
        {
            var before=Capture(source,tr,cancel);CadModifier.ApplyProperties(source,properties,propertyIds!);var after=Capture(source,tr,cancel);
            VerifyShape(before.Geometry,after.Geometry,cancel);Retained(before,after,cancel,properties:true);return [];
        }
        if(edit is EraseSpec){source.Erase(true);return [];}
        if(Copying(edit))
        {
            warnings?.Add("LEGACY_POLYLINE_COPY_INDEPENDENT: Native root/vertices and ordinary owned data are copied. Existing Hatch/dimension relationships remain on the retained source; the copy does not acquire those associations.");
            return CopyTransforms2d407.Handles(edit)?CopyTransforms2d407.Expand(edit,cancel).Select(copy=>Copy(source,copy,owner,tr,cancel)).ToArray():[Copy(source,edit,owner,tr,cancel)];
        }
        Transform(source,edit,tr,cancel);return [];
    }
    internal static void Complete(Plan plan,Transaction tr,Action cancel,List<string>? warnings=null)
    {
        foreach(var saved in plan.Hatches)
        {
            cancel();var expected=HatchGeometry(saved,plan,tr,cancel,true);var hatch=(Hatch)tr.GetObject(saved.Id,OpenMode.ForWrite);
            for(int index=hatch.NumberOfLoops-1;index>=0;index--){cancel();hatch.RemoveLoopAt(index);}
            for(int index=0;index<saved.Groups.Length;index++)
            {
                cancel();using var ids=new ObjectIdCollection(saved.Groups[index]);hatch.AppendLoop(index==0?HatchLoopTypes.External:HatchLoopTypes.Default,ids);
                foreach(ObjectId id in saved.Groups[index])
                {
                    var source=(Entity)tr.GetObject(id,OpenMode.ForWrite);using var links=source.GetPersistentReactorIds();
                    int count=links.Cast<ObjectId>().Count(link=>link==hatch.ObjectId);Need(count<=1,"Native Hatch refresh duplicated a source reverse link.","NATIVE_LEGACY_POLYLINE_MISMATCH");
                    if(count==0)source.AddPersistentReactor(hatch.ObjectId);
                }
            }
            hatch.EvaluateHatch(true);hatch.RecordGraphicsModified(true);cancel();CadHatchAssociationGeometry362.Verify(hatch,expected,cancel);
            var after=CadHatchEditingReader.Stored(hatch,cancel,tr);
            Need(Exact(saved.Before.Pattern,after.Pattern)&&Exact(saved.Before.Definitions,after.Definitions)&&saved.Before.SourceHandles.SequenceEqual(after.SourceHandles),
                "Native contour update changed the retained Hatch pattern, definitions or source IDs.","NATIVE_LEGACY_POLYLINE_MISMATCH");
            foreach(var field in saved.Before.Preserved.EnumerateObject())
                if(field.Name!="loops")Need(Exact(field.Value,after.Preserved.GetProperty(field.Name)),"Native contour update changed unrequested Hatch metadata/plane/associativity.","NATIVE_LEGACY_POLYLINE_MISMATCH");
        }
        foreach(var source in plan.Sources.Values)
        {
            cancel();if(source.Erased)continue;var current=(Polyline2d)tr.GetObject(source.Id,OpenMode.ForRead);VerifyShape(source.Planned,Read(current,tr,cancel),cancel);
        }
        foreach(var saved in plan.Dimensions)
        {
            cancel();var dimension=(Dimension)tr.GetObject(saved.Id,OpenMode.ForWrite);
            var points=saved.Endpoints.ToDictionary(endpoint=>endpoint.Role,endpoint=>
                Read((Polyline2d)tr.GetObject(endpoint.Source,OpenMode.ForRead),tr,cancel).Vertices.Single(vertex=>vertex.Handle==endpoint.VertexHandle).Wcs,StringComparer.Ordinal);
            CadDimensionAssociation385.RefreshRetainedLegacyEndpoints408(dimension,saved.Before,points,dimension.Database,tr,cancel);
        }
        if(plan.Dimensions.Length>0)warnings?.Add(CadDimensionAssociation385.Warning);
        if(plan.Hatches.Length>0)warnings?.Add("LEGACY_POLYLINE_HATCH_REFRESH: Retained native Hatch source IDs, holes, fill settings and reverse links were refreshed from the edited source geometry. Native recovery/Undo is not guaranteed; inspect the resulting fill.");
    }
    internal static object Editing(Polyline2d source,Transaction tr,Action cancel)
    {
        try
        {
            var state=Capture(source,tr,cancel);CheckGeometry(state.Geometry,cancel);
            Need(!source.HasFields&&source.Annotative!=AnnotativeStates.True,"Field-bearing/annotative legacy geometry needs its owning adapter.","UNSUPPORTED_CURVE_DEPENDENCIES");
            foreach(var child in state.Children.Values)
                Need(!child.GetProperty("HasFields").GetBoolean()&&child.GetProperty("reactors").GetArrayLength()==0,
                    "A Field-bearing or linked Vertex2d needs its native subentity adapter.","UNSUPPORTED_CURVE_DEPENDENCIES");
            using var links=source.GetPersistentReactorIds();
            foreach(ObjectId link in links)
            {
                cancel();var value=tr.GetObject(link,OpenMode.ForRead);
                Need(value is Hatch or DimAssoc,"The legacy root has a persistent dependency requiring its owning adapter.","UNSUPPORTED_CURVE_DEPENDENCIES");
            }
            // This is read readiness, not a model-ownership scan or an assertion
            // that every external association is editable. Write resolves links.
            return new{polyline_token=state.Token,editable=true,unavailable_reason=(string?)null,
                coordinate_frame="absolute_WCS_XY_Z0_mapped_to_retained_native_OCS",
                scope="ordinary_SimplePoly_retained_root_and_surviving_vertices; native_links_checked_by_write_preflight"};
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            string reason=error is DomainException domain?domain.Error.code+": "+domain.Error.message:error.GetType().Name;
            return new{polyline_token=(string?)null,editable=false,unavailable_reason=reason,
                coordinate_frame="reported_native_OCS_and_WCS; no_projection_or_conversion",
                scope="readable_native_legacy_definition; sparse_edit_requires_ordinary_SimplePoly"};
        }
    }
}
