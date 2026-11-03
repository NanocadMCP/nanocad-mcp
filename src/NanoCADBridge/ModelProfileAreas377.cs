using System.Text.Json;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;

namespace NanoCADBridge;

// Explicit additive adapter for one whole-Circle cell with native holes.
// Existing single-loop geometry/token shape stays unchanged.
internal static class ModelProfileAreas377
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string message)
    {if(!ok)throw new DomainException("PROFILE_GEOMETRY_MISMATCH",message);}
    static Point3 P(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
    internal static SketchPlaneSpec Plane(JsonElement frame)
    {
        var plane=new SketchPlaneSpec(P(frame.GetProperty("origin_wcs")),P(frame.GetProperty("x_step_wcs")),P(frame.GetProperty("y_step_wcs")));
        Need(ModelParameterSnapshot.Near(DrawingWorkplane.Dot(plane.XAxis,plane.XAxis),1)&&ModelParameterSnapshot.Near(DrawingWorkplane.Dot(plane.YAxis,plane.YAxis),1)&&
            Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.YAxis))<1e-9,"The native hole profile requires observed perpendicular unit sketch coordinates without normalization.");
        return plane;
    }
    internal static JsonElement CircularArea(G.Polyline3d outer,IEnumerable<G.Polyline3d> holes,JsonElement sketchGeometry,Action cancel)
    {
        var frame=sketchGeometry.GetProperty("frame");var plane=Plane(frame);
        Need(sketchGeometry.GetProperty("geometry_details_complete").GetBoolean(),"A hole profile requires complete observed native sketch geometry.");
        var curves=ModelProfileLifecycleState340.Boundary(sketchGeometry,plane,cancel);
        Need(curves.Length>0&&curves.All(curve=>curve.Kind=="circle"),"The new hole profile adapter requires a separated/nested whole-Circle sketch; mixed boundary unions are not inferred.");
        var loops=curves.Select(curve=>new ModelCircleLoop350(curve.Id,curve.Center!,curve.Radius,curve.Id)).ToArray();
        var parents=ModelCircleRegions350.Parents(loops,cancel);
        string outside=ModelProfileRegionsReader350.CircleLoop(outer,loops,plane,cancel);
        var actualHoles=holes.Select(hole=>{cancel();return (Id:ModelProfileRegionsReader350.CircleLoop(hole,loops,plane,cancel),Geometry:ModelProfileElementsReader358.Loop(hole,frame,cancel));}).ToArray();
        var expected=parents.Where(pair=>pair.Value==outside).Select(pair=>pair.Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
        Need(actualHoles.Select(hole=>hole.Id).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).SequenceEqual(expected,StringComparer.OrdinalIgnoreCase),"Selected native holes must be exactly the immediate whole-Circle boundaries of the actual atomic cell.");
        var area=new{outer=ModelProfileElementsReader358.Loop(outer,frame,cancel),holes=actualHoles.OrderBy(hole=>hole.Id,StringComparer.OrdinalIgnoreCase).Select(hole=>hole.Geometry).ToArray(),
            outer_element_id=outside,hole_element_ids=expected};
        var result=Wire.Element(new{kind="profile_regions",coordinate_system="WCS",geometry_details_complete=true,frame,areas=new[]{area},
            identity_scope="boundary_order_only; area_element_ids_are_current_native_sketch_members"});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    internal static bool Same(JsonElement actual,SketchPlaneSpec plane,ModelSketchRegion377 expected,Action cancel)
    {
        if(!actual.TryGetProperty("areas",out var areas))
            return expected.Holes.Length==0&&ModelProfileBoundary340.Same(ModelProfileLifecycleState340.Boundary(actual,plane,cancel),expected.Outer,cancel);
        if(areas.ValueKind!=JsonValueKind.Array||areas.GetArrayLength()!=1)return false;
        var area=areas[0];var holes=area.GetProperty("holes").EnumerateArray().Select(value=>ModelProfileLifecycleState340.Boundary(value,plane,cancel)).ToArray();
        if(!ModelProfileBoundary340.Same(ModelProfileLifecycleState340.Boundary(area.GetProperty("outer"),plane,cancel),expected.Outer,cancel)||holes.Length!=expected.Holes.Length)return false;
        var matched=new HashSet<int>();
        foreach(var expectedHole in expected.Holes)
        {
            cancel();var candidates=Enumerable.Range(0,holes.Length).Where(index=>ModelProfileBoundary340.Same(holes[index],expectedHole,cancel)).ToArray();
            if(candidates.Length!=1||!matched.Add(candidates[0]))return false;
        }
        return matched.Count==holes.Length;
    }
}
