using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CurveSplineSpec408(string SourceHandle, string LayerName) : EntitySpec("Spline", LayerName)
{
    public string Construction => "native_curve";
}

public static partial class CreateValidation
{
    internal static CurveSplineSpec408 ParseCurveSpline408(JsonElement item, string path, string layer)
    {
        Shape(item, path, "type", "layer", "construction", "source_handle");
        if (Text(item, "construction", path, 16) != "native_curve")
            throw Invalid(path + ".construction", "Expected native_curve construction.");
        string handle = Text(item, "source_handle", path, 16);
        if (handle.Any(c => !Uri.IsHexDigit(c)))
            throw Invalid(path + ".source_handle", "Expected a native hexadecimal Arc/Ellipse handle.");
        return new(Validation.NormalizeHandle(handle), layer);
    }
}
