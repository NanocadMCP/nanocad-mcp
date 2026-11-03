using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using HostMgd.PointClouds;

using NanoCAD.Contracts;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Diagnostic build only. One fixed owned file and immutable short-lived plan.
internal static class PointCloudFixture
{
    internal const string CommandName="NCMCP_POINTCLOUD_FIXTURE";
    private const string Run=@"C:\NanoCAD MCP\artifacts\phase10-cloud-fixture-native-v2";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase10-cloud-fixture-native-v2\cloud-fixture.dxf";
    private const string Source=@"C:\NanoCAD MCP\tests\fixtures\phase1-empty.dxf";
    private static bool _consumed;
    private static string? _plan;
    public static bool Pending{get;private set;}
    private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
    private static void Log(string stage,object? details=null)
    {
        using var stream=new FileStream(Path.Combine(Run,"fixture.jsonl"),FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);
        using var writer=new StreamWriter(stream);writer.WriteLine(JsonSerializer.Serialize(new{utc=DateTime.UtcNow.ToString("O"),stage,details},Wire.Json));
    }
    private static Document Validate(Descriptor? descriptor,bool insideCommand)
    {
        Need(descriptor!=null && _plan!=null && _plan.Length<4096,"Bound plan required");
        Need(File.ReadAllText(Path.Combine(Run,"plan.json"))==_plan,"Plan changed");
        using var parsed=JsonDocument.Parse(_plan!);var p=parsed.RootElement;
        using var process=Process.GetCurrentProcess();
        Need(p.GetProperty("bridge_instance_id").GetString()==descriptor!.bridge_instance_id && p.GetProperty("process_id").GetInt32()==process.Id &&
            p.GetProperty("process_start_utc_ticks").GetInt64()==process.StartTime.ToUniversalTime().Ticks,"Wrong bridge lifetime");
        var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        Need(expiry>DateTimeOffset.UtcNow && expiry<DateTimeOffset.UtcNow.AddMinutes(10),"Expired or excessive fixture window");
        var doc=Application.DocumentManager.MdiActiveDocument;
        Need(doc!=null && doc.IsNamedDrawing && !doc.IsReadOnly && string.Equals(Path.GetFullPath(doc.Name),Target,StringComparison.OrdinalIgnoreCase),"Wrong active fixture");
        Need(insideCommand || !HostReadGate.IsBusy(doc!),"Host busy");
        for(var parent=new DirectoryInfo(Path.GetDirectoryName(Target)!);parent!=null;parent=parent.Parent)
            Need((parent.Attributes&FileAttributes.ReparsePoint)==0,"Reparse fixture directory");
        Need((File.GetAttributes(Target)&FileAttributes.ReparsePoint)==0,"Reparse fixture");
        using var file=File.Open(Target,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        using var sha=SHA256.Create();
        Need(Convert.ToHexString(sha.ComputeHash(file))==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Source))),"Fixture bytes differ from fixed empty source");
        var cloud=Path.Combine(Run,"fixture.pcd");
        Need((File.GetAttributes(cloud)&FileAttributes.ReparsePoint)==0,"Reparse PCD source");
        for(var parent=new DirectoryInfo(Run);parent!=null;parent=parent.Parent)
            Need((parent.Attributes&FileAttributes.ReparsePoint)==0,"Reparse PCD directory");
        Need(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cloud)))==p.GetProperty("pcd_sha256").GetString(),"PCD bytes changed after preflight");
        return doc!;
    }
    public static void Pump(Descriptor descriptor)
    {
        if(_consumed || !File.Exists(Path.Combine(Run,"plan.json")))return;
        _consumed=true;
        try
        {
            _plan=File.ReadAllText(Path.Combine(Run,"plan.json"));var doc=Validate(descriptor,false);
            Pending=true;Log("scheduled",new{descriptor.bridge_instance_id,descriptor.process_id});
            doc.SendStringToExecute(CommandName+"\n",false,false,false);
        }
        catch(System.Exception e){Pending=false;Log("schedule_failed",new{error=e.ToString()});}
    }
    public static void Execute(Descriptor? descriptor)
    {
        if(!Pending)return;
        try
        {
            var doc=Validate(descriptor,true);
            File.Move(Path.Combine(Run,"plan.json"),Path.Combine(Run,"plan.consumed.json"));
            Log("import_starting",new{source=Path.Combine(Run,"fixture.pcd")});
            using var ids=new ObjectIdCollection();
            bool imported=PointCloudBridgeMdg.createPointCloudFromFile(doc.UnmanagedObject,Path.Combine(Run,"fixture.pcd"),ids.UnmanagedObject,false);
            Need(imported && ids.Count==1,"PCD import did not return one insertion");
            var id=ids[0];
            Need(!id.IsNull && id.IsValid && id.Database==doc.Database && PointCloudBridgeMdg.isPointCloudInsertion(id.OldIdPtr),"Imported insertion identity mismatch");
            Log("imported",new{handle=id.Handle.ToString()});
            using(var tx=doc.Database.TransactionManager.StartTransaction())
            {
                var entity=tx.GetObject(id,OpenMode.ForWrite) as Entity;
                Need(entity!=null,"Imported insertion is not an Entity");
                entity!.TransformBy(Matrix3d.Displacement(new Vector3d(100,200,300))*Matrix3d.Rotation(Math.PI/2,Vector3d.ZAxis,Point3d.Origin));
                tx.Commit();
            }
            Log("created",new{handle=id.Handle.ToString(),point_count="24",bounds=new{min=new[]{-11900,1200,3300},max=new[]{-1900,7200,10300}}});
        }
        catch(System.Exception e){Log("fixture_failed_unknown",new{error=e.ToString()});}
        finally{Pending=false;}
    }
}
