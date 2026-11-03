using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
namespace NanoCADBridge;

internal static class CadDimensionTextPositionEditor
{
    internal static void CheckSource(Entity entity)
    {
        if(!CadDimensionTextEditor.Supported(entity.GetType())||entity is not Dimension d||d.HasFields||
            d.Normal!=Vector3d.ZAxis||d.Elevation!=0||!DimensionTextContent.IsPlain(d.DimensionText))
            throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT","Text placement needs a plain native aligned/rotated/radial/diametric dimension in WCS XY.");
        using var reactors=d.GetPersistentReactorIds();
        if(reactors.Count!=0)throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT","Associative dimension ownership requires its adapter.");
        if(!double.IsFinite(d.TextPosition.X)||!double.IsFinite(d.TextPosition.Y)||d.TextPosition.Z!=0)
            throw new DomainException("INVALID_GEOMETRY","Dimension text position is unavailable.");
    }
    internal static void Apply(Entity entity,SetDimensionTextPositionSpec edit,Action cancel,bool regenerate)
    {
        cancel();CheckSource(entity);var d=(Dimension)entity;
        var expected=new Point3d(edit.Expected.X,edit.Expected.Y,0);
        if(d.TextPosition.DistanceTo(expected)>1e-6)
            throw new DomainException("PRECONDITION_FAILED","Read the dimension's current text_position before moving its label.");
        var definition=Wire.Element(DimensionGeometryReader.Definition(d));
        var measurement=d.Measurement;var style=d.DimensionStyle;var text=d.DimensionText;var rotation=d.TextRotation;
        // Explicit operation contract, not an implicit modification by text editing.
        d.Dimtmove=2;
        var target=new Point3d(edit.Position.X,edit.Position.Y,0);
        d.TextPosition=target;
        if(regenerate)d.RecomputeDimensionBlock(true);
        cancel();
        if(d.TextPosition.DistanceTo(target)>1e-6||d.UsingDefaultTextPosition||d.Dimtmove!=2||
           d.DimensionStyle!=style||d.DimensionText!=text||d.TextRotation!=rotation||
           !DimensionTextGeometry.SameDefinition(definition,Wire.Element(DimensionGeometryReader.Definition(d)))||
           !double.IsFinite(d.Measurement)||Math.Abs(d.Measurement-measurement)>Math.Max(1e-7,Math.Abs(measurement)*1e-9))
            throw new DomainException("NATIVE_DIMENSION_MISMATCH","Text placement changed the measured definition or failed to retain the requested location.");
    }
}
