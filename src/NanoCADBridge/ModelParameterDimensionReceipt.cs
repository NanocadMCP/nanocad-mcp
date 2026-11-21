using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class ModelParameterDimensionReceipt
{
    internal static Result Fit(Result result, int budget)
    {
        if (result.data is not ModelParameterResult { observed_dimensions: { Length: > 0 } observations } data ||
            Size(result) <= budget)
            return result;

        Result Prefix(int count) => result with
        {
            data = data with { observed_dimensions = observations[..count] },
            warnings = [.. result.warnings,
                $"RESPONSE_LIMIT_EXCEEDED: Returned {count} of {observations.Length} requested dimension observations in order. Current geometry can be read again. Previous measurements for omitted dimensions cannot be recovered from the current revision."]
        };

        // Row bytes and the printed count grow monotonically. Measure the whole receipt, including the count warning.
        int lower = 0, upper = observations.Length - 1;
        while (lower < upper)
        {
            int count = lower + (upper - lower + 1) / 2;
            if (Size(Prefix(count)) <= budget) lower = count;
            else upper = count - 1;
        }
        return Prefix(lower);
    }

    private static int Size(Result result) => JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length;
}
