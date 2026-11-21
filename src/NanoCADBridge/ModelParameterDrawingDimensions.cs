using static NanoCADBridge.ModelParameterDimensionBinding;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelParameterDrawingDimensions
{
    private static Snapshot Read(string handle, ModelParameterArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        cancel();
        var db = state.Document.Database;
        var table = (Db.BlockTable)tx.GetObject(db.BlockTableId, Db.OpenMode.ForRead);
        var model = table[Db.BlockTableRecord.ModelSpace];
        var id = CadStructureWriter.Find(db, handle);
        if (tx.GetObject(id, Db.OpenMode.ForRead) is not Db.Dimension dim ||
            dim is not (Db.AlignedDimension or Db.RotatedDimension or Db.RadialDimension or Db.DiametricDimension) ||
            dim.OwnerId == model || tx.GetObject(dim.OwnerId, Db.OpenMode.ForRead) is not Db.BlockTableRecord { IsLayout: true })
            throw Unavailable(handle, "A paper-space linear, radial or diametric dimension is required.");
        var summary = EntityAdapter.Summary(dim, model, args.BridgeId, state.Id, paper: true);
        var detail = CadProjectedDimensionAssociationReader.ReadLoaded(dim, summary, cancel, includeDesignSource: false);
        if (detail == null) throw Unavailable(handle, "No local native projected dimension is available.");
        return ModelParameterDimensionBinding.Read(handle, Validation.NormalizeHandle(dim.OwnerId.Handle.ToString()), Wire.Element(detail));
    }

    internal static Snapshot[]? Before(ModelParameterArguments args, DocumentState state, Db.Transaction tx,
        IEnumerable<string> affectedBodies, Action cancel)
    {
        if (args.ObserveDimensionHandles == null) return null;
        var allowed = affectedBodies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var snapshots = new List<Snapshot>();
        var budget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel);
        foreach (string handle in args.ObserveDimensionHandles)
        {
            var before = Read(handle, args, state, tx, cancel);
            if (!allowed.Contains(before.BodyId)) throw new DomainException("OUT_OF_SCOPE", $"Dimension {handle} does not refer to a single affected model body.");
            budget.Add(before.Compare(before));
            snapshots.Add(before);
        }
        return snapshots.ToArray();
    }

    internal static ParameterDimensionObservation[]? After(Snapshot[]? before, ModelParameterArguments args, DocumentState state, Action cancel)
    {
        if (before == null) return null;
        var observations = new List<ParameterDimensionObservation>(before.Length);
        string stopped = "OBSERVATION_UNAVAILABLE";
        try
        {
            using var tx = state.Document.Database.TransactionManager.StartTransaction();
            foreach (var previous in before)
            {
                cancel();
                try { observations.Add(previous.Compare(Read(previous.DimensionHandle, args, state, tx, cancel))); }
                catch (Exception error) when (error is DomainException or Teigha.Runtime.Exception)
                {
                    observations.Add(previous.Compare(null, error is DomainException domain ? domain.Error.code : "NATIVE_READ_FAILED"));
                }
            }
        }
        catch (Exception error) when (error is OperationCanceledException or Teigha.Runtime.Exception)
        {
            // Geometry was already verified. End the optional reads without losing that outcome.
            stopped = error is OperationCanceledException ? "CANCELLED" : "NATIVE_READ_FAILED";
        }
        foreach (var pending in before.Skip(observations.Count)) observations.Add(pending.Compare(null, stopped));
        return observations.ToArray();
    }
}
