using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed class CadModelBodyNames
{
    private Func<Entity, string?>? _read;

    internal NanoCAD.Contracts.EntitySummary Observe(Entity entity, NanoCAD.Contracts.EntitySummary summary)
    {
        if (entity is not Solid3d || summary.space != "model") return summary;
        _read ??= Reader();
        return summary with { body_name = _read(entity) };
    }

    // Keep the optional mixed-mode assemblies outside the ordinary query JIT path.
    private static Func<Entity, string?> Reader()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes") ? ReadNativeName : static _ => null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? ReadNativeName(Entity entity)
    {
        try
        {
            var document = McDocument.ActiveDocument;
            return document == null ? null : Resolve(entity, document)?.Name;
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException or AccessViolationException))
        {
            // The basic DWG identity remains observable even if optional MultiCAD
            // metadata is unavailable. Name-filtered results disclose exclusions.
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
