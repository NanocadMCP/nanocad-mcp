namespace NanoCAD.Contracts;

// Analytic mass is diagnostic. Different native loft algorithms need not
// interpolate their intermediate surfaces in the same way.
public static class RuledLoftGeometry
{
    public static (double Volume,Point3 Centroid) Mass(CircularLoftSection[] sections)
    {
        double volume=0,x=0,y=0,z=0;var anchor=sections[0].Center;
        for(int i=1;i<sections.Length;i++)
        {
            var a=sections[i-1];var b=sections[i];double r=a.Radius,s=b.Radius,d=r*r+r*s+s*s;
            double v=Math.PI*(b.Center.Z-a.Center.Z)*d/3,t=(r*r+2*r*s+3*s*s)/(4*d);
            volume+=v;x+=v*(a.Center.X-anchor.X+(b.Center.X-a.Center.X)*t);
            y+=v*(a.Center.Y-anchor.Y+(b.Center.Y-a.Center.Y)*t);z+=v*(a.Center.Z-anchor.Z+(b.Center.Z-a.Center.Z)*t);
        }
        if(!double.IsFinite(volume)||volume<=0)throw new DomainException("INVALID_GEOMETRY","Invalid ruled loft mass bounds.");
        return (volume,new Point3(anchor.X+x/volume,anchor.Y+y/volume,anchor.Z+z/volume));
    }
    public static void VerifySurface(CircularLoftSection[] sections,IReadOnlyList<Point3[]> faces)
    {
        if(faces.Count<1)
            throw new DomainException("ANALYSIS_LIMIT_EXCEEDED","Native loft surface inventory is empty.");
        SerializedCapacity357.Check(faces);
        foreach(var points in faces)
        {
            if(points.Length<25||points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z)))
                throw new DomainException("INVALID_GEOMETRY","Native loft has nonfinite sampled surface geometry.");
        }
    }
}
