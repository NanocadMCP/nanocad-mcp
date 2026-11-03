using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class CameraChangeFailureProbe
{
    private const string Root=@"C:\NanoCAD MCP\artifacts\core-camera-change-native-105v4";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-camera-change-native-105v4\camera.dxf";
    private static bool _consumed;
    internal static void Observe(string bridge,DocumentState state,CameraChangeArguments args)
    {
        string path=Path.Combine(Root,"failure-plan.json");
        if(_consumed||!File.Exists(path)||!string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return;
        string text=File.ReadAllText(path);if(text.Length>4096)throw new InvalidOperationException("Excessive camera fault plan.");
        using var document=JsonDocument.Parse(text);var p=document.RootElement;
        if(p.GetProperty("operation_id").GetString()!=args.OperationId)return;
        using var process=Process.GetCurrentProcess();var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=bridge||p.GetProperty("document_id").GetString()!=state.Id||
            p.GetProperty("process_id").GetInt32()!=process.Id||p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks||
            expiry<=DateTimeOffset.UtcNow||expiry>DateTimeOffset.UtcNow.AddMinutes(10))throw new InvalidOperationException("Camera fault lifetime mismatch.");
        _consumed=true;File.Move(path,path+".consumed");
        throw new DomainException("CAMERA_TEST_FAILURE","Intentional fault after successful native camera readback.");
    }
}
