using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadDimensionLineEditor
{
    internal static void CheckSource(Entity entity)
    {
        if(entity.GetType()!=typeof(AlignedDimension)&&entity.GetType()!=typeof(RotatedDimension))
            throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT","Dimension line placement supports exact native aligned/rotated dimensions.");
        CadDimensionTextEditor.CheckSource(entity);
        if(!((Dimension)entity).UsingDefaultTextPosition)
            throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT","Dimension line placement requires automatic text placement.");
        var definition=Wire.Element(DimensionGeometryReader.Definition((Dimension)entity));
        foreach(var field in definition.EnumerateObject().Where(p=>p.Value.ValueKind==System.Text.Json.JsonValueKind.Array))
            if(field.Value.EnumerateArray().Any(v=>Math.Abs(v.GetDouble())>1e6))throw new DomainException("OUT_OF_SCOPE","Dimension placement coordinates exceed +/-1000000mm.");
        DimensionLineGeometry.Axis(definition);
    }
    internal static void Apply(Entity entity,SetDimensionLineSpec edit,Action cancel,bool regenerate)
    {
        cancel();CheckSource(entity);var d=(Dimension)entity;
        var before=Wire.Element(DimensionGeometryReader.Definition(d));var expected=DimensionLineGeometry.Expected(before,edit);
        var style=d.DimensionStyle;var text=d.DimensionText;var rotation=d.TextRotation;var normal=d.Normal;var elevation=d.Elevation;
        var target=new Point3d(edit.Point.X,edit.Point.Y,0);
        if(d is AlignedDimension aligned)aligned.DimLinePoint=target;else ((RotatedDimension)d).DimLinePoint=target;
        if(regenerate)d.RecomputeDimensionBlock(true);
        cancel();CheckSource(d);
        if(!DimensionTextGeometry.SameDefinition(expected,Wire.Element(DimensionGeometryReader.Definition(d))) || d.DimensionStyle!=style || d.DimensionText!=text || d.TextRotation!=rotation || d.Normal!=normal || d.Elevation!=elevation)
            throw new DomainException("NATIVE_DIMENSION_MISMATCH","Dimension placement changed measured endpoints, orientation, text/style or the requested dimension line.");
        if(regenerate)
        {
            var measurement=DimensionLineGeometry.Axis(before).Measurement;double actual=d.Measurement;
            if(!double.IsFinite(actual)||Math.Abs(actual-measurement)>Math.Max(1e-7,1e-8*measurement))throw new DomainException("NATIVE_DIMENSION_MISMATCH","Dimension placement changed the native measurement.");
        }
    }
}
