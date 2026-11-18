using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
namespace NanoCADBridge;

internal static class CadDimensionTextPositionEditor
{
    private static void CheckSource(Entity entity, bool ordinaryAssociation = false)
    {
        if (!CadDimensionTextEditor.Supported(entity.GetType()) || entity is not Dimension d || d.HasFields ||
            d.Normal != Vector3d.ZAxis || d.Elevation != 0 || !DimensionTextContent.IsPlain(d.DimensionText))
            throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "Text placement needs a plain native aligned/rotated/radial/diametric dimension in WCS XY.");
        using var reactors = d.GetPersistentReactorIds();
        if (!ordinaryAssociation && reactors.Count != 0) throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "Associative dimension ownership requires its adapter.");
        if (!double.IsFinite(d.TextPosition.X) || !double.IsFinite(d.TextPosition.Y) || d.TextPosition.Z != 0)
            throw new DomainException("INVALID_GEOMETRY", "Dimension text position is unavailable.");
    }
    internal static ObjectId CheckResident(Dimension dimension, Database db, Transaction tr, Action cancel)
    {
        using var reactors = dimension.GetPersistentReactorIds();
        if (dimension is DiametricDimension && (reactors.Count != 0 || !dimension.ExtensionDictionary.IsNull))
        {
            var association = CadDimensionAssociation385.FindId(dimension, db, tr, cancel);
            if (!association.IsNull)
            {
                CheckSource(dimension, ordinaryAssociation: true);
                if (reactors.Cast<ObjectId>().Any(id => id != association))
                    throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "The diameter has another persistent dependency.");
                _ = Retained(dimension, association, tr, cancel);
                return association;
            }
        }
        CheckSource(dimension);
        return ObjectId.Null;
    }

    internal static void PreviewPositions(Dimension dimension, IEnumerable<SetDimensionTextPositionSpec> edits)
    {
        var position = dimension.TextPosition;
        foreach (var edit in edits)
        {
            Expected(position, edit);
            position = new(edit.Position.X, edit.Position.Y, 0);
        }
    }

    private static void Expected(Point3d current, SetDimensionTextPositionSpec edit)
    {
        if (current.DistanceTo(new Point3d(edit.Expected.X, edit.Expected.Y, 0)) > 1e-6)
            throw new DomainException("PRECONDITION_FAILED", "Read the dimension's current text_position before moving its label.");
    }

    private static string Retained(Dimension dimension, ObjectId association, Transaction tr, Action cancel)
    {
        if (dimension.Dimtmove != 2)
            throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "Moving the label of an associated diameter requires an existing effective DIMTMOVE of 2.");
        var snapshot = CadDimensionAssociation385.Capture(dimension, dimension.Database, tr, cancel);
        CadDimensionAssociation385.Need(snapshot.Association == association && snapshot.Active && snapshot.Points.All(p =>
            p.Snap == ObjectSnapModes.ModeQuad && CadDimensionAssociation385.DefinitionPoint(dimension, p.Role).DistanceTo(p.Evaluated) <= 1e-7),
            "The diameter must retain its actual local Circle quadrants at its measured endpoints.");
        CadDimensionAssociation385.CheckCircleDiameter(dimension, snapshot, tr);
        var links = CadDimensionAssociation385.Connections(dimension, association, snapshot.Points, tr, cancel);
        CadDimensionAssociation385.VerifyConnections(links, "retained_diameter_label");
        var display = Wire.Element(CadDimensionEditing341.ReadDisplay(dimension)).EnumerateObject()
            .Where(p => p.Name != "automatic_text_position").ToDictionary(p => p.Name, p => p.Value);
        return Wire.Element(new { snapshot.Token, snapshot.Containers, links, format = CadDimensionFormatEditor.Read(dimension), display }).GetRawText();
    }

    internal static void MoveAssociated(Dimension dimension, SetDimensionTextPositionSpec edit, ObjectId association, Transaction tr, Action cancel)
    {
        var before = Retained(dimension, association, tr, cancel);
        Apply(dimension, edit, cancel, true, ordinaryAssociation: true);
        if (Retained(dimension, association, tr, cancel) != before)
            throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "Moving the diameter label changed its retained source, snap, dimension metadata or format.");
    }

    internal static void Apply(Entity entity, SetDimensionTextPositionSpec edit, Action cancel, bool regenerate, bool ordinaryAssociation = false)
    {
        cancel(); CheckSource(entity, ordinaryAssociation); var d = (Dimension)entity;
        Expected(d.TextPosition, edit);
        var definition = Wire.Element(DimensionGeometryReader.Definition(d));
        var measurement = d.Measurement; var style = d.DimensionStyle; var text = d.DimensionText; var rotation = d.TextRotation;
        // Explicit operation contract, not an implicit modification by text editing.
        if (d.Dimtmove != 2) d.Dimtmove = 2;
        var target = new Point3d(edit.Position.X, edit.Position.Y, 0);
        d.TextPosition = target;
        if (regenerate) d.RecomputeDimensionBlock(true);
        cancel();
        if (d.TextPosition.DistanceTo(target) > 1e-6 || d.UsingDefaultTextPosition || d.Dimtmove != 2 ||
           d.DimensionStyle != style || d.DimensionText != text || d.TextRotation != rotation ||
           !DimensionTextGeometry.SameDefinition(definition, Wire.Element(DimensionGeometryReader.Definition(d))) ||
           !double.IsFinite(d.Measurement) || Math.Abs(d.Measurement - measurement) > Math.Max(1e-7, Math.Abs(measurement) * 1e-9))
            throw new DomainException("NATIVE_DIMENSION_MISMATCH", "Text placement changed the measured definition or failed to retain the requested location.");
    }
}
