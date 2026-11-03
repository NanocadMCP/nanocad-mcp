using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Bound retained verification data by its real serialized size, never by a
// number of native history items, projections, variables or pattern elements.
internal sealed class ModelInventoryBudget
{
    private const long MaxBytes = 8L * 1024 * 1024;
    private readonly Action _cancel;
    private long _bytes = 2;
    internal ModelInventoryBudget(Action? cancel = null) => _cancel = cancel ?? (() => { });
    internal void Add<T>(T value)
    {
        _cancel();
        _bytes += JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).LongLength + 1;
        if (_bytes > MaxBytes)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Native model verification inventory exceeds the actual8MiB byte budget.");
    }
    internal static void Ids(IEnumerable<McObjectId> ids, Action? cancel = null)
    {
        var budget = new ModelInventoryBudget(cancel);
        foreach (var id in ids) budget.Add(id.ToString());
    }
    internal static List<McObjectId> History(Mc3dSolid body, Action? cancel = null)
    {
        cancel?.Invoke();
        var ids = body.GetPartContents(false) ?? throw new DomainException("HISTORY_UNAVAILABLE", "Native model history inventory is unavailable.");
        Ids(ids, cancel);
        return ids;
    }
    internal static void Pattern(RectangularPatternFeature pattern, Action? cancel = null)
    {
        if (pattern.SourceItemsCount <= 0)
            throw new DomainException("MODEL_STATE_UNAVAILABLE", "Native pattern has no source items.");
        var budget = new ModelInventoryBudget(cancel);
        for (int index = 0; index < pattern.SourceItemsCount; index++)
        {
            cancel?.Invoke();
            var source = pattern.GetSourceItemFeature(index);
            budget.Add(source.ToString());
        }
        var suppressed = pattern.GetSuppressedElementsIndices();
        // The full native snapshot retains null as unknown. Byte accounting
        // must not add a new pattern refusal or invent an empty collection.
        if (suppressed == null) { budget.Add<object?>(null); return; }
        foreach (var index in suppressed) budget.Add(new[] { index.Index1, index.Index2 });
    }
}
