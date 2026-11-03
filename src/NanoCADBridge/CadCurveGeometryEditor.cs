using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadCurveGeometryEditor
{
    internal static void Apply(Entity entity, CurveGeometrySpec edit, Action cancel,bool localPlane=false)
    {
        if(edit.Workplane!=null){CadExplicitCurveGeometry388.Apply(entity,edit,cancel);return;}
        void Check(){if(localPlane)CadPlaneCurveGeometry393.CheckLocal(entity,cancel);else CadModifier.CheckGeometry(entity,cancel);}
        Check();
        Require(entity, edit.ExpectedGeometry, "GEOMETRY_CHANGED", cancel,localPlane);
        Point3d Point(Point2 p) => new(p.X, p.Y, 0);
        if (entity is Line line) { line.StartPoint = Point(edit.Geometry.Start!); line.EndPoint = Point(edit.Geometry.End!); }
        else if (entity is Circle circle) { circle.Center = Point(edit.Geometry.Center!); circle.Radius = edit.Geometry.Radius; }
        else if (entity is Arc arc) { arc.Center=Point(edit.Geometry.Center!);arc.Radius=edit.Geometry.Radius;arc.StartAngle=edit.Geometry.StartAngle;arc.EndAngle=ContourGeometry.Angle(edit.Geometry.StartAngle+edit.Geometry.SweepAngle); }
        else if (entity is Ellipse ellipse)
        {
            var spec = edit.Geometry.Ellipse!;
            // Set's angle arguments differ from ellipse parameters. Reset the
            // full ellipse first, then use the verified parameter accessors.
            ellipse.Set(Point(spec.Center), Vector3d.ZAxis, new(spec.MajorAxis.X,spec.MajorAxis.Y,0), spec.RadiusRatio, 0, 2*Math.PI);
            if (spec.SweepParameter != 2*Math.PI)
            {
                CadEllipseCreator.SetParameter(ellipse,"set_StartParam",spec.StartParameter);
                double end=spec.StartParameter+spec.SweepParameter;
                CadEllipseCreator.SetParameter(ellipse,"set_EndParam",end>2*Math.PI?end-2*Math.PI:end);
            }
        }
        else if (entity is Spline spline)
        {
            // The public NurbsData setter replaces the spline definition in the
            // existing DB entity. Hold its getter-owned native points until the
            // setter has copied them; display properties are never copied.
            using var template = CadSplineCreator.Create(edit.Geometry.Spline!);
            var data = template.NurbsData;
            using var points = data.GetControlPoints();
            cancel(); spline.NurbsData = data;
        }
        else throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Geometry replacement requires native Line, Circle, Arc, Ellipse or Spline.");
        Check();
        Require(entity, edit.Geometry, "NATIVE_GEOMETRY_MISMATCH", cancel,localPlane);
    }
    internal static void Require(Entity entity, CurveEditGeometry geometry, string error, Action cancel,bool localPlane=false)
    {
        if (entity.GetType()==typeof(Ellipse) && geometry.Type=="Ellipse" || entity.GetType()==typeof(Spline) && geometry.Type=="Spline")
        {
            try
            {
                if(entity is Ellipse e)EllipseCreation.RequireMatch(geometry.Ellipse!,CadEllipseReader.Read(e,cancel));
                else SplineCreation.RequireMatch(geometry.Spline!,CadSplineReader.Read((Spline)entity,cancel),localPlane?EllipseVerification.Tolerance:0);
                return;
            }
            catch(DomainException ex) when(ex.Error.code is "NATIVE_ELLIPSE_MISMATCH" or "NATIVE_SPLINE_MISMATCH")
            { throw new DomainException(error,"Stored curve definition differs from requested axes/parameters or complete NURBS geometry."); }
        }
        bool Point(Point3d point, Point2 expected) => point.DistanceTo(new Point3d(expected.X, expected.Y, 0)) <= 1e-7;
        bool matches = entity.GetType() == typeof(Line) && geometry.Type == "Line" && entity is Line line &&
            Point(line.StartPoint, geometry.Start!) && Point(line.EndPoint, geometry.End!) ||
            entity.GetType() == typeof(Circle) && geometry.Type == "Circle" && entity is Circle circle &&
            Point(circle.Center, geometry.Center!) && Math.Abs(circle.Radius - geometry.Radius) <= 1e-7 ||
            entity.GetType()==typeof(Arc) && geometry.Type=="Arc" && entity is Arc arc && Point(arc.Center,geometry.Center!) && Math.Abs(arc.Radius-geometry.Radius)<=1e-7 &&
            Math.Abs(ContourGeometry.Angle(arc.EndAngle-arc.StartAngle)-geometry.SweepAngle)<=1e-7 &&
            Point(arc.StartPoint,new(geometry.Center!.X+geometry.Radius*Math.Cos(geometry.StartAngle),geometry.Center.Y+geometry.Radius*Math.Sin(geometry.StartAngle))) &&
            Point(arc.EndPoint,new(geometry.Center.X+geometry.Radius*Math.Cos(geometry.StartAngle+geometry.SweepAngle),geometry.Center.Y+geometry.Radius*Math.Sin(geometry.StartAngle+geometry.SweepAngle)));
        if (!matches) throw new DomainException(error, "Curve differs from the requested exact type and geometry.");
    }
}
