using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Direct bodies only. A MultiCAD representation must be changed through its model adapter.
internal static class CadSolidEditor
{
    internal static bool CheckSource(Solid3d solid, Database db, Action cancel)
    {
        cancel();
        if (solid.GetType() != typeof(Solid3d) || solid.RecordHistory || solid.ShowHistory || solid.HasFields)
            throw new DomainException("UNSUPPORTED_SOLID_HISTORY", "Direct solid editing requires an exact native Solid3d without enabled history or fields.");
        using var reactors = solid.GetPersistentReactorIds();
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool documented=false;
        if (loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes")) documented=RequireDirect(solid, db);
        if(reactors.Count!=0 && !documented)
            throw new DomainException("UNSUPPORTED_SOLID_DEPENDENCIES", "This solid has unclassified persistent dependencies.");
        if(documented)DirectSolidDocumentation.CheckReactors(solid,reactors);
        CheckGeometry(solid, cancel);
        return documented;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool RequireDirect(Solid3d solid, Database db)
    {
        var document = Multicad.DatabaseServices.McDocument.ActiveDocument;
        if (document == null)
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "Cannot establish the active MultiCAD context for the solid identity check.");
        if (!string.IsNullOrEmpty(db.Filename) && !string.Equals(Path.GetFullPath(db.Filename), Path.GetFullPath(document.Path), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "MultiCAD context differs from the requested database.");
        var id = Multicad.McObjectId.FromHandle(solid.Handle.Value, document.ID);
        if(id.IsNull)return false;
        var model=id.GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();
        if(model==null)return false;
        // Native box/sphere creation is exposed by MultiCAD as Mc3dAlienSolid:
        // one non-parametric self item, not a sketch/feature history. Preserve
        // protection for actual models and for unknown/external dependencies.
        if(id.QueryObjectType()!=typeof(Multicad.Mc3D.Mc3dAlienSolid)||model.ID!=id)
            throw new DomainException("UNSUPPORTED_SOLID_HISTORY", "This solid has native model history; use its model adapter.");
        var contents=model.GetPartContents(false);var item=id.GetObjectOfType<Multicad.Mc3D.HistoryItem>();
        if(contents==null||!contents.Contains(id)||item==null||item.IsSuppressed||item.Status.ToString()!="Ok")
            throw new DomainException("UNSUPPORTED_SOLID_HISTORY", "A direct body requires its healthy non-parametric self item.");
        foreach(var child in contents.Where(c=>c!=id))DirectSolidDocumentation.RequireDependency(child,id);
        var inputs=item.GetDependsOn(false,false);var dependents=item.GetDependent();
        if(inputs==null||inputs.Count!=0||dependents==null)
            throw new DomainException("UNSUPPORTED_SOLID_DEPENDENCIES", "The non-parametric body has unknown or existing model dependencies.");
        foreach(var dependent in dependents)DirectSolidDocumentation.RequireDependency(dependent,id);
        bool documented=contents.Count>1||dependents.Count>0;
        if(!documented&&model.LastPartItemID!=id)
            throw new DomainException("UNSUPPORTED_SOLID_HISTORY","Non-parametric body has an unclassified history tail.");
        return documented;
    }

    internal static Matrix3d Matrix(SolidEditTransform transform)
    {
        Point3d Point(Point3 p) => new(p.X, p.Y, p.Z);
        Vector3d Vector(Point3 p) => new(p.X, p.Y, p.Z);
        if (transform.Kind == "translation") return Matrix3d.Displacement(Vector(transform.Displacement!));
        if (transform.Kind == "rotation") return Matrix3d.Rotation(transform.Angle, Vector(transform.Direction!).GetNormal(), Point(transform.Origin!));
        if (transform.Kind == "scale") return Matrix3d.Scaling(transform.Factor, Point(transform.Origin!));
        if (transform.Kind == "mirror")
        {
            using var plane = new Plane(Point(transform.Origin!), Vector(transform.Direction!).GetNormal());
            return Matrix3d.Mirroring(plane);
        }
        throw new DomainException("INVALID_ARGUMENT", "Unknown solid transform.");
    }

    internal static void Transform(Solid3d solid, Matrix3d matrix, double factor, Action cancel)
    {
        CheckGeometry(solid, cancel);
        if (!double.IsFinite(factor) || factor <= 0)
            throw new DomainException("INVALID_GEOMETRY", "Transformed solid volume is outside the supported finite range.");
        solid.TransformBy(matrix);
        CheckGeometry(solid, cancel);
    }

    internal static void CheckGeometry(Solid3d solid, Action cancel)
    {
        cancel();
        if (solid.IsNull) throw new DomainException("DEGENERATE_GEOMETRY", "Direct editing requires a nonempty solid.");
        var mass = solid.MassProperties;
        if (!double.IsFinite(mass.Volume) || mass.Volume <= 1e-12)
            throw new DomainException("INVALID_GEOMETRY", "Solid volume must be finite and positive.");
        void Point(Point3d p)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || Math.Abs(p.X) > 1e9 || Math.Abs(p.Y) > 1e9 || Math.Abs(p.Z) > 1e9)
                throw new DomainException("INVALID_GEOMETRY", "Solid exceeds finite WCS coordinate bounds.");
        }
        var box = solid.GeometricExtents;
        Point(mass.Centroid); Point(box.MinPoint); Point(box.MaxPoint);
        cancel();
    }
}
