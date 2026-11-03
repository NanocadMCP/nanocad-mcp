using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Read-only native sources for a new direct loft/sweep. No selected source is
// transferred, transformed, consumed or presented as an associative input.
internal static class CadLoftSource409
{
    internal sealed record State(Entity Source, JsonElement Shape, JsonElement Root,
        JsonElement Display, bool CompleteShape, bool HasOwnedData);
    private static string? Id(ObjectId value) => value.IsNull ? null : Validation.NormalizeHandle(value.Handle.ToString());
    private static void Need(bool ok, string code, string message)
    { if (!ok) throw new DomainException(code, message); }

    internal static Entity Resolve(string handle, bool profile, Database db, Transaction tr, ObjectId model, Action cancel)
    {
        cancel(); var id = CadStructureWriter.Find(db, handle);
        var value = tr.GetObject(id, OpenMode.ForRead) as Entity;
        Need(value != null && value.ObjectId == id && !id.IsNull && id.IsValid && !id.IsErased &&
            !id.IsEffectivelyErased && value.OwnerId == model && value.Database?.UnmanagedObject == db.UnmanagedObject &&
            value.UnmanagedObject != IntPtr.Zero, "OUT_OF_SCOPE", "Loft/sweep sources must be committed native entities in the current Model Space.");
        Need(profile ? value is Curve or Region : value is Curve, "UNSUPPORTED_ENTITY_TYPE",
            profile ? "A solid section requires a closed native curve or Region." : "A guide or trajectory must be a native Curve; a Region cannot be used as a path.");
        if (profile && value is Curve curve)
            Need(curve.Closed, "INVALID_GEOMETRY", "A direct solid section must be closed; select a closed native curve or Region.");
        if (value is Region region)
            Need(!region.IsNull, "EMPTY_GEOMETRY", "The selected native Region is empty.");
        cancel(); return value!;
    }

    private static JsonElement Shape(Entity source, Action cancel, out bool complete)
    {
        cancel(); complete = source is Curve c && CadRetainedSolidPath392.Supported(c);
        if (complete) return CadRetainedSolidPath392.StableShape((Curve)source, cancel);
        // Other existing native curves/Regions keep their previous kernel
        // admission. Do not invent a tessellated replacement or require an
        // analytical integral merely because a complete reader is absent.
        // The exact detached-root proof below still excludes resident aliases.
        return Wire.Element(new { runtime_type = source.GetType().FullName,
            closed = source is Curve curve ? (bool?)curve.Closed : null,
            region_empty = source is Region region ? (bool?)region.IsNull : null,
            observation_scope = "root_only; complete source shape is not observed by this adapter" });
    }

    internal static State Capture(Entity source, Action cancel)
    {
        var shape = Shape(source, cancel, out bool complete); var budget = new CadContourBudget344(cancel);
        using var data = source.XData; using var links = source.GetPersistentReactorIds();
        var reactors = new List<string?>();
        foreach (ObjectId link in links) { cancel(); var id = Id(link); budget.Add(id!); reactors.Add(id); }
        var root = Wire.Element(new { handle = Id(source.ObjectId), owner_handle = Id(source.OwnerId),
            runtime_type = source.GetType().FullName, source.HasFields, extension_handle = Id(source.ExtensionDictionary),
            xdata = data == null ? null : NativeOwnedMetadata.Buffer(data, cancel, budget.Add), reactors });
        var display = Wire.Element(new { layer_handle = Id(source.LayerId), properties = EntityAdapter.StoredProperties(source) });
        CadContourBudget344.Check(new { shape, root, display }, cancel);
        return new(source, shape, root, display, complete,
            source.HasFields || !source.ExtensionDictionary.IsNull || reactors.Count != 0);
    }

    internal static void Verify(IEnumerable<State> states, Action cancel)
    {
        foreach (var before in states)
        {
            cancel(); var after = Capture(before.Source, cancel);
            Need(before.CompleteShape == after.CompleteShape &&
                before.Shape.GetRawText()==after.Shape.GetRawText() &&
                before.Root.GetRawText()==after.Root.GetRawText() &&
                before.Display.GetRawText()==after.Display.GetRawText(), "NATIVE_CURVE_SOLID_SOURCE_CHANGED",
                "Native independent loft/sweep construction changed a retained source's observed geometry, identity, appearance or root data.");
        }
    }

    internal static void VerifyCopy(State before, Entity copy, Action cancel,List<string>? warnings=null)
    {
        cancel(); var actual = Shape(copy, cancel, out bool complete);
        if(before.CompleteShape!=complete)throw new DomainException("NATIVE_LOFT_SOURCE_COPY_MISMATCH",
            "An independent native input copy changed its complete source observation scope.",false,
            new{source_complete=before.CompleteShape,copy_complete=complete,differences=CadCurveCopyGeometry417.Differences(before.Shape,actual,cancel)});
        CadCurveCopyGeometry417.Require(before.Shape,actual,before.Source.GetType()==typeof(Spline)&&copy.GetType()==typeof(Spline),
            (a,b)=>a.GetRawText()==b.GetRawText(),"NATIVE_LOFT_SOURCE_COPY_MISMATCH",
            "An independent native input copy does not retain the reported source definition.","direct_loft_sweep",cancel,warnings);
    }

    internal static void Warnings(IEnumerable<State> states, List<string>? warnings)
    {
        if (warnings == null) return;
        var partial = states.Where(s => !s.CompleteShape).Select(s => s.Source.GetType().Name).Distinct().ToArray();
        if (partial.Length != 0)
            warnings.Add("DIRECT_CURVE_SOURCE_SHAPE_PARTIAL: Exact independent detached copies were passed to the native kernel; original " +
                string.Join(", ", partial) + " sources remain read-only with observed root identity/data/appearance. This adapter does not certify their complete source geometry; inspect the required profiles and result.");
        if (states.Any(s => s.HasOwnedData))
            warnings.Add("DIRECT_CURVE_SOURCE_OWNED_DATA_UNVERIFIED: Retained source Fields, extension and reactor identities are observed; their private internal records are not deeply rechecked. A direct loft/sweep creates no persistent source association or model feature history.");
    }
}
