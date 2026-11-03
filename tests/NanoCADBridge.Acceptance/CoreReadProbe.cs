using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// One-shot read-only test of native Measurement on owned detached clones.
internal static class CoreReadProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-dimension-measurement-native-100";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-dimension-measurement-native-100\dimension-measurement.dxf";
    private static bool _consumed;
    internal static void Observe(string bridge,DocumentState state,Request request,Transaction tr,ObjectId modelId,Action cancel)
    {
        string plan=Path.Combine(Run,"plan.json");
        if(_consumed||request.operation!="get_document"||!File.Exists(plan)||!string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return;
        var text=File.ReadAllText(plan);if(text.Length>4096)throw new InvalidOperationException("Excessive diagnostic plan.");
        using var parsed=JsonDocument.Parse(text);var p=parsed.RootElement;using var process=Process.GetCurrentProcess();
        var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=bridge||p.GetProperty("document_id").GetString()!=state.Id||p.GetProperty("process_id").GetInt32()!=process.Id||
            p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks||expiry<=DateTimeOffset.UtcNow||expiry>DateTimeOffset.UtcNow.AddMinutes(10))
            throw new InvalidOperationException("Diagnostic lifetime mismatch.");
        _consumed=true;File.Move(plan,plan+".consumed");
        var watch=Stopwatch.StartNew();
        void Check(){cancel();if(watch.ElapsedMilliseconds>2000)throw new InvalidOperationException("Diagnostic two-second limit.");}
        void Save(string name,object value)
        {using var f=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(f,value,Wire.Json);}
        foreach(string handle in new[]{"8A","99","A8","B2","BC","CD","DD"})
        {
            Check();var original=(Dimension)tr.GetObject(CadStructureWriter.Find(state.Document.Database,handle),OpenMode.ForRead);
            using var reactors=original.GetPersistentReactorIds();
            if(!DimensionGeometryReader.Supported(original.GetType())||original.OwnerId!=modelId||original.HasFields||reactors.Count!=0)
                throw new InvalidOperationException("Expected plain native model dimension without dependencies.");
            var before=Wire.Element(DimensionGeometryReader.Definition(original));
            Save(handle+"-before.json",new{type=original.GetType().Name,definition=before,source_write_enabled=original.IsWriteEnabled});
            using(var clone=(Dimension)original.Clone())
            {
                Save(handle+"-identity.json",new{object_id_null=clone.ObjectId.IsNull,database_null=clone.Database==null,
                    type=clone.GetType().Name,original_type=original.GetType().Name,is_new=clone.IsNewObject,
                    write_enabled=clone.IsWriteEnabled,source_write_enabled=original.IsWriteEnabled});
                if(!clone.ObjectId.IsNull||clone.GetType()!=original.GetType()||!clone.IsNewObject||!clone.IsWriteEnabled||original.IsWriteEnabled)
                    throw new InvalidOperationException("Expected exact writable new detached clone and read-only source.");
                var effectiveFactor=clone.Dimlfac;
                // Unit normalization applies only to this detached owned clone.
                clone.Dimlfac=1;
                Save(handle+"-factor.json",new{source_factor=original.Dimlfac,clone_factor_before=effectiveFactor,clone_factor_after=clone.Dimlfac});
                var cloneBefore=Wire.Element(DimensionGeometryReader.Definition(clone));
                double? measurement=null;string? status=null;
                try
                {
                    double value=clone.Measurement;
                    if(!double.IsFinite(value))throw new InvalidOperationException("Non-finite measurement.");
                    measurement=value;
                }
                catch(Teigha.Runtime.Exception e){status=e.ErrorStatus.ToString();}
                var cloneAfter=Wire.Element(DimensionGeometryReader.Definition(clone));
                Save(handle+"-result.json",new{measurement,native_status=status,clone_write_enabled=clone.IsWriteEnabled,
                    definition_before=cloneBefore,definition_after=cloneAfter});
            }
            if(!DimensionTextGeometry.SameDefinition(before,Wire.Element(DimensionGeometryReader.Definition(original))))
                throw new InvalidOperationException("Source dimension definition changed.");
        }
        Save("complete.json",new{observed=true,elapsed_ms=watch.ElapsedMilliseconds,count=7,scope="native getter on detached clones; source read-only audit still required"});
    }
}
