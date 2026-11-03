using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadAngularOrdinateCreator
{
    private static Point3d P(Point2 p)=>new(p.X,p.Y,0);
    internal static Dimension Create(EntitySpec spec,ObjectId style)
    {
        if(spec is AngularDimensionSpec a)return a.Kind=="angular_3point"
            ?new Point3AngularDimension(P(a.Center),P(a.FirstEnd),P(a.SecondEnd),P(a.ArcPoint),a.TextOverride,style)
            :new LineAngularDimension2(P(a.FirstStart),P(a.FirstEnd),P(a.SecondStart),P(a.SecondEnd),P(a.ArcPoint),a.TextOverride,style);
        var o=(OrdinateDimensionSpec)spec;
        var result=new OrdinateDimension(o.UsingXAxis,P(o.DefiningPoint),P(o.LeaderEndPoint),o.TextOverride,style);
        try
        {
            result.Origin=P(o.Origin);
            // Set defining geometry after origin, so any native origin adjustment
            // cannot silently relocate the caller's measured point or leader.
            result.DefiningPoint=P(o.DefiningPoint);result.LeaderEndPoint=P(o.LeaderEndPoint);
            return result;
        }
        catch{result.Dispose();throw;}
    }
    private static void Number(string name,double actual,double expected,double tolerance=1e-7)
    {
        if(!double.IsFinite(actual)||Math.Abs(actual-expected)>tolerance)
            throw new DomainException("NATIVE_DIMENSION_MISMATCH","Native dimension changed requested geometry or measurement.",false,new{property=name,expected,actual=double.IsFinite(actual)?actual:(double?)null});
    }
    private static void Point(string name,Point3d actual,Point2 expected)
    {Number(name+".x",actual.X,expected.X);Number(name+".y",actual.Y,expected.Y);Number(name+".z",actual.Z,0);}
    internal static void Verify(Dimension d,EntitySpec spec,ObjectId style)
    {
        var content=(IDimensionCreation)spec;
        if(d.DimensionStyle!=style||d.DimensionText!=content.TextOverride||d.Normal!=Vector3d.ZAxis||d.Elevation!=0||d.HasFields||d.GetPersistentReactorIds().Count!=0)
            throw new DomainException("NATIVE_DIMENSION_MISMATCH","Native dimension did not retain plain text, explicit style or WCS plane, or gained unsupported dependencies.");
        if(spec is AngularDimensionSpec a)
        {
            Point3d arc;
            if(d is Point3AngularDimension p)
            {Point("center",p.CenterPoint,a.Center);Point("xline1",p.XLine1Point,a.FirstEnd);Point("xline2",p.XLine2Point,a.SecondEnd);arc=p.ArcPoint;}
            else
            {var l=(LineAngularDimension2)d;Point("xline1_start",l.XLine1Start,a.FirstStart);Point("xline1_end",l.XLine1End,a.FirstEnd);Point("xline2_start",l.XLine2Start,a.SecondStart);Point("xline2_end",l.XLine2End,a.SecondEnd);arc=l.ArcPoint;}
            Number("arc_point.z",arc.Z,0);
            var arc2=new Point2(arc.X,arc.Y);Number("arc_radius",AngularDimensionGeometry.Length(a.Center,arc2),AngularDimensionGeometry.Length(a.Center,a.ArcPoint));
            if(!AngularDimensionGeometry.InSector(a.Center,AngularDimensionGeometry.Difference(a.FirstEnd,a.FirstStart),arc2,a.Angle))throw new DomainException("NATIVE_DIMENSION_MISMATCH","Native arc point left the requested angular sector.");
            Number("angle_radians",d.Measurement,a.Angle,1e-8);
        }
        else
        {
            var o=(OrdinateDimensionSpec)spec;var n=(OrdinateDimension)d;
            Point("origin",n.Origin,o.Origin);Point("defining_point",n.DefiningPoint,o.DefiningPoint);Point("leader_end_point",n.LeaderEndPoint,o.LeaderEndPoint);
            if(n.UsingXAxis!=o.UsingXAxis)throw new DomainException("NATIVE_DIMENSION_MISMATCH","Native ordinate axis changed.");
            Number("coordinate_measurement",n.Measurement,Math.Abs(o.UsingXAxis?o.DefiningPoint.X-o.Origin.X:o.DefiningPoint.Y-o.Origin.Y));
        }
    }
}
