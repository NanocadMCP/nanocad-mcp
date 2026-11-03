namespace NanoCAD.Contracts;

public static class PointContainment
{
    // Conservative rejection against native extents only. The margin excludes
    // neither near-boundary queries nor any point inside the enclosing box.
    public static bool OutsideNativeBounds(double[] point,double[] min,double[] max)
    {
        if(point.Length!=3||min.Length!=3||max.Length!=3||point.Concat(min).Concat(max).Any(v=>!double.IsFinite(v))||
            Enumerable.Range(0,3).Any(i=>min[i]>max[i]))
            throw new DomainException("INVALID_GEOMETRY","Containment requires finite ordered native extents.");
        double magnitude=min.Concat(max).Select(Math.Abs).Max();
        double margin=Math.Max(1e-6,magnitude*1e-12);
        return Enumerable.Range(0,3).Any(i=>point[i]<min[i]-margin||point[i]>max[i]+margin);
    }
    public static string Classify(string nativeClassification,bool containerIsNull) => (nativeClassification,containerIsNull) switch
    {
        ("Inside",false)=>"inside",
        ("Outside",true)=>"outside",
        ("OnBoundary",false)=>"boundary",
        _=>throw new DomainException("NATIVE_CONTAINMENT_MISMATCH","Native face classification or containing topology is inconsistent.",false,
            new { native_classification=nativeClassification, container_is_null=containerIsNull })
    };
}
