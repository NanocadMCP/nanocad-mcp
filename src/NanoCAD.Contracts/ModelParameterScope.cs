using System.Text.Json;
namespace NanoCAD.Contracts;

public static class ModelParameterScope
{
    public static string[]? Parse(JsonElement input,string primary)
    {
        if(!input.TryGetProperty("scope_handles",out var scope))return null;
        void Need(bool ok){if(!ok)throw new DomainException("INVALID_ARGUMENT","scope_handles must explicitly list distinct body handles including the primary handle.");}
        Need(scope.ValueKind==JsonValueKind.Array&&scope.GetArrayLength()>=1);
        var values=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in scope.EnumerateArray())
        {
            Need(item.ValueKind==JsonValueKind.String);var value=Validation.NormalizeHandle(item.GetString()!);
            Need(seen.Add(value));values.Add(value);
        }
        Need(values.Contains(primary,StringComparer.OrdinalIgnoreCase));return values.ToArray();
    }
}
