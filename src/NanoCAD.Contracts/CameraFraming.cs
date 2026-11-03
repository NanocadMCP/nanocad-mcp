namespace NanoCAD.Contracts;

public sealed record CameraBounds(Point3 Min,Point3 Max);
public sealed record CameraFrame(Point3 Target,Point2 Center,double Width,double Height);

public static class CameraFraming
{
    private static double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    public static CameraFrame WithTarget(CameraFrame frame,Point3 target,Point3 viewX,Point3 viewY)
    {
        if(!new[]{target.X,target.Y,target.Z}.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1e9))
            throw new DomainException("FRAME_BOUNDS_UNAVAILABLE","Existing camera target is unavailable or excessive.");
        var delta=new Point3(frame.Target.X-target.X,frame.Target.Y-target.Y,frame.Target.Z-target.Z);
        return frame with{Target=target,Center=new(frame.Center.X+Dot(delta,viewX),frame.Center.Y+Dot(delta,viewY))};
    }
    public static CameraFrame Calculate(CameraBounds[] bounds,Point3 viewX,Point3 viewY,double aspect,double padding)
    {
        void Need(bool valid,string message){if(!valid)throw new DomainException("FRAME_BOUNDS_UNAVAILABLE",message);}
        Need(bounds.Length >= 1,"Framing requires a nonempty set of finite native bounds.");
        Need(double.IsFinite(aspect)&&aspect>=.01&&aspect<=100&&double.IsFinite(padding)&&padding>=0&&padding<=1,"Invalid viewport aspect or framing padding.");
        Need(new[]{viewX.X,viewX.Y,viewX.Z,viewY.X,viewY.Y,viewY.Z}.All(double.IsFinite)&&
            Math.Abs(Dot(viewX,viewX)-1)<1e-10&&Math.Abs(Dot(viewY,viewY)-1)<1e-10&&Math.Abs(Dot(viewX,viewY))<1e-10,"Native view basis is not orthonormal.");
        foreach(var box in bounds)Need(new[]{box.Min.X,box.Min.Y,box.Min.Z,box.Max.X,box.Max.Y,box.Max.Z}.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1e9)&&
            box.Min.X<=box.Max.X&&box.Min.Y<=box.Max.Y&&box.Min.Z<=box.Max.Z,"Entity bounds are unavailable or inverted.");
        var target=new Point3((bounds.Min(b=>b.Min.X)+bounds.Max(b=>b.Max.X))*.5,
            (bounds.Min(b=>b.Min.Y)+bounds.Max(b=>b.Max.Y))*.5,(bounds.Min(b=>b.Min.Z)+bounds.Max(b=>b.Max.Z))*.5);
        double minX=double.PositiveInfinity,minY=double.PositiveInfinity,maxX=double.NegativeInfinity,maxY=double.NegativeInfinity;
        foreach(var box in bounds)for(int corner=0;corner<8;corner++)
        {
            var point=new Point3(((corner&1)==0?box.Min.X:box.Max.X)-target.X,((corner&2)==0?box.Min.Y:box.Max.Y)-target.Y,((corner&4)==0?box.Min.Z:box.Max.Z)-target.Z);
            double x=Dot(point,viewX),y=Dot(point,viewY);minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
        }
        // A line seen edge-on or a point still gets a finite, nonzero view field.
        double height=Math.Max(.01,Math.Max(maxY-minY,(maxX-minX)/aspect))*(1+2*padding);
        double width=height*aspect;Need(double.IsFinite(width)&&double.IsFinite(height)&&width<=1e12&&height<=1e12,"Framing view exceeds finite size bounds.");
        return new(target,new((minX+maxX)*.5,(minY+maxY)*.5),width,height);
    }
}
