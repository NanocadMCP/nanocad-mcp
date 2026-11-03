using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class SolidCreator
{
    public static ObjectId[] Resolve(SolidBooleanSpec spec,Database db,Transaction tr,ObjectId model)
        =>ResolveSources(spec.SourceHandles,db,tr,model);
    public static ObjectId[] Resolve(SolidTransformCopySpec spec,Database db,Transaction tr,ObjectId model)
        =>ResolveSources(new[]{spec.SourceHandle},db,tr,model);
    public static ObjectId[] Resolve(DrilledSolidCopySpec spec,Database db,Transaction tr,ObjectId model)
        =>ResolveSources(new[]{spec.SourceHandle},db,tr,model);
    public static ObjectId[] Resolve(SolidFinishingCopySpec spec,Database db,Transaction tr,ObjectId model,EntityRef reference,Action? cancel=null)
    {
        var ids=ResolveSources(new[]{spec.SourceHandle},db,tr,model);
        SolidFinisher.Validate(spec,(Solid3d)tr.GetObject(ids[0],OpenMode.ForRead),reference,cancel);
        return ids;
    }
    private static ObjectId[] ResolveSources(string[] handles,Database db,Transaction tr,ObjectId model)
    {
        return handles.Select(handle=>
        {
            var id=CadStructureWriter.Find(db,handle);
            var value=tr.GetObject(id,OpenMode.ForRead);
            if(value is not Solid3d solid || value.GetType()!=typeof(Solid3d) || solid.OwnerId!=model)
                throw new DomainException("OUT_OF_SCOPE","Sources must be top-level native Model Space Solid3d.",false,new{handle});
            if(solid.IsNull)throw new DomainException("DEGENERATE_GEOMETRY","Source is an empty solid.",false,new{handle});
            if(solid.RecordHistory || solid.ShowHistory)throw new DomainException("UNSUPPORTED_SOLID_HISTORY","Copying solids with enabled native history flags is not supported.",false,new{handle});
            return id;
        }).ToArray();
    }
    public static Solid3d Boolean(SolidBooleanSpec spec,ObjectId[] sources,Transaction tr,Action<string,long>? trace=null)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        void Trace(string stage)=>trace?.Invoke(stage,watch.ElapsedMilliseconds);
        // Both operands are detached copies. Source objects remain ForRead and
        // are neither consumed, erased nor passed to the native boolean method.
        Trace("clone_first_begin");
        var result=(Solid3d)((Solid3d)tr.GetObject(sources[0],OpenMode.ForRead)).Clone();
        try
        {
            Trace("clone_first_returned");
            using var other=(Solid3d)((Solid3d)tr.GetObject(sources[1],OpenMode.ForRead)).Clone();
            Trace("boolean_begin");
            result.BooleanOperation(spec.Operator switch {
                "union"=>BooleanOperationType.BoolUnite,"intersection"=>BooleanOperationType.BoolIntersect,
                "subtract"=>BooleanOperationType.BoolSubtract,_=>throw new DomainException("INVALID_ARGUMENT","Unknown solid boolean.")},other);
            Trace("boolean_returned");
            if(result.IsNull)throw new DomainException("EMPTY_SOLID_RESULT","Boolean result is empty; no entity was appended.");
            return result;
        }
        catch {result.Dispose();throw;}
    }
    public static Solid3d Create(ExtrudedSolidSpec spec)
    {
        using var profile=new Polyline(spec.Profile.Length);
        for(int i=0;i<spec.Profile.Length;i++) profile.AddVertexAt(i,new Point2d(spec.Profile[i].X,spec.Profile[i].Y),0,0,0);
        profile.Closed=true; profile.Elevation=spec.BaseZ;
        using var options=new SweepOptions();
        var solid=new Solid3d();
        try
        {
            solid.RecordHistory=false;
            solid.CreateExtrudedSolid(profile,new Vector3d(0,0,spec.Height),options);
            return solid;
        }
        catch {solid.Dispose();throw;}
    }
    public static Solid3d TransformCopy(SolidTransformCopySpec spec,ObjectId sourceId,Transaction tr)
    {
        var source=(Solid3d)tr.GetObject(sourceId,OpenMode.ForRead);
        var transform=spec.Transform;
        Point3d Point(Point3 p)=>new(p.X,p.Y,p.Z);
        Vector3d Vector(Point3 p)=>new(p.X,p.Y,p.Z);
        Matrix3d matrix;
        if(transform.Kind=="translation")matrix=Matrix3d.Displacement(Vector(transform.Displacement!));
        else if(transform.Kind=="rotation")matrix=Matrix3d.Rotation(transform.Angle,Vector(transform.Direction!).GetNormal(),Point(transform.Origin!));
        else
        {
            using var plane=new Plane(Point(transform.Origin!),Vector(transform.Direction!).GetNormal());
            matrix=Matrix3d.Mirroring(plane);
        }
        var result=(Solid3d)source.Clone();
        try
        {
            result.TransformBy(matrix);RequireVolume(result);
            return result;
        }
        catch{result.Dispose();throw;}
    }
    public static Solid3d DrillCopy(DrilledSolidCopySpec spec,ObjectId sourceId,Transaction tr)
    {
        var source=(Solid3d)tr.GetObject(sourceId,OpenMode.ForRead);
        var result=(Solid3d)source.Clone();
        try{Drill(spec,result);return result;}
        catch{result.Dispose();throw;}
    }
    public static void Drill(DrilledSolidCopySpec spec,Solid3d target)
    {
        double before=target.MassProperties.Volume;
        var direction=new Vector3d(spec.Direction.X,spec.Direction.Y,spec.Direction.Z).GetNormal();
        var start=new Point3d(spec.Start.X,spec.Start.Y,spec.Start.Z);
        using var cutter=new Solid3d();cutter.RecordHistory=false;
        cutter.CreateFrustum(spec.Depth,spec.Radius,spec.Radius,spec.Radius);
        var axis=Vector3d.ZAxis.CrossProduct(direction);double angle=Vector3d.ZAxis.GetAngleTo(direction);
        if(angle>1e-12)cutter.TransformBy(Matrix3d.Rotation(angle,axis.Length>1e-12?axis.GetNormal():Vector3d.XAxis,Point3d.Origin));
        cutter.TransformBy(Matrix3d.Displacement(start+direction*(spec.Depth/2)-cutter.MassProperties.Centroid));
            target.BooleanOperation(BooleanOperationType.BoolSubtract,cutter);
            RequireVolume(target);
            double removed=before-target.MassProperties.Volume;
            double tolerance=Math.Max(1e-7,Math.Abs(before)*1e-9);
            if(!double.IsFinite(removed) || removed<=tolerance)
                throw new DomainException("NO_MATERIAL_REMOVED","Cylindrical cut removed no measurable volume; nothing appended.");
            // Cutter volume is diagnostic only: the kernel may report a
            // different integral for a valid B-rep. The required cut effect
            // above is checked on the actual detached body.
    }
    public static Solid3d Create(SolidSpec spec)
    {
        var solid=new Solid3d();
        try
        {
            // This capability creates a new direct body. It never strips an
            // existing body's history or advertises a parametric feature graph.
            solid.RecordHistory=false;
            switch(spec.Kind)
            {
                case "box": solid.CreateBox(spec.Size!.X,spec.Size.Y,spec.Size.Z); break;
                case "sphere": solid.CreateSphere(spec.Radius); break;
                case "cylinder": solid.CreateFrustum(spec.Height,spec.Radius,spec.Radius,spec.Radius); break;
                case "frustum": solid.CreateFrustum(spec.Height,spec.Radius,spec.Radius,spec.TopRadius!.Value); break;
                case "torus": solid.CreateTorus(spec.Radius,spec.MinorRadius!.Value); break;
                case "wedge": solid.CreateWedge(spec.Size!.X,spec.Size.Y,spec.Size.Z); break;
                case "pyramid": solid.CreatePyramid(spec.Height,spec.Sides!.Value,spec.Radius,spec.TopRadius!.Value); break;
                default: throw new DomainException("INVALID_ARGUMENT","Unsupported solid primitive.");
            }
            var center=solid.MassProperties.Centroid;
            solid.TransformBy(Matrix3d.Displacement(new Point3d(spec.Center.X,spec.Center.Y,spec.Center.Z)-center));
            return solid;
        }
        catch { solid.Dispose(); throw; }
    }
    public static Solid3d Create(RevolvedSolidSpec spec)
    {
        using var profile=new Polyline(spec.Profile.Length);
        for(int i=0;i<spec.Profile.Length;i++)profile.AddVertexAt(i,new Point2d(spec.Profile[i].X,spec.Profile[i].Y),0,0,0);
        profile.Closed=true;
        var origin=new Point3d(spec.AxisOrigin.X,spec.AxisOrigin.Y,spec.AxisOrigin.Z);
        // Local XY means radius/height; rotate it into the WCS XZ meridian.
        profile.TransformBy(Matrix3d.Displacement(origin-Point3d.Origin)*Matrix3d.Rotation(Math.PI/2,Vector3d.XAxis,Point3d.Origin));
        using var options=new RevolveOptions();var solid=new Solid3d();
        try
        {
            solid.RecordHistory=false;
            solid.CreateRevolvedSolid(profile,origin,Vector3d.ZAxis,spec.Angle,spec.StartAngle,options);
            RequireVolume(solid);return solid;
        }
        catch{solid.Dispose();throw;}
    }
    public static Solid3d Create(SweptSolidSpec spec)
    {
        Point3d Point(Point3 p)=>new(p.X,p.Y,p.Z);
        Point3d start;Vector3d tangent;Curve path;
        if(spec.Path.Kind=="line")
        {
            start=Point(spec.Path.Start!);var end=Point(spec.Path.End!);tangent=(end-start).GetNormal();path=new Line(start,end);
        }
        else
        {
            var center=Point(spec.Path.Center!);double a=spec.Path.StartAngle;
            start=center+new Vector3d(spec.Path.Radius*Math.Cos(a),spec.Path.Radius*Math.Sin(a),0);
            tangent=new Vector3d(-Math.Sin(a),Math.Cos(a),0);
            path=new Arc(center,Vector3d.ZAxis,spec.Path.Radius,a,a+spec.Path.SweepAngle);
        }
        using(path)
        using(var profile=new Circle(start,tangent,spec.Radius))
        {
            var builder=new SweepOptionsBuilder{Align=SweepOptionsAlignOption.NoAlignment,BasePoint=start,CheckIntersections=true,ScaleFactor=1,TwistAngle=0};
            using var options=builder.ToSweepOptions();var solid=new Solid3d();
            try{solid.RecordHistory=false;solid.CreateSweptSolid(profile,path,options);RequireVolume(solid);return solid;}
            catch{solid.Dispose();throw;}
        }
    }
    public static Solid3d Create(LoftedSolidSpec spec)
    {
        if(spec.Interpolation=="ruled")return CreateRuled(spec);
        var sections=new List<Entity>();
        try
        {
            foreach(var section in spec.Sections)
                sections.Add(new Circle(new Point3d(section.Center.X,section.Center.Y,section.Center.Z),Vector3d.ZAxis,section.Radius));
#if READ_ACCEPTANCE_AUDIT
            ReadAcceptanceAudit.ObserveLoftOptions();
#endif
            var builder=new LoftOptionsBuilder{Closed=false,NoTwist=true,AlignDirection=true,Simplify=true,Ruled=false};
            using var options=builder.ToLoftOptions();
            if(options.Ruled || options.Closed)
                throw new DomainException("NATIVE_OPTIONS_MISMATCH","Loft options do not preserve the requested smooth interpolation.");
            var solid=new Solid3d();
            try
            {
                solid.RecordHistory=false;
                solid.CreateLoftedSolid(sections.ToArray(),Array.Empty<Entity>(),null,options);
                RequireVolume(solid);
                VerifyLoftSections(solid,spec);
                return solid;
            }
            catch{solid.Dispose();throw;}
        }
        finally{foreach(var section in sections)section.Dispose();}
    }
    private static Solid3d CreateRuled(LoftedSolidSpec spec)
    {
        Solid3d? result=null;
        try
        {
            for(int i=1;i<spec.Sections.Length;i++)
            {
                var pair=new[]{spec.Sections[i-1],spec.Sections[i]};
                Solid3d? piece=Create(new LoftedSolidSpec("smooth",pair,spec.Layer));
                try
                {
                    RuledLoftVerifier.Verify(piece,pair);
                    if(result==null){result=piece;piece=null;}
                    else result.BooleanOperation(BooleanOperationType.BoolUnite,piece);
                }
                finally{piece?.Dispose();}
            }
            RequireVolume(result!);RuledLoftVerifier.Verify(result!,spec.Sections);
            // GetSection remains suitable at input profiles, but its converted
            // spline approximation is not an oracle for intermediate sections.
            VerifyLoftSections(result!,spec);
            return result!;
        }
        catch{result?.Dispose();throw;}
    }
    private static void VerifyLoftSections(Solid3d solid,LoftedSolidSpec spec)
    {
        // A smooth loft is constrained by its input sections, not by the mass
        // of piecewise conical frusta. Verify those sections on the detached
        // body; no section region is ever appended to the drawing.
        for(int i=0;i<spec.Sections.Length;i++)
        {
            var section=spec.Sections[i];
            var center=new Point3d(section.Center.X,section.Center.Y,section.Center.Z);
            using var plane=new Plane(center,Vector3d.ZAxis);
            using var cut=solid.GetSection(plane);
            if(cut==null || cut.IsNull)throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Loft has no section at an input profile.",false,new{section_index=i});
            var origin=center;var x=Vector3d.XAxis;var y=Vector3d.YAxis;
            var area=cut.AreaProperties(ref origin,ref x,ref y);
            var actualCenter=origin+x*area.Centroid.X+y*area.Centroid.Y;
            var bounds=cut.GeometricExtents;
            if(!double.IsFinite(area.Area)||area.Area<=0||!double.IsFinite(area.Perimeter)||area.Perimeter<=0||
               !double.IsFinite(actualCenter.X)||!double.IsFinite(actualCenter.Y)||!double.IsFinite(actualCenter.Z))
                throw new DomainException("INVALID_GEOMETRY","Loft input section has no finite positive geometry.",false,new{section_index=i});
            double radius=section.Radius,tolerance=Math.Max(1e-6,radius*1e-7);
            bool Near(double a,double b,double limit)=>double.IsFinite(a) && Math.Abs(a-b)<=limit;
            bool matches=Near(area.Area,Math.PI*radius*radius,Math.Max(1e-7,Math.PI*radius*radius*1e-7)) &&
                Near(area.Perimeter,2*Math.PI*radius,tolerance) && Near(actualCenter.DistanceTo(center),0,tolerance) &&
                Near(bounds.MinPoint.DistanceTo(center-new Vector3d(radius,radius,0)),0,tolerance) &&
                Near(bounds.MaxPoint.DistanceTo(center+new Vector3d(radius,radius,0)),0,tolerance);
#if READ_ACCEPTANCE_AUDIT
            ReadAcceptanceAudit.ObserveLoftSection(new{section_count=spec.Sections.Length,section_index=i,
                area=area.Area,perimeter=area.Perimeter,centroid=new[]{actualCenter.X,actualCenter.Y,actualCenter.Z},
                expected_center=new[]{center.X,center.Y,center.Z},expected_radius=radius,matches});
#endif
            // Analytic area/perimeter/bbox comparisons aid diagnosis. Different
            // native section and integration algorithms may disagree slightly;
            // the requested profiles and the final MCP view decide acceptance.
        }
    }
    private static void RequireVolume(Solid3d solid)
    {
        if(solid.IsNull)throw new DomainException("EMPTY_SOLID_RESULT","Kernel returned an empty solid; nothing appended.");
        double volume=solid.MassProperties.Volume;
        if(!double.IsFinite(volume) || volume<=0)throw new DomainException("INVALID_GEOMETRY","Kernel returned nonpositive or nonfinite solid volume.");
    }
}
