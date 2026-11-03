using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Stored loop and source NURBS geometry for preservation/token checks. Source
// Source Spline snapshots include the existing native fit-data observation;
// optional native tangent unavailability stays explicit rather than invented.
// No tessellation,
// contour reconstruction, EvaluateHatch or generated-line estimate on reads.
internal static class CadHatchEditingReader
{
    internal sealed record PatternValue(string Name,string Type,double Angle,double Scale,double[] Origin,string Style,bool Double,double Space,bool Solid);
    internal sealed record StoredValue(JsonElement Preserved,PatternValue Pattern,JsonElement Definitions,string[] SourceHandles);
    internal sealed record Snapshot(StoredValue Stored,JsonElement Sources,string Identity,string Token);
    internal sealed record GeometryValue377(JsonElement Preserved,string[] SourceHandles);
    internal sealed record FillBoundary377(GeometryValue377 Geometry,JsonElement Sources,string Identity,double[] Origin,string HatchStyle);
    internal sealed record ReadValue(string hatch_token,string scope,string[] source_handles,HatchBoundaryRead349? boundary_replacement=null,HatchAssociationRead358? association_lifecycle=null);
    private const int SnapshotByteBudget=8*1024*1024;
    internal static void Need(bool value,string message,string code="UNSUPPORTED_HATCH_CONTENT")
    {if(!value)throw new DomainException(code,message);}
    private sealed class Budget
    {
        private long bytes=2048;
        internal void Add(object value)
        {
            bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length+1;
            Need(bytes<=SnapshotByteBudget,"Stored native hatch snapshot exceeds the actual8MiB memory budget; no loop/edge count truncation occurred.","RESPONSE_LIMIT_EXCEEDED");
        }
    }
    internal static double Number(double value)
    {Need(double.IsFinite(value),"Stored native hatch/boundary has a nonfinite property.","INVALID_GEOMETRY");return value==0?0:value;}
    private static double[] Point(Point2d point)=>new[]{Number(point.X),Number(point.Y)};
    private static double[] Point(Point3d point)=>new[]{Number(point.X),Number(point.Y),Number(point.Z)};
    private static double[] Vector(Vector2d vector)=>new[]{Number(vector.X),Number(vector.Y)};
    private static double[] Vector(Vector3d vector)=>new[]{Number(vector.X),Number(vector.Y),Number(vector.Z)};
    private static string Id(ObjectId id)
    {
        Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,"A stored native hatch/boundary identity is unavailable.");
        return Validation.NormalizeHandle(id.Handle.ToString());
    }
    internal static string Style(HatchStyle style)=>style switch{HatchStyle.Normal=>"normal",HatchStyle.Outer=>"outer",HatchStyle.Ignore=>"ignore",_=>style.ToString()};
    private static string[] Reactors(DBObject item,Action cancel)
    {
        using var ids=item.GetPersistentReactorIds();var result=new List<string>();
        foreach(ObjectId id in ids){cancel();result.Add(Id(id));}
        return result.OrderBy(value=>value,StringComparer.Ordinal).ToArray();
    }
    private static JsonElement Metadata(Entity entity,Action cancel,Budget budget,Transaction? tr=null)
    {
        cancel();using var data=entity.XData;
        var xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add);
        var common=new{properties=EntityAdapter.StoredProperties(entity),layer_handle=Id(entity.LayerId),linetype_handle=Id(entity.LinetypeId),
            reactors=Reactors(entity,cancel),extension_handle=entity.ExtensionDictionary.IsNull?null:Id(entity.ExtensionDictionary),annotative=entity.Annotative.ToString()};
        var owned=tr==null?(JsonElement?)null:NativeOwnedMetadata.Capture(entity,tr,cancel);
        budget.Add(common);if(owned.HasValue)budget.Add(owned.Value);
        return Wire.Element(new{common.properties,common.layer_handle,common.linetype_handle,common.reactors,xdata,common.extension_handle,common.annotative,owned_metadata=owned});
    }
    private static double[] Numbers(int count,Func<int,double> getter,Action cancel,Budget budget)
    {
        Need(count>=0,"Native hatch/boundary returned a negative collection count.","OBJECT_READ_FAILED");
        var values=new List<double>();
        for(int i=0;i<count;i++){cancel();double value=Number(getter(i));budget.Add(value);values.Add(value);}
        return values.ToArray();
    }
    private static object Curve(Curve2d curve,Action cancel,Budget budget)
    {
        cancel();using var interval=curve.GetInterval();
        Need(interval.IsBounded,"An unbounded native hatch curve does not define a finite stored boundary.");
        object range=new{start=Number(interval.LowerBound),end=Number(interval.UpperBound)};
        if(curve.GetType()==typeof(LineSegment2d))
        {var line=(LineSegment2d)curve;var value=new{kind="line_segment",range,start=Point(line.StartPoint),end=Point(line.EndPoint)};budget.Add(value);return value;}
        if(curve.GetType()==typeof(CircularArc2d))
        {
            var arc=(CircularArc2d)curve;var value=new{kind="circular_arc",range,center=Point(arc.Center),reference_vector=Vector(arc.ReferenceVector),
                radius=Number(arc.Radius),start_angle=Number(arc.StartAngle),end_angle=Number(arc.EndAngle),clockwise=arc.IsClockWise};
            budget.Add(value);return value;
        }
        if(curve.GetType()==typeof(EllipticalArc2d))
        {
            var ellipse=(EllipticalArc2d)curve;var value=new{kind="elliptical_arc",range,center=Point(ellipse.Center),major_axis=Vector(ellipse.MajorAxis),minor_axis=Vector(ellipse.MinorAxis),
                major_radius=Number(ellipse.MajorRadius),minor_radius=Number(ellipse.MinorRadius),start_angle=Number(ellipse.StartAngle),end_angle=Number(ellipse.EndAngle),clockwise=ellipse.IsClockWise};
            budget.Add(value);return value;
        }
        if(curve.GetType()==typeof(NurbCurve2d))
        {
            var nurb=(NurbCurve2d)curve;var data=nurb.DefinitionData;
            // DefinitionData allocates native backing Point2d/Knot collections;
            // DoubleCollection is managed. Own only these returned wrappers.
            using var points=data.ControlPoints;using var knots=data.Knots;
            var controls=new List<double[]>();
            for(int i=0;i<points.Count;i++){cancel();var point=Point(points[i]);budget.Add(point);controls.Add(point);}
            var weights=data.Weights;
            object? fit=null;
            if(nurb.NumFitPoints>0)
            {
                var fitData=nurb.FitData;using var fitPoints=fitData.FitPoints;var storedPoints=new List<double[]>();
                for(int i=0;i<fitPoints.Count;i++){cancel();var point=Point(fitPoints[i]);budget.Add(point);storedPoints.Add(point);}
                var fitCommon=new{degree=fitData.Degree,tangents_exist=fitData.TangentsExist,start_tangent=fitData.TangentsExist?Vector(fitData.StartTangent):null,end_tangent=fitData.TangentsExist?Vector(fitData.EndTangent):null,
                    equal_point=Number(fitData.FitTolerance.EqualPoint),equal_vector=Number(fitData.FitTolerance.EqualVector),knot_parameterization=fitData.KnotParam.ToString()};
                budget.Add(fitCommon);fit=new{fitCommon.degree,points=storedPoints,fitCommon.tangents_exist,fitCommon.start_tangent,fitCommon.end_tangent,
                    fitCommon.equal_point,fitCommon.equal_vector,fitCommon.knot_parameterization};
            }
            budget.Add(new{kind="nurb_curve",range,degree=data.Degree,rational=data.Rational,periodic=data.Periodic,knot_tolerance=Number(knots.Tolerance)});
            return new{kind="nurb_curve",range,degree=data.Degree,rational=data.Rational,periodic=data.Periodic,controls,
                knots=Numbers(knots.Count,i=>knots[i],cancel,budget),knot_tolerance=Number(knots.Tolerance),
                weights=Numbers(weights.Count,i=>weights[i],cancel,budget),fit};
        }
        throw new DomainException("UNSUPPORTED_HATCH_CONTENT","Stored native hatch curve type requires its owning adapter; no curve sampling/reconstruction was used.",false,new{runtime_type=curve.GetType().FullName});
    }
    private static GeometryValue377 Geometry377(Hatch hatch,Action cancel,Transaction? tr,bool allowGradient)
    {
        cancel();Need(hatch.GetType()==typeof(Hatch),"Expected an exact native Hatch.","UNSUPPORTED_ENTITY_TYPE");
        Need(hatch.HatchObjectType==HatchObjectType.HatchObject||allowGradient&&hatch.HatchObjectType==HatchObjectType.GradientObject,"Gradient fill conversion requires its own adapter.");
        Need(!hatch.HasFields,"Field-bearing hatch evaluation requires its owning field adapter.");
        var budget=new Budget();int count=hatch.NumberOfLoops;
        Need(count>0,"Native hatch has no stored boundary loops.");
        var loops=new List<object>();var associated=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<count;i++)
        {
            cancel();var loop=hatch.GetLoopAt(i);var vertices=new List<object>();var curves=new List<object>();
            if(loop.IsPolyline)foreach(BulgeVertex vertex in loop.Polyline)
            {cancel();var value=new{point_ocs=Point(vertex.Vertex),bulge=Number(vertex.Bulge)};budget.Add(value);vertices.Add(value);}
            else
            {
                var native=loop.Curves;
                try{foreach(Curve2d curve in native){cancel();curves.Add(Curve(curve,cancel,budget));}}
                finally{foreach(Curve2d curve in native)curve.Dispose();}
            }
            using var ids=hatch.GetAssociatedObjectIdsAt(i);var sources=new List<string>();
            foreach(ObjectId id in ids){cancel();string handle=Id(id);sources.Add(handle);associated.Add(handle);}
            Need(vertices.Count>0||curves.Count>0,"Native hatch loop has no readable stored geometry.");
            var common=new{loop_type=(int)loop.LoopType,polyline=loop.IsPolyline,associated_handles=sources};budget.Add(common);
            loops.Add(new{common.loop_type,common.polyline,vertices,curves,common.associated_handles});
        }
        using(var ids=hatch.GetAssociatedObjectIds())
        {
            var all=new HashSet<string>(StringComparer.Ordinal);
            foreach(ObjectId id in ids){cancel();all.Add(Id(id));}
            Need(all.SetEquals(associated),"Native aggregate/per-loop associative references disagree.");
        }
        Need(hatch.Associative||associated.Count==0,"Non-associative hatch retains native boundary references requiring its owning adapter.");
        var preserved=Wire.Element(new{hatch_object_type=hatch.HatchObjectType.ToString(),associative=hatch.Associative,normal=Vector(hatch.Normal),elevation=Number(hatch.Elevation),
            metadata=Metadata(hatch,cancel,budget,tr),loops});
        Need(JsonSerializer.SerializeToUtf8Bytes(preserved,Wire.Json).Length<=SnapshotByteBudget,"Stored Hatch boundary/metadata exceeds the actual8MiB memory budget.","RESPONSE_LIMIT_EXCEEDED");
        return new(preserved,associated.OrderBy(value=>value,StringComparer.Ordinal).ToArray());
    }
    internal static StoredValue Stored(Hatch hatch,Action cancel,Transaction? tr=null)
    {
        var geometry=Geometry377(hatch,cancel,tr,false);var budget=new Budget();
        var definitions=new List<object>();int definitionCount=hatch.NumberOfPatternDefinitions;
        Need(definitionCount>=0,"Native hatch returned a negative pattern-definition count.","OBJECT_READ_FAILED");
        for(int i=0;i<definitionCount;i++)
        {
            cancel();var line=hatch.GetPatternDefinitionAt(i);var dashes=line.GetDashes();
            var common=new{angle=Number(line.Angle),base_point=new[]{Number(line.BaseX),Number(line.BaseY)},offset=new[]{Number(line.OffsetX),Number(line.OffsetY)}};
            budget.Add(common);definitions.Add(new{common.angle,common.base_point,common.offset,dashes=Numbers(dashes.Count,j=>dashes[j],cancel,budget)});
        }
        var pattern=new PatternValue(hatch.PatternName,hatch.PatternType.ToString(),Number(hatch.PatternAngle),Number(hatch.PatternScale),Point(hatch.Origin),
            Style(hatch.HatchStyle),hatch.PatternDouble,Number(hatch.PatternSpace),hatch.IsSolidFill);
        var preserved=geometry.Preserved;
        Need(JsonSerializer.SerializeToUtf8Bytes(new{preserved,pattern,definitions},Wire.Json).Length<=SnapshotByteBudget,"Stored hatch snapshot exceeds the actual8MiB memory budget.","RESPONSE_LIMIT_EXCEEDED");
        return new(preserved,pattern,Wire.Element(definitions),geometry.SourceHandles);
    }
    internal static JsonElement CaptureBoundary358(Entity entity,Transaction tr,Action cancel)=>Boundary(entity,cancel,new Budget(),tr);
    private static JsonElement Boundary(Entity entity,Action cancel,Budget budget,Transaction tr)
    {
        cancel();Need(!entity.HasFields,"Field-bearing associative boundary evaluation requires its owning adapter.");
        object geometry;
        if(entity.GetType()==typeof(Polyline))
        {
            var polyline=(Polyline)entity;var vertices=new List<object>();
            for(int i=0;i<polyline.NumberOfVertices;i++)
            {
                cancel();var vertex=new{point_ocs=Point(polyline.GetPoint2dAt(i)),bulge=Number(polyline.GetBulgeAt(i)),start_width=Number(polyline.GetStartWidthAt(i)),end_width=Number(polyline.GetEndWidthAt(i))};budget.Add(vertex);vertices.Add(vertex);
            }
            geometry=new{closed=polyline.Closed,normal=Vector(polyline.Normal),elevation=Number(polyline.Elevation),thickness=Number(polyline.Thickness),constant_width=Number(polyline.ConstantWidth),vertices};
        }
        else if(entity.GetType()==typeof(Polyline2d))geometry=CadLegacyPolyline408.Geometry((Polyline2d)entity,tr,cancel);
        else if(entity.GetType()==typeof(Line)){var line=(Line)entity;geometry=new{start=Point(line.StartPoint),end=Point(line.EndPoint),normal=Vector(line.Normal),thickness=Number(line.Thickness)};}
        else if(entity.GetType()==typeof(Circle)){var circle=(Circle)entity;geometry=new{center=Point(circle.Center),radius=Number(circle.Radius),normal=Vector(circle.Normal),thickness=Number(circle.Thickness)};}
        else if(entity.GetType()==typeof(Arc)){var arc=(Arc)entity;geometry=new{center=Point(arc.Center),radius=Number(arc.Radius),normal=Vector(arc.Normal),start_angle=Number(arc.StartAngle),end_angle=Number(arc.EndAngle),thickness=Number(arc.Thickness)};}
        else if(entity.GetType()==typeof(Ellipse))
        {
            var ellipse=(Ellipse)entity;geometry=new{center=Point(ellipse.Center),major_axis=Vector(ellipse.MajorAxis),minor_axis=Vector(ellipse.MinorAxis),normal=Vector(ellipse.Normal),
                major_radius=Number(ellipse.MajorRadius),minor_radius=Number(ellipse.MinorRadius),start_parameter=Number(ellipse.StartParam),end_parameter=Number(ellipse.EndParam)};
        }
        else if(entity.GetType()==typeof(Spline))
        {
            geometry=CadNativeCurveInspection342.ReadSpline((Spline)entity,cancel,SnapshotByteBudget);budget.Add(geometry);
        }
        else throw new DomainException("UNSUPPORTED_HATCH_CONTENT","Associative boundary type requires its owning adapter; no boundary was reconstructed.",false,new{runtime_type=entity.GetType().FullName});
        if(entity.GetType()!=typeof(Polyline)&&entity.GetType()!=typeof(Spline))budget.Add(geometry);
        var common=new{handle=Id(entity.ObjectId),owner_handle=Id(entity.OwnerId),runtime_type=entity.GetType().FullName};budget.Add(common);
        return Wire.Element(new{common.handle,common.owner_handle,common.runtime_type,geometry,metadata=Metadata(entity,cancel,budget,tr)});
    }
    private static JsonElement Sources377(Hatch hatch,Database db,Transaction tr,GeometryValue377 geometry,Action cancel)
    {
        var space=tr.GetObject(hatch.OwnerId,OpenMode.ForRead) as BlockTableRecord;
        Need(space!=null&&space.GetType()==typeof(BlockTableRecord)&&space.IsLayout&&!space.IsDependent&&!space.IsFromExternalReference&&!space.IsFromOverlayReference,
            "Hatch owner must be an ordinary local drawing space.","OUT_OF_SCOPE");
        // A normal associative hatch owns its forward IDs; each source owns a
        // reverse hatch reactor. Other reactors of those read-only sources are
        // retained, so a boundary shared by several native hatches is allowed.
        Need(geometry.Preserved.GetProperty("metadata").GetProperty("reactors").GetArrayLength()==0,
            "Hatch has unclassified persistent dependants requiring their owning adapter.");
        // Stored metadata already follows exact native ownership. Soft dictionary
        // references remain identities; they are not this Hatch's owned network.
        Need(!hatch.Associative||geometry.SourceHandles.Length>0,"Associative native hatch has no readable boundary references.");
        var boundaries=new List<JsonElement>();var budget=new Budget();
        foreach(string sourceHandle in geometry.SourceHandles)
        {
            cancel();var id=CadStructureWriter.Find(db,sourceHandle);
            Need(id!=hatch.ObjectId&&tr.GetObject(id,OpenMode.ForRead) is Entity,"Associative boundary reference must be a local native entity.");
            var source=(Entity)tr.GetObject(id,OpenMode.ForRead);
            Need(source.OwnerId==hatch.OwnerId,"Associative boundary ownership differs from this Hatch's native drawing space.","OUT_OF_SCOPE");
            using(var reactors=source.GetPersistentReactorIds())Need(reactors.Cast<ObjectId>().Any(value=>value==hatch.ObjectId),"Associative boundary must retain its reverse native hatch reactor.");
            boundaries.Add(Boundary(source,cancel,budget,tr));
        }
        return Wire.Element(boundaries);
    }
    internal static FillBoundary377 CaptureFillBoundary377(Hatch hatch,Database db,Transaction tr,Action cancel)
    {
        Need(!hatch.ObjectId.IsNull&&db.TryGetObjectId(hatch.Handle,out var resident)&&resident==hatch.ObjectId,"Hatch fill token requires its resident document.","OUT_OF_SCOPE");
        var geometry=Geometry377(hatch,cancel,tr,true);var sources=Sources377(hatch,db,tr,geometry,cancel);
        return new(geometry,sources,Id(hatch.ObjectId)+":"+Id(hatch.OwnerId),Point(hatch.Origin),Style(hatch.HatchStyle));
    }
    internal static Snapshot Capture(Hatch hatch,Database db,Transaction tr,Action cancel)
    {
        Need(!hatch.ObjectId.IsNull&&db.TryGetObjectId(hatch.Handle,out var resident)&&resident==hatch.ObjectId,"Hatch editing token requires its resident document.","OUT_OF_SCOPE");
        var stored=Stored(hatch,cancel,tr);string handle=Id(hatch.ObjectId),owner=Id(hatch.OwnerId);
        var sources=Sources377(hatch,db,tr,new(stored.Preserved,stored.SourceHandles),cancel);string identity=handle+":"+owner;
        var state=Wire.Element(new{stored.Preserved,stored.Pattern,stored.Definitions,sources});
        Need(JsonSerializer.SerializeToUtf8Bytes(state,Wire.Json).Length<=SnapshotByteBudget,"Hatch plus associative-source snapshot exceeds the actual8MiB memory budget.","RESPONSE_LIMIT_EXCEEDED");
        string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{identity,state},Wire.Json)));
        return new(stored,sources,identity,token);
    }
    internal static (object? Editing,string? Reason,PatternValue? Pattern) TryRead(Hatch hatch,Action cancel)
    {
        try
        {
            cancel();Need(hatch.Database!=null,"Hatch editing metadata requires its resident document.");
            using var tr=hatch.Database!.TransactionManager.StartTransaction();var value=Capture(hatch,hatch.Database,tr,cancel);
            var read=new ReadValue(value.Token,"stored_native_loops_and_local_associations",value.Stored.SourceHandles,CadHatchBoundaryReader349.Describe(hatch),CadHatchAssociationWriter358.Describe(hatch,cancel));
            Need(JsonSerializer.SerializeToUtf8Bytes(read,Wire.Json).Length<=Wire.PayloadBudget-16384,"Compact hatch editing metadata exceeds the actual response-byte budget.","RESPONSE_LIMIT_EXCEEDED");
            return(read,null,value.Stored.Pattern);
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return(null,error.Error.code+": "+error.Error.message,null);}
        catch(System.Exception error){return(null,"Stored native hatch editing metadata unavailable: "+error.GetType().Name,null);}
    }
}
