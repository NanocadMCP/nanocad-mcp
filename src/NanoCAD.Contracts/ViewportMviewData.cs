using System.Collections;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ViewportDataValue(int Code, object Value);

// Native ACAD/MVIEW is a legacy representation of viewport settings, not third-party XData.
// It is inspected only; editing always uses the reviewed Viewport property setters.
public static class ViewportMviewData
{
    public const int MaximumBytes = Wire.PayloadBudget-8192;
    private static DomainException Unsupported(string why, object? details = null) => new("UNSUPPORTED_VIEWPORT", why, false, details);
    public static ViewportDataValue[] Read(IEnumerator values, Func<object, ViewportDataValue> convert, Action check)
    {
        var result = new List<ViewportDataValue>();int bytes=2;
        try
        {
            while (true)
            {
                check(); bool next = values.MoveNext(); check(); if (!next) break;
                var item = convert(values.Current!); check();
                bool supported = item.Code switch {
                    1000 or 1001 or 1002 or 1003 => item.Value is string text && text.Length <= 4096,
                    1070 => item.Value is short || item.Value is int number && number is >= short.MinValue and <= short.MaxValue,
                    1040 => item.Value is double number && double.IsFinite(number),
                    1010 => item.Value is double[] point && point.Length == 3 && point.All(double.IsFinite),
                    _ => false };
                if (!supported) throw Unsupported("Unsupported viewport XData value.", new { index = result.Count, code = item.Code });
                // A common canonical representation makes detached/resident comparisons exact.
                var canonical=item.Code == 1070 ? item with { Value = Convert.ToInt32(item.Value) } : item;
                bytes+=JsonSerializer.SerializeToUtf8Bytes(canonical,Wire.Json).Length+1;
                if(bytes>MaximumBytes)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Viewport MVIEW data exceeds the actual serialized payload budget; no record-count truncation occurred.");
                result.Add(canonical);
            }
        }
        finally { (values as IDisposable)?.Dispose(); }
        var data = result.ToArray(); Validate(data,check); return data;
    }
    public static void Validate(ViewportDataValue[] data,Action? check=null)
    {
        check?.Invoke();
        if (data.Length == 0) return;
        if (data.Length < 32) throw Unsupported("Expected one complete ACAD/MVIEW record.", new { count = data.Length });
        if(JsonSerializer.SerializeToUtf8Bytes(data,Wire.Json).Length>MaximumBytes)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Viewport MVIEW data exceeds the actual serialized payload budget.");
        var codes = new[] { 1001, 1000, 1002, 1070, 1010, 1010 }
            .Concat(Enumerable.Repeat(1040, 7)).Concat(Enumerable.Repeat(1070, 8))
            .Concat(Enumerable.Repeat(1040, 7)).Concat(new[] { 1070, 1002 })
            .Concat(Enumerable.Repeat(1003, data.Length - 32)).Concat(new[] { 1002, 1002 }).ToArray();
        for (int i = 0; i < data.Length; i++)
        {
            check?.Invoke();
            if (data[i].Code != codes[i]) throw Unsupported("Viewport XData is outside the ACAD/MVIEW format.", new { index = i, expected_code = codes[i], actual_code = data[i].Code });
            if(i>=30&&i<data.Length-2&&data[i].Value is not string)
                throw Unsupported("Frozen-layer MVIEW entries must be native layer names.",new{index=i});
        }
        if (!Equals(data[0].Value, "ACAD") || !Equals(data[1].Value, "MVIEW") || !Equals(data[2].Value, "{") || !Equals(data[3].Value, 16) ||
            !Equals(data[29].Value, "{") || !Equals(data[^2].Value, "}") || !Equals(data[^1].Value, "}"))
            throw Unsupported("Unknown viewport XData application, version or record boundaries.");
    }
    public static void RequireSame(ViewportDataValue[] expected, ViewportDataValue[] actual,Action? check=null)
    {
        Validate(expected,check); Validate(actual,check);
        if (expected.Length != actual.Length) throw new DomainException("API_EXCEPTION", "Viewport MVIEW record size changed unexpectedly.");
        ViewportDataValue[] FrozenOrder(ViewportDataValue[] values)=>values.Length==0?values:
            values.Take(30).Concat(values.Skip(30).Take(values.Length-32).OrderBy(value=>(string)value.Value,StringComparer.Ordinal)).Concat(values.TakeLast(2)).ToArray();
        // Frozen layers are a set. Native order changes do not change the
        // requested engineering visibility; all entries remain compared.
        expected=FrozenOrder(expected);actual=FrozenOrder(actual);
        for (int i = 0; i < expected.Length; i++)
        {
            check?.Invoke();
            if (i == 5 && expected[i].Code == 1010 && actual[i].Code == 1010 && expected[i].Value is double[] direction && actual[i].Value is double[] actualDirection && ViewportWriteValidation.SameDirection(direction, actualDirection)) continue;
            if (!JsonSerializer.SerializeToUtf8Bytes(expected[i], Wire.Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(actual[i], Wire.Json)))
                throw new DomainException("API_EXCEPTION", "Viewport MVIEW data changed outside the requested view properties.", false,
                    new { index = i, expected = expected[i], actual = actual[i] });
        }
    }
    public static void RequireDesired(ViewportDataValue[] before, ViewportDataValue[] actual, SetViewportViewEdit edit,Action? check=null)
    {
        Validate(before,check); Validate(actual,check);
        if (before.Length == 0 || actual.Length == 0)
        {
            RequireSame(before, actual,check); return;
        }
        if (edit.Properties.FrozenLayerHandles is null && before.Length != actual.Length) throw new DomainException("API_EXCEPTION", "Viewport MVIEW representation changed unexpectedly.");
        var expected = before.ToArray(); var p = edit.Properties;
        // The first four 1040 values are derived from exactly the four reviewed native view coordinates.
        // Public ViewportProperties verification independently enforces their requested numeric values/units.
        if (before.Length > 0)
        {
            if (p.ViewTarget is not null) expected[4] = actual[4];
            if (p.ViewDirection is not null) expected[5] = actual[5];
            if (p.TwistAngle.HasValue) expected[6] = actual[6];
            if (p.ViewHeight.HasValue) expected[7] = actual[7];
            if (p.ViewCenter is not null) { expected[8] = actual[8]; expected[9] = actual[9]; }
        }
        if (p.FrozenLayerHandles is not null)
        {
            // Codes1003 inside the final nested list contain only frozen layer names.
            // Requested handle membership and local ownership are checked independently.
            expected = expected.Take(30).Concat(actual.Skip(30)).ToArray();
        }
        RequireSame(expected, actual,check);
    }
}
