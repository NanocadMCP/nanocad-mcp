using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;

namespace NanoCADBridge;

// Selected profile boundaries stay native curves. Neither point containment nor
// area measurement substitutes a tessellated polygon for the SDK's own region.
internal static class ModelProfileCurvedGeometry445
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string code,string message)
    {if(!value)throw new DomainException(code,message);}
    internal static JsonElement Loop(G.Polyline3d loop,JsonElement frame,Action cancel)
    {
        cancel();Need(loop!=null&&loop.IsClosed(),"PROFILE_GEOMETRY_UNAVAILABLE","An actual selected native loop is unavailable or open.");
        using var segments=loop.Segments;Need(segments.Count>0,"INVALID_GEOMETRY","A selected native loop is empty.");
        var items=new List<object>();var bytes=new ModelInventoryBudget(cancel);
        for(uint index=0;index<segments.Count;index++)
        {
            cancel();object geometry;var kind=segments.SegType(index);
            if(kind==G.SegPlType.sgLine){using var line=segments.GetLineSegAt(index);geometry=SketchGeometryReader.Line(line);}
            else if(kind==G.SegPlType.sgArc){using var arc=segments.GetArcSegAt(index);geometry=SketchGeometryReader.Arc(arc);}
            else if(kind==G.SegPlType.sgEllipse){using var ellipse=segments.GetEllipsSegAt(index);geometry=NativeSketchCurveGeometry445.Ellipse(ellipse,cancel);}
            else if(kind==G.SegPlType.sgNurb){using var spline=segments.GetNurbSegAt(index);geometry=NativeSketchCurveGeometry445.Spline(spline,cancel);}
            else throw new DomainException("PROFILE_GEOMETRY_UNAVAILABLE","The selected native profile contains an unreadable or degenerate boundary segment.");
            var item=new{element_id=index.ToString(System.Globalization.CultureInfo.InvariantCulture),geometry};bytes.Add(item);items.Add(item);
        }
        return Wire.Element(new{frame,geometry_details_complete=true,elements=items,identity_scope="boundary_order_only_not_native_element_identity"});
    }
    internal static bool HasCurves(JsonElement geometry)
    {
        if(geometry.ValueKind==JsonValueKind.Object)
        {
            if(geometry.TryGetProperty("kind",out var kind)&&kind.ValueKind==JsonValueKind.String&&kind.GetString() is "spline" or "elliptical_arc")return true;
            foreach(var field in geometry.EnumerateObject())if(HasCurves(field.Value))return true;
        }
        else if(geometry.ValueKind==JsonValueKind.Array)foreach(var item in geometry.EnumerateArray())if(HasCurves(item))return true;
        return false;
    }
    internal static double Area(SketchProfile profile,Action cancel)
    {
        cancel();var areas=profile.GetContours(false);
        Need(areas!=null&&areas.Count>0,"PROFILE_GEOMETRY_UNAVAILABLE","The native profile has no readable selected closed area.");
        try
        {
            double total=0;
            foreach(var area in areas)
            {
                cancel();Need(area!=null,"PROFILE_GEOMETRY_UNAVAILABLE","A selected native area is unavailable.");
                double value=area.GetArea();Need(double.IsFinite(value)&&value>0,"INVALID_GEOMETRY","A selected native area has no positive finite SDK area measurement.");
                total+=value;Need(double.IsFinite(total),"INVALID_GEOMETRY","The native selected area total is not finite.");
            }
            return total;
        }
        finally{foreach(var area in areas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable disposable)disposable.Dispose();}
    }
    internal static void RequireRegionPoint(SketchProfile profile,SketchPlaneSpec plane,Point3 point,Action cancel)
    {
        DrawingWorkplane.Local(plane,point);var native=new G.Point3d(point.X,point.Y,point.Z);
        cancel();var areas=profile.GetContours(false);Need(areas!=null&&areas.Count>0,"PROFILE_GEOMETRY_UNAVAILABLE","The native selected profile regions are unavailable.");
        try
        {
            int selected=0;
            foreach(var area in areas)
            {
                cancel();Need(area!=null,"PROFILE_GEOMETRY_UNAVAILABLE","An actual selected native area is unavailable.");
                bool inside=area.IsPointIn(native,Service.Tol3d);
                using var outer=area.External;Need(outer!=null&&outer.IsClosed(),"PROFILE_GEOMETRY_UNAVAILABLE","The selected native outer boundary is unavailable or open.");
                Need(!outer.IsOn(native,out double _,Service.Tol3d),"PROFILE_REGION_AMBIGUOUS","The region point lies on the selected native outer boundary.");
                var holes=area.Internal;Need(holes!=null,"PROFILE_GEOMETRY_UNAVAILABLE","The selected native hole inventory is unavailable.");
                try{foreach(var hole in holes){cancel();Need(hole!=null&&hole.IsClosed(),"PROFILE_GEOMETRY_UNAVAILABLE","A selected native hole boundary is unavailable or open.");Need(!hole.IsOn(native,out double _,Service.Tol3d),"PROFILE_REGION_AMBIGUOUS","The region point lies on a selected native hole boundary.");}}
                finally{foreach(var hole in holes.Distinct(ReferenceEqualityComparer.Instance))if(hole is IDisposable disposable)disposable.Dispose();}
                if(inside)selected++;
            }
            Need(selected==1,"PROFILE_REGION_AMBIGUOUS","The region point must lie inside exactly one selected native area and outside its holes.");
        }
        finally{foreach(var area in areas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable disposable)disposable.Dispose();}
    }
}
