using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace NanoCADBridge;

internal static class CadEllipseCreator
{
    private static MethodInfo ParameterSetter(string name)
    {
        // C++/CLI publishes a getter override plus an added setter; C# resolves
        // assignment to read-only Curve.StartParam/EndParam. Invoke only these
        // exact public Ellipse accessors verified in the installed SDK metadata.
        var method=typeof(Ellipse).GetMethod(name,BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,new[]{typeof(double)},null);
        if(method==null||method.ReturnType!=typeof(void))throw new DomainException("UNSUPPORTED_API","Native Ellipse parameter setter is unavailable.");
        return method;
    }
    internal static void SetParameter(Ellipse ellipse,string name,double value)
    {
        try { ParameterSetter(name).Invoke(ellipse,new object[]{value}); }
        catch(TargetInvocationException error) when(error.InnerException!=null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw; }
    }
    internal static Ellipse Create(EllipseSpec spec)
    {
        var ellipse=new Ellipse(new Point3d(spec.Center.X,spec.Center.Y,0),Vector3d.ZAxis,
            new Vector3d(spec.MajorAxis.X,spec.MajorAxis.Y,0),spec.RadiusRatio,0,2*Math.PI);
        try
        {
            // Ellipse's own setters map directly to OdDbEllipse.setStartParam/
            // setEndParam. Constructor/Set angle arguments are a different API.
            if(spec.SweepParameter!=2*Math.PI)
            {
                SetParameter(ellipse,"set_StartParam",spec.StartParameter);
                double end=spec.StartParameter+spec.SweepParameter;
                SetParameter(ellipse,"set_EndParam",end>2*Math.PI?end-2*Math.PI:end);
            }
            Verify(ellipse,spec,()=>{});return ellipse;
        }
        catch { ellipse.Dispose();throw; }
    }
    internal static void Verify(Ellipse ellipse,EllipseSpec spec,Action check) => EllipseCreation.RequireMatch(spec,CadEllipseReader.Read(ellipse,check));
}
