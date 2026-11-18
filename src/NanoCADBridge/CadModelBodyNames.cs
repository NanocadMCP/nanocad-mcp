using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using Teigha.DatabaseServices;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal sealed class CadModelBodyNames
{
    internal sealed record Observation(string? Name, string? Source, string? Native, ModelBodyNameGroup? Group);
    private Func<Entity, (string? Name, bool? Imported)>? _read;

    internal EntitySummary Observe(Entity entity, EntitySummary summary, Transaction tx, Action cancel)
    {
        if (entity is not Solid3d || summary.space != "model") return summary;
        _read ??= Reader();
        var (name, imported) = _read(entity);
        var observation = Inspect(entity, tx, summary.@ref, name, imported, cancel);
        return summary with
        {
            body_name = observation.Name,
            body_name_source = observation.Source,
            native_tree_name = observation.Native,
            body_name_group = observation.Group
        };
    }

    internal static Observation Inspect(Entity entity, Transaction tx, EntityRef reference, string? native, bool? imported, Action cancel)
    {
        if (imported != false)
        {
            try
            {
                var found = DwgBodyNameGroup.Find(entity, tx, cancel);
                if (found.Ambiguous) return new(null, "ambiguous_dwg_group", native, null);
                if (found.Group != null) return new(found.Group.Description, "dwg_group_description", native, found.Group.Reference(reference));
            }
            catch (Teigha.Runtime.Exception) { return new(null, null, native, null); }
        }
        return new(native, native == null ? null : "native_tree", native, null);
    }

    // Keep the optional mixed-mode assemblies outside the ordinary query JIT path.
    private static Func<Entity, (string?, bool?)> Reader()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes") ? ReadNativeName : static _ => (null, null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string?, bool?) ReadNativeName(Entity entity)
    {
        try
        {
            var document = McDocument.ActiveDocument;
            var body = document == null ? null : Resolve(entity, document);
            return body == null ? (null, null) : (ReadTreeName(body), IsImported(body));
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException or AccessViolationException))
        {
            // The basic DWG identity remains observable even if optional MultiCAD
            // metadata is unavailable. Name-filtered results disclose exclusions.
            return (null, null);
        }
    }

    internal static bool IsImported(Mc3dSolid solid) => solid.ID.QueryObjectType() == typeof(Mc3dAlienSolid);

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
