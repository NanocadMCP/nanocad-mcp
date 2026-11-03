using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// The SDK CHM declares native MInsert row/column counts/spacings and the
// BlockReference.BlockTransform mapping, but does not document whether the
// spacing getters are expressed before or after occurrence scale. The observed
// 407 getter bounds covered only the seed of a 2x3 insertion. Do not call that
// result a whole-array envelope or explode/enumerate all grid occurrences.
internal static class CadMInsertBounds407
{
    internal const string Kind = "conservative_minsert_spacing_envelope";
    internal const string Scope = "reported_native_extents_expanded_by_absolute_grid_travel_and_maximum_of_owner_or_definition_space_scale";
    internal readonly record struct Bounds(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ);

    internal static bool TryExpand(MInsertBlock array, Point3d reportedMin, Point3d reportedMax, Action cancel, out Bounds bounds)
    {
        bounds = default; cancel();
        if(!new[]{reportedMin.X,reportedMin.Y,reportedMin.Z,reportedMax.X,reportedMax.Y,reportedMax.Z}.All(double.IsFinite)||
            reportedMin.X>reportedMax.X||reportedMin.Y>reportedMax.Y||reportedMin.Z>reportedMax.Z)return false;
        var parameters = CadBlockArray384.Parameters(array);
        if (parameters.Rows == 0 || parameters.Columns == 0 || !double.IsFinite(parameters.RowSpacing) || !double.IsFinite(parameters.ColumnSpacing)) return false;
        var transform = array.BlockTransform;
        // Vector3d.TransformBy ignores affine translation, so no origin/base-point
        // guess is needed. A norm envelope also avoids assuming a row-axis order,
        // sign, rotation/reflection convention or a particular native plane.
        var axisX=Vector3d.XAxis.TransformBy(transform);var axisY=Vector3d.YAxis.TransformBy(transform);var axisZ=Vector3d.ZAxis.TransformBy(transform);
        double x = axisX.Length,y=axisY.Length,z=axisZ.Length;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return false;
        // Gram row sums bound the squared operator norm, including a retained
        // nonorthogonal matrix. Normalize first to avoid overflow. Orthogonal
        // native axes naturally retain their tight maximum scale bound.
        double scale=Math.Max(x,Math.Max(y,z));
        if(scale>0)
        {
            var u=new Vector3d(axisX.X/scale,axisX.Y/scale,axisX.Z/scale);
            var v=new Vector3d(axisY.X/scale,axisY.Y/scale,axisY.Z/scale);
            var w=new Vector3d(axisZ.X/scale,axisZ.Y/scale,axisZ.Z/scale);
            double uv=Math.Abs(u.DotProduct(v)),uw=Math.Abs(u.DotProduct(w)),vw=Math.Abs(v.DotProduct(w));
            scale*=Math.Sqrt(Math.Max(u.DotProduct(u)+uv+uw,Math.Max(v.DotProduct(v)+uv+vw,w.DotProduct(w)+uw+vw)));
        }
        double multiplier = Math.Max(1, scale);
        double travel = ((parameters.Rows - 1) * Math.Abs(parameters.RowSpacing) +
                         (parameters.Columns - 1) * Math.Abs(parameters.ColumnSpacing)) * multiplier;
        if (!double.IsFinite(travel)) return false;
        // Expanding in every XY direction contains both plausible spacing scales
        // and arbitrary grid-axis orientation; it intentionally overselects. Even
        // if a later SDK supplies whole-array extents, the expansion stays safe
        // as a candidate envelope. It is never an exact array/geometric bbox.
        double minX = reportedMin.X - travel, minY = reportedMin.Y - travel;
        double maxX = reportedMax.X + travel, maxY = reportedMax.Y + travel;
        double minZ=reportedMin.Z-travel,maxZ=reportedMax.Z+travel;
        if (!new[]{minX,minY,minZ,maxX,maxY,maxZ}.All(double.IsFinite)) return false;
        bounds = new(minX, minY,minZ, maxX, maxY,maxZ); cancel(); return true;
    }
    internal static Extents3d Read(MInsertBlock array,Action cancel)
    {
        cancel();var reported=array.GeometricExtents;
        if(!TryExpand(array,reported.MinPoint,reported.MaxPoint,cancel,out var envelope))
            throw new DomainException("ARRAY_BOUNDS_UNAVAILABLE","Complete finite native MInsert count/spacing/transform envelope is unavailable; seed bounds are not presented as the whole grid.");
        return new(new(envelope.MinX,envelope.MinY,envelope.MinZ),new(envelope.MaxX,envelope.MaxY,envelope.MaxZ));
    }
}
