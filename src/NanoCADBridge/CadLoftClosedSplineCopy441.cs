using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// A direct kernel input only. Keep the resident source and its association/data
// graph untouched; do not refit its points or convert through geometry curves.
internal static class CadLoftClosedSplineCopy441
{
    internal static Entity Detached(CadLoftSource409.State state, Action cancel)
    {
        cancel();
        if (state.Source.GetType() != typeof(Spline) || !((Spline)state.Source).Closed)
            return CadContourBudget344.Detached(state.Source);

        // Original SDK database.h:7671-7673 /7762-7769. Installed constructor
        // calls OdDbSpline.createObject + setNurbsData with these native values.
        // In441d, RXObject.Clone changed the closed source's periodic definition
        // and controls. Copy the complete observed native definition instead;
        // the existing exact full-definition comparison still gates kernel use.
        var source = (Spline)state.Source;
        var native = source.NurbsData;
        using var points = native.GetControlPoints();
        var knots = native.GetKnots(); var weights = native.GetWeights();
        cancel();
        var copy = new Spline(native.Degree, native.Rational, native.Closed, native.Periodic,
            points, knots, weights, native.ControlPointTolerance, native.KnotTolerance);
        // A failed independence proof never adopts or disposes a possible alias.
        if (copy.GetType() != typeof(Spline) || !copy.ObjectId.IsNull || !copy.OwnerId.IsNull ||
            copy.UnmanagedObject == IntPtr.Zero || copy.UnmanagedObject == source.UnmanagedObject)
            throw new DomainException("UNSAFE_NATIVE_CLONE", "The native closed-Spline input copy did not prove an exact independent detached root; an unsafe alias was not adopted or disposed.");
        try { cancel(); return copy; }
        catch { copy.Dispose(); throw; }
    }
}
