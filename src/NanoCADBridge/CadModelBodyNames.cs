using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using Teigha.DatabaseServices;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal sealed class CadModelBodyNames
{
    internal sealed record Observation(string? Name, string? Source, string? Part, string? Native, ModelBodyNameGroup? Group, bool AmbiguousGroup);
    private Func<Entity, (string? Part, string? Item, bool? Alien)>? _read;

    internal EntitySummary Observe(Entity entity, EntitySummary summary, Transaction tx, Action cancel)
    {
        if (entity is not Solid3d || summary.space != "model") return summary;
        _read ??= Reader();
        var (part, item, alien) = _read(entity);
        var observation = Inspect(entity, tx, summary.@ref, part, item, alien, cancel);
        return summary with
        {
            body_name = observation.Name,
            body_name_source = observation.Source,
            part_name = observation.Part,
            native_tree_name = observation.Native,
            body_name_group = observation.Group,
            body_name_group_ambiguous = observation.AmbiguousGroup
        };
    }

    internal static Observation Inspect(Entity entity, Transaction tx, EntityRef reference, string? part, string? native, bool? alien, Action cancel)
    {
        string? name = native;
        string? source = native == null ? null : "native_tree";
        ModelBodyNameGroup? group = null;
        bool ambiguous = false;
        if (alien != false)
        {
            try
            {
                var found = DwgBodyNameGroup.Find(entity, tx, cancel);
                ambiguous = found.Ambiguous;
                if (ambiguous) (name, source) = (null, "ambiguous_dwg_group");
                if (found.Group != null)
                {
                    group = found.Group.Reference(reference);
                    (name, source) = (found.Group.Description, "dwg_group_description");
                }
            }
            catch (Teigha.Runtime.Exception) { (name, source) = (null, null); }
        }
        if (!string.IsNullOrWhiteSpace(part)) (name, source) = (part, "native_part_name");
        return new(name, source, part, native, group, ambiguous);
    }

    // Keep the optional mixed-mode assemblies outside the ordinary query JIT path.
    private static Func<Entity, (string?, string?, bool?)> Reader()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes") ? ReadNativeName : static _ => (null, null, null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string?, string?, bool?) ReadNativeName(Entity entity)
    {
        try
        {
            var document = McDocument.ActiveDocument;
            var body = document == null ? null : Resolve(entity, document);
            return body == null ? (null, null, null) : (ReadPartName(body), ReadTreeName(body), IsAlienSolid(body));
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException or AccessViolationException))
        {
            // The basic DWG identity remains observable even if optional MultiCAD
            // metadata is unavailable. Name-filtered results disclose exclusions.
            return (null, null, null);
        }
    }

    internal static bool IsAlienSolid(Mc3dSolid solid) => solid.ID.QueryObjectType() == typeof(Mc3dAlienSolid);

    internal static string? ReadPartName(Mc3dSolid solid)
    {
        try { return solid.PartName; }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException or AccessViolationException))
        {
            return null;
        }
    }

    internal static string? ReadTreeName(Mc3dSolid solid)
    {
        try { return solid.Name; }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException or AccessViolationException))
        {
            return null;
        }
    }

    internal static Mc3dSolid? Resolve(Entity entity, McDocument document)
    {
        var top = document.TopDoc;
        if (entity is not Solid3d || top == null || top.ModelSpaceId.ToOldIdPtr() != entity.OwnerId.OldIdPtr)
            return null;
        var native = McObjectId.FromHandle(entity.Handle.Value, document.ID);
        if (native.IsNull) return null;
        var body = native.GetObjectOfType<Mc3dSolid>();
        return body != null && body.ID == native && body.DbEntity?.Document?.TopDoc?.ID == top.ID ? body : null;
    }
}
