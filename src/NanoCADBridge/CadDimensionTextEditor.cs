using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadDimensionTextEditor
{
    internal static bool Supported(Type type)=>type==typeof(AlignedDimension) || type==typeof(RotatedDimension) || type==typeof(RadialDimension) || type==typeof(DiametricDimension);
    internal static void CheckSource(Entity entity)
    {
        if(!Supported(entity.GetType()) || entity is not Dimension d || d.HasFields || !DimensionTextContent.IsPlain(d.DimensionText))
            throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT","Requires a supported native dimension with plain text and no fields.");
        //107 observed stable text AND rendered MText centers only for the fresh
        //manual diametric DIMTMOVE=2 case. Never change the user's movement style.
        bool placement=d.GetType()==typeof(DiametricDimension)
            ? !d.UsingDefaultTextPosition && d.Dimtmove==2 : d.UsingDefaultTextPosition;
        if(!placement)
            throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT","Requires automatic aligned/rotated/radial placement, or a manual diametric dimension already using DIMTMOVE=2.");
        using var reactors=d.GetPersistentReactorIds();
        if(reactors.Count!=0)throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT","Dimensions with persistent dependencies are outside this text-edit scope.");
        if(d.Normal.X!=0 || d.Normal.Y!=0 || d.Normal.Z!=1 || d.Elevation!=0 || d.TextPosition.Z!=0 ||
            !double.IsFinite(d.TextPosition.X) || !double.IsFinite(d.TextPosition.Y) || Math.Abs(d.TextPosition.X)>1e9 || Math.Abs(d.TextPosition.Y)>1e9 ||
            !double.IsFinite(d.TextRotation) || d.DimensionStyle.IsNull || !d.DimensionStyle.IsValid || d.DimensionStyle.IsErased)
            throw new DomainException("OUT_OF_SCOPE","Dimension text editing requires WCSXY/Z0 and a valid existing style.");
        var definition=Wire.Element(DimensionGeometryReader.Definition(d));
        foreach(var field in definition.EnumerateObject())
        {
            if(field.Value.ValueKind==System.Text.Json.JsonValueKind.Array)
            {
                var p=field.Value.EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                if(p.Length!=3 || p[2]!=0 || p.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1e9))throw new DomainException("OUT_OF_SCOPE","Dimension definition exceeds bounded WCSXY geometry.");
            }
            else if(field.Value.ValueKind==System.Text.Json.JsonValueKind.Number && (!field.Value.TryGetDouble(out var n)||!double.IsFinite(n)||Math.Abs(n)>1e9))
                throw new DomainException("INVALID_GEOMETRY","Nonfinite or excessive dimension definition.");
        }
    }
    internal static void Apply(Entity entity,ReplaceDimensionTextSpec edit,Action cancel,bool regenerate)
    {
        cancel();CheckSource(entity);var dimension=(Dimension)entity;
        if(dimension.DimensionText!=edit.ExpectedText)throw new DomainException("TEXT_CONTENT_CHANGED","Dimension text differs from expected_text after preceding edits.");
        var before=Wire.Element(DimensionGeometryReader.Definition(dimension));
        var style=dimension.DimensionStyle;var rotation=dimension.TextRotation;var normal=dimension.Normal;var elevation=dimension.Elevation;
        var defaultPosition=dimension.UsingDefaultTextPosition;var position=dimension.TextPosition;
        dimension.DimensionText=edit.Text;
        if(regenerate)dimension.RecomputeDimensionBlock(true);
        cancel();CheckSource(dimension);
        var after=Wire.Element(DimensionGeometryReader.Definition(dimension));
#if DIMENSION_TEXT_FAILURE_PROBE
        DimensionTextFailureProbe.RecordComparison(new{handle=dimension.Handle.ToString(),old_definition=before,new_definition=after,old_style=style.Handle.ToString(),new_style=dimension.DimensionStyle.Handle.ToString(),old_rotation=rotation,new_rotation=dimension.TextRotation,
            old_default_position=defaultPosition,new_default_position=dimension.UsingDefaultTextPosition,old_position=new[]{position.X,position.Y,position.Z},new_position=new[]{dimension.TextPosition.X,dimension.TextPosition.Y,dimension.TextPosition.Z},text=dimension.DimensionText});
#endif
        if(dimension.DimensionText!=edit.Text || dimension.DimensionStyle!=style || dimension.TextRotation!=rotation || dimension.Normal!=normal || dimension.Elevation!=elevation ||
            dimension.UsingDefaultTextPosition!=defaultPosition || (!defaultPosition && dimension.TextPosition.DistanceTo(position)>1e-8) ||
            !DimensionTextGeometry.SameDefinition(before,after))
            throw new DomainException("NATIVE_DIMENSION_MISMATCH","Native text edit changed requested text, measured definition, style or orientation.");
    }
}
