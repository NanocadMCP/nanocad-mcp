using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Compile-time acceptance hook, absent from ordinary builds. It can only fail
// one preselected operation after mutation in the fixed owned test drawing.
internal static class ModelParameterFailureProbe
{
    private const string Plan = @"C:\NanoCAD MCP\artifacts\core-parameter-complex-native\failure-plan.json";
    private const string Target = @"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-parameter-complex-native\spider-working.dwg";
    private static bool _consumed;
    public static void Check(ModelParameterArguments args, DocumentState state)
    {
        if (_consumed || !string.Equals(Path.GetFullPath(state.Document.Name), Target, StringComparison.OrdinalIgnoreCase) || !File.Exists(Plan)) return;
        var text = File.ReadAllText(Plan);
        if (text.Length > 4096) throw new InvalidOperationException("Acceptance failure plan too large.");
        using var json = JsonDocument.Parse(text); var p = json.RootElement;
        if (p.GetProperty("operation_id").GetString() != args.OperationId) return;
        using var process = Process.GetCurrentProcess();
        if (p.GetProperty("bridge_instance_id").GetString() != args.BridgeId || p.GetProperty("document_id").GetString() != args.DocumentId ||
            p.GetProperty("process_id").GetInt32() != process.Id || p.GetProperty("process_start_utc_ticks").GetInt64() != process.StartTime.ToUniversalTime().Ticks ||
            p.GetProperty("expires_at_utc").GetDateTimeOffset() <= DateTimeOffset.UtcNow || p.GetProperty("expires_at_utc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(10) ||
            p.GetProperty("handle").GetString()!=args.Handle || p.GetProperty("variable_id").GetString()!=args.VariableId ||
            p.GetProperty("expected_expression").GetString()!=args.ExpectedExpression || p.GetProperty("value").GetDouble()!=args.Value)
            throw new InvalidOperationException("Acceptance failure plan does not match the active operation.");
        _consumed = true;
        File.Move(Plan, Plan + ".consumed");
        throw new DomainException("MODEL_ACCEPTANCE_FAILURE", "Controlled acceptance failure after variable update and model rebuild.");
    }
}
