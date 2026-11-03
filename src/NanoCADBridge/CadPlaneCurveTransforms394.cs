using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Native placement in a declared physical plane. No coordinate reconstruction,
// projection, UCS changes or resident normalization.
internal static class CadPlaneCurveTransforms394
{
    internal sealed record State(CadContourBudget344.SourceState Core,JsonElement? Owned);
    internal sealed record Saved(ObjectId Id,SketchPlaneSpec Frame,State Before);
    static void Need([DoesNotReturnIf(false)]bool valid,string message,string code="NATIVE_PLANE_TRANSFORM_MISMATCH")
    {if(!valid)throw new DomainException(code,message);}
    static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    internal static SketchPlaneSpec? Frame(EditSpec edit)=>PlaneCurveTransforms394.Frame(edit);
    internal static SketchPlaneSpec? Frame(EditSpec[] operations,string handle)
    {
        var relevant=operations.Where(edit=>edit.Handles.Contains(handle)).ToArray();
        var framed=relevant.Where(edit=>Frame(edit)!=null).ToArray();
        if(framed.Length==0)return null;
        var frame=Frame(framed[0])!;
        Need(relevant.All(edit=>edit is PropertiesSpec||Frame(edit)==frame),
            "A plane-transform target may share only transforms in the same explicit frame and appearance edits; use separate requests for other geometry routes or legacy transforms.","INVALID_OPERATION_ORDER");
        return frame;
    }
    static bool Supported(Type type)=>type==typeof(Line)||type==typeof(Circle)||type==typeof(Arc)||
        type==typeof(Ellipse)||type==typeof(Spline)||type==typeof(Polyline);
    internal static Matrix3d Matrix(EditSpec edit)
    {
        var frame=Frame(edit)??throw new DomainException("INVALID_ARGUMENT","Use an explicit workplane.");
        var world=DrawingWorkplaneNative.Matrix(frame);
        return world*CadModifier.Matrix(PlaneCurveTransforms394.WithoutFrame(edit))*world.Inverse();
    }
    internal static void Check(Entity entity,SketchPlaneSpec frame,Action cancel)
    {
        Need(Supported(entity.GetType()),"Explicit-plane transformations support exact independent native Line/Circle/Arc/Ellipse/Spline/Polyline; blocks, annotations and bodies retain their owning routes.","UNSUPPORTED_ENTITY_TYPE");
        var n=DrawingWorkplane.Normal(frame);var normal=new Vector3d(n.X,n.Y,n.Z);
        void Number(double value,bool positive=false)=>Need(double.IsFinite(value)&&Math.Abs(value)<=1e9&&(!positive||value>=1e-9),
            "Native curve geometry is nonfinite, excessive or degenerate.","INVALID_GEOMETRY");
        void Point(Point3d point)
        {
            Number(point.X);Number(point.Y);Number(point.Z);
            var relative=point-new Point3d(frame.Origin.X,frame.Origin.Y,frame.Origin.Z);
            Need(Math.Abs(relative.DotProduct(normal))<=EllipseVerification.Tolerance,
                "The actual curve does not belong to the declared plane; no projection is performed.","PLANAR_GEOMETRY_REQUIRED");
        }
        void Direction(Vector3d direction,bool oriented=true)
        {
            Number(direction.X);Number(direction.Y);Number(direction.Z);
            Need(Math.Abs(direction.Length-1)<=1e-10,"A native unit normal is required.","INVALID_GEOMETRY");
            if(oriented)Need(direction.CrossProduct(normal).Length<=1e-10&&Math.Abs(Math.Abs(direction.DotProduct(normal))-1)<=1e-10,
                "The actual native normal is not parallel to the declared plane; reflected normal direction is retained.","PLANAR_GEOMETRY_REQUIRED");
        }
        void Zero(double value){Number(value);Need(Math.Abs(value)<=1e-9,"Zero-thickness independent curve placement is required.","OUT_OF_SCOPE");}
        Point3d P(double[] point)=>new(point[0],point[1],point[2]);
        cancel();
        switch(entity)
        {
            case Line line:Point(line.StartPoint);Point(line.EndPoint);Direction(line.Normal,false);Zero(line.Thickness);Number(line.StartPoint.DistanceTo(line.EndPoint),true);break;
            case Circle circle:Point(circle.Center);Direction(circle.Normal);Zero(circle.Thickness);Number(circle.Radius,true);break;
            case Arc arc:Point(arc.Center);Point(arc.StartPoint);Point(arc.EndPoint);Direction(arc.Normal);Zero(arc.Thickness);Number(arc.Radius,true);break;
            case Ellipse ellipse:
                var e=CadEllipseReader.Read(ellipse,cancel);
                Point(P(e.center));Point(P(e.start_point));Point(P(e.end_point));Direction(ellipse.Normal);
                Need(Math.Abs(ellipse.MajorAxis.DotProduct(normal))<=EllipseVerification.Tolerance&&Math.Abs(ellipse.MinorAxis.DotProduct(normal))<=EllipseVerification.Tolerance,
                    "The complete native ellipse axes must lie in the declared plane.","PLANAR_GEOMETRY_REQUIRED");break;
            case Spline spline:
                var s=CadSplineReader.Read(spline,cancel);
                Need(s.planar,"The complete clamped native Spline must be planar.","PLANAR_GEOMETRY_REQUIRED");
                Need(!s.closed||s.control_points[0].SequenceEqual(s.control_points[^1]),
                    "Closed Spline placement retains the established repeated endpoint representation.","UNSUPPORTED_SPLINE");
                foreach(var point in s.control_points){cancel();Point(P(point));}Point(P(s.start_point));Point(P(s.end_point));break;
            case Polyline polyline:
                Need(polyline.NumberOfVertices>=2,"Polyline requires at least two native vertices.","INVALID_GEOMETRY");
                Direction(polyline.Normal);Zero(polyline.Thickness);Number(polyline.Elevation);Number(polyline.ConstantWidth);
                for(int i=0;i<polyline.NumberOfVertices;i++){cancel();Point(polyline.GetPoint3dAt(i));Number(polyline.GetBulgeAt(i));Number(polyline.GetStartWidthAt(i));Number(polyline.GetEndWidthAt(i));}break;
        }
        _=CadContourBudget344.Geometry(entity,cancel);
    }
    static State Capture(Entity entity,Transaction tr,Action cancel)
    {
        // Borrow resident wrappers; the transaction owns their lifetime.
        var core=CadContourBudget344.Source(entity,cancel);
        var owned=entity.ObjectId.IsNull?(JsonElement?)null:CadAnnotationMirrorEditor340.Metadata(entity,tr,cancel);
        CadContourBudget344.Check(new{core,owned},cancel);return new(core,owned);
    }
    internal static void CheckSource(Entity source,SketchPlaneSpec frame,Database db,Transaction tr,Action cancel)
    {
        Check(source,frame,cancel);CadIndependentCurve.Require(source,db,tr,cancel);
        Need(source.Annotative!=AnnotativeStates.True,"Native annotation contexts require their owning route.","UNSUPPORTED_CURVE_DEPENDENCIES");
        _=Capture(source,tr,cancel);
    }
    static bool Shape(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        if(a.ValueKind==JsonValueKind.Object)return a.EnumerateObject().Count()==b.EnumerateObject().Count()&&a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var other)&&Shape(p.Value,other));
        if(a.ValueKind==JsonValueKind.Array)return a.GetArrayLength()==b.GetArrayLength()&&a.EnumerateArray().Zip(b.EnumerateArray()).All(pair=>Shape(pair.First,pair.Second));
        return a.ValueKind==JsonValueKind.Number?BlockMoveGeometry.Close(a.GetDouble(),b.GetDouble()):a.GetRawText()==b.GetRawText();
    }
    static JsonElement Root(JsonElement metadata)=>Wire.Element(metadata.EnumerateObject().Where(p=>p.Name!="xdata").ToDictionary(p=>p.Name,p=>(object)p.Value.Clone(),StringComparer.Ordinal));
    static void VerifyRetained(Entity source,State before,Transaction tr,Action cancel)
    {
        var after=Capture(source,tr,cancel);
        Need(Exact(before.Core,after.Core)&&Exact(before.Owned,after.Owned),
            "Independent native preparation/copy changed an original curve's observed geometry, root, display or owned data.","NATIVE_PLANE_TRANSFORM_SOURCE_CHANGED");
    }
    internal static void Transform(Entity source,EditSpec edit,Transaction tr,Action cancel)
    {
        var frame=Frame(edit)??throw new DomainException("INVALID_ARGUMENT","Use the declared plane.");
        Check(source,frame,cancel);var before=Capture(source,tr,cancel);var matrix=Matrix(edit);
        JsonElement expectedData;
        // Native world-XData evaluation is independent of the geometry preview,
        // as in the existing reflection adapter; do not guess DXF point semantics.
        using(var dataCopy=CadContourBudget344.Detached(source))
        {
            cancel();dataCopy.XDataTransformBy(matrix);
            expectedData=CadContourBudget344.Source(dataCopy,cancel).Metadata.GetProperty("xdata").Clone();
        }
        JsonElement expectedGeometry;
        using(var expected=CadContourBudget344.Detached(source))
        {
            cancel();expected.TransformBy(matrix);Check(expected,frame,cancel);
            expectedGeometry=CadContourBudget344.Source(expected,cancel).Geometry.Clone();
        }
        // All preparation roots have been released before proving retention or
        // applying the one actual resident world transform.
        VerifyRetained(source,before,tr,cancel);
        cancel();source.TransformBy(matrix);
        var actualData=CadContourBudget344.Source(source,cancel).Metadata.GetProperty("xdata");
        // Native adapters may leave XData unchanged. Complete the separate
        // native operation once only when its exact before-state proves this;
        // never transform changed data a second time.
        if(!Exact(actualData,expectedData)&&Exact(actualData,before.Core.Metadata.GetProperty("xdata")))
            source.XDataTransformBy(matrix);
        if(!source.ObjectId.IsNull)source.RecordGraphicsModified(true);
        cancel();Check(source,frame,cancel);var after=Capture(source,tr,cancel);
        Need(Shape(expectedGeometry,after.Core.Geometry)&&Exact(before.Core.Display,after.Core.Display)&&
            Exact(Root(before.Core.Metadata),Root(after.Core.Metadata))&&
            Exact(expectedData,after.Core.Metadata.GetProperty("xdata"))&&Exact(before.Owned,after.Owned),
            "Native transform differs from its complete independent native definition or changed retained identity/display/owned data.");
    }
    internal static Entity Copy(Entity source,EditSpec edit,ObjectId owner,Transaction tr,Action cancel)
    {
        var frame=Frame(edit)??throw new DomainException("INVALID_ARGUMENT","Use the declared plane.");
        Need(edit is CopySpec or Mirror2dSpec{Copy:true},"Native plane copy uses copy or mirror copy=true.","INVALID_ARGUMENT");
        CheckSource(source,frame,source.Database,tr,cancel);var before=Capture(source,tr,cancel);
        using var ids=new ObjectIdCollection(new[]{source.ObjectId});using var mapping=new IdMapping();
        source.Database.DeepCloneObjects(ids,owner,mapping,false);cancel();
        Need(mapping.Contains(source.ObjectId),"Native DeepClone returned no requested curve root.","UNSAFE_NATIVE_CLONE");
        var root=mapping.Lookup(source.ObjectId);var id=root.Value;
        Need(root.IsCloned&&root.Key==source.ObjectId&&id!=source.ObjectId&&!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,
            "Native DeepClone must return an actual distinct live mapped root.","UNSAFE_NATIVE_CLONE");
        var copy=tr.GetObject(id,OpenMode.ForWrite) as Entity;
        Need(copy!=null&&copy.GetType()==source.GetType()&&copy.Database==source.Database&&copy.OwnerId==owner&&
            copy.UnmanagedObject!=IntPtr.Zero&&copy.UnmanagedObject!=source.UnmanagedObject,"Native copy has a different type/owner or aliases its source.","UNSAFE_NATIVE_CLONE");
        var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(IdPair pair in mapping)
        {
            cancel();if(!pair.Key.IsNull&&!pair.Value.IsNull)map[Validation.NormalizeHandle(pair.Key.Handle.ToString())]=Validation.NormalizeHandle(pair.Value.Handle.ToString());
            if(pair.IsCloned&&pair.Value!=id&&tr.GetObject(pair.Value,OpenMode.ForRead) is Entity child)
                Need(child.OwnerId!=owner,"Native copy produced an additional unreported top-level entity.");
        }
        var copied=Capture(copy!,tr,cancel);
        Need(Shape(before.Core.Geometry,copied.Core.Geometry)&&Exact(before.Core.Display,copied.Core.Display)&&
            Exact(CadAnnotationMirrorEditor340.Remap(before.Core.Metadata,map,cancel),copied.Core.Metadata)&&
            before.Owned.HasValue==copied.Owned.HasValue&&(!before.Owned.HasValue||
                Exact(CadAnnotationMirrorEditor340.Remap(before.Owned.Value,map,cancel),copied.Owned!.Value)),
            "Native curve copy lost its full mapped ordinary owned data, definition or display.");
        Transform(copy!,edit,tr,cancel);VerifyRetained(source,before,tr,cancel);
        // Native resident clone and its owned objects belong to this transaction.
        // Never append/register/dispose them as detached copies.
        return copy!;
    }
    private static bool SeparatePlacementOwner(DBObject value)=>value.GetType()==typeof(DBPoint)||value is Entity entity&&CadConstructionCurve443.Is(entity);
    internal static Saved[] CaptureSources(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,Action cancel)
    {
        var saved=new List<Saved>();var budget=new ModelInventoryBudget(cancel);
        foreach(var pair in resolved.Sources)
        {
            if(SeparatePlacementOwner(tr.GetObject(pair.Value,OpenMode.ForRead)))continue;
            var frame=Frame(args.Operations,pair.Key);if(frame==null)continue;
            var entity=(Entity)tr.GetObject(pair.Value,OpenMode.ForRead);var state=Capture(entity,tr,cancel);
            budget.Add(state);saved.Add(new(pair.Value,frame,state));
        }
        return saved.ToArray();
    }
    internal static void VerifySources(Saved[] sources,Transaction tr,Action cancel)
    {foreach(var saved in sources){cancel();VerifyRetained((Entity)tr.GetObject(saved.Id,OpenMode.ForRead),saved.Before,tr,cancel);}}
    internal static Saved[] CaptureCommitted(ModifyArguments args,ModifiedEntities result,Database db,Transaction tr,Action cancel)
    {
        var frames=new Dictionary<string,SketchPlaneSpec>(StringComparer.Ordinal);
        foreach(var handle in args.Operations.SelectMany(edit=>edit.Handles).Distinct(StringComparer.Ordinal))
        {
            if(result.erased.Any(reference=>reference.handle==handle))continue;
            if(SeparatePlacementOwner(tr.GetObject(CadStructureWriter.Find(db,handle),OpenMode.ForRead)))continue;
            if(Frame(args.Operations,handle) is {} frame)frames[handle]=frame;
        }
        foreach(var copy in result.copies)
        {
            if(SeparatePlacementOwner(tr.GetObject(CadStructureWriter.Find(db,copy.created.handle),OpenMode.ForRead)))continue;
            if(Frame(args.Operations[copy.operation_index]) is {} frame)frames[copy.created.handle]=frame;
        }
        var saved=new List<Saved>();var budget=new ModelInventoryBudget(cancel);
        foreach(var pair in frames)
        {
            cancel();var id=CadStructureWriter.Find(db,pair.Key);var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);
            Check(entity,pair.Value,cancel);var state=Capture(entity,tr,cancel);budget.Add(state);saved.Add(new(id,pair.Value,state));
        }
        return saved.ToArray();
    }
    internal static void VerifyCommitted(Saved[] sources,Database db,Action cancel)
    {
        using var tr=db.TransactionManager.StartTransaction();
        foreach(var saved in sources)
        {
            cancel();Need(!saved.Id.IsNull&&saved.Id.IsValid&&!saved.Id.IsErased&&!saved.Id.IsEffectivelyErased,"Committed curve root is unavailable.");
            var entity=(Entity)tr.GetObject(saved.Id,OpenMode.ForRead);Check(entity,saved.Frame,cancel);
            var actual=Capture(entity,tr,cancel);
            Need(Exact(saved.Before.Core,actual.Core)&&Exact(saved.Before.Owned,actual.Owned),"Committed native curve geometry/root/display/owned data changed after the observed apply.");
        }
    }
}

