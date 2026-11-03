namespace NanoCAD.Contracts;

// Check the actual solved finite geometry in the observed sketch frame. The
// native solver establishes the relation; this does not create mirrored copies.
public static class SketchSymmetryGeometry414
{
    public static void Types(SketchConstraintCurve first,SketchConstraintCurve second,SketchConstraintCurve axis)
    {
        bool Point(Point2? value)=>value!=null&&double.IsFinite(value.X)&&double.IsFinite(value.Y);
        bool Line(SketchConstraintCurve value)=>value.Kind=="line"&&Point(value.Start)&&Point(value.End)&&DrawingProfileGeometry.Distance(value.Start!,value.End!)>1e-7;
        bool Circle(SketchConstraintCurve value)=>value.Kind=="circle"&&Point(value.Center)&&double.IsFinite(value.Radius)&&value.Radius>0;
        if(!Line(axis)||!(Line(first)&&Line(second)||Circle(first)&&Circle(second)))
            throw new DomainException("OUT_OF_SCOPE","Symmetry needs two actual Lines or two actual Circles and a separate nondegenerate Line axis in the same sketch.");
    }
    public static void Validate(SketchConstraintCurve first,SketchConstraintCurve second,SketchConstraintCurve axis)
    {
        Types(first,second,axis);
        var origin=axis.Start!;double dx=axis.End!.X-origin.X,dy=axis.End.Y-origin.Y,length=Math.Sqrt(dx*dx+dy*dy);dx/=length;dy/=length;
        Point2 Reflect(Point2 point)
        {
            double x=point.X-origin.X,y=point.Y-origin.Y,along=x*dx+y*dy;
            return new(origin.X+2*along*dx-x,origin.Y+2*along*dy-y);
        }
        bool valid=first.Kind=="circle"
            ?first.Center!=null&&second.Center!=null&&DrawingProfileGeometry.Same(Reflect(first.Center),second.Center)&&DrawingProfileGeometry.Near(first.Radius,second.Radius)
            :first.Start!=null&&first.End!=null&&second.Start!=null&&second.End!=null&&
                (DrawingProfileGeometry.Same(Reflect(first.Start),second.Start)&&DrawingProfileGeometry.Same(Reflect(first.End),second.End)||
                 DrawingProfileGeometry.Same(Reflect(first.Start),second.End)&&DrawingProfileGeometry.Same(Reflect(first.End),second.Start));
        if(!valid)throw new DomainException("CONSTRAINT_SOLVE_FAILED","Actual sketch geometry is not symmetric about the requested native Line axis.");
    }
}
