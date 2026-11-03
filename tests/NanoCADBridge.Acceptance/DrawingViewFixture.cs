using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Diagnostic build only. One fixed owned file and immutable short-lived plan.
internal static class DrawingViewFixture
{
    internal const string CommandName="NCMCP_DRAWINGVIEW_FIXTURE";
    private const string Run=@"C:\NanoCAD MCP\artifacts\phase9-view-fixture-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase9-view-fixture-native\spider-view-fixture.dwg";
    private const string Source=@"C:\NanoCAD MCP\tests\fixtures\phase7-multicad-spider.dwg";
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
        Need(Convert.ToHexString(sha.ComputeHash(file))==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Source))),"Fixture bytes differ from original Spider");
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
            Validate(descriptor,true);
            var active=Multicad.DatabaseServices.McDocument.ActiveDocument;
            Need(active!=null && string.Equals(Path.GetFullPath(active.Path),Target,StringComparison.OrdinalIgnoreCase),"Wrong MultiCAD document");
            var sourceId=McObjectId.FromHandle(0x150,active!.ID);var solid=sourceId.GetObjectOfType<Mc3dSolid>();
            Need(solid!=null && solid.ID==sourceId && solid.GetPartContents(false).Count==54,"Unexpected source body");
            File.Move(Path.Combine(Run,"plan.json"),Path.Combine(Run,"plan.consumed.json"));
            Log("creating_front");
            var front=new DrawingView();front.DirectionType=DrawingView.ViewDirectionType.kViewFront;front.SetIsSection(false);
            front.Set3dEntsIds(new List<McObjectId>{sourceId},true);
            front.ProjPlane=new Plane3d(new Point3d(0,0,0),Vector3d.XAxis);
            front.ViewPlane=new Plane3d(new Point3d(3000,0,0),Vector3d.XAxis,Vector3d.YAxis);
            front.DbEntity.AddToCurrentDocument();front.Name="MCP Fixture Front";
            front.DbEntity.DScale=0.5;front.DbEntity.Scale=1;
            Log("creating_top");
            var top=front.CreateChildView(DrawingView.ViewDirectionType.kViewTop,new Plane3d(new Point3d(3000,2000,0),Vector3d.XAxis,Vector3d.YAxis));
            top.DbEntity.AddToCurrentDocument();top.Name="MCP Fixture Top";
            Log("creating_section");
            var plane=new WorkPlane();plane.SetFixedDefinition(new Plane3d(new Point3d(0,0,0),Vector3d.XAxis));plane.DbEntity.AddToCurrentDocument();
            var section=new Section();section.DbEntity.AddToCurrentDocument();section.AssociatedPlanarObjectID=plane.ID;section.DbEntity.Update();
            var cut=new DrawingView();cut.SectionID=section.ID;cut.SetIsSection(true);cut.DirectionType=DrawingView.ViewDirectionType.kViewFront;
            cut.ViewPlane=new Plane3d(new Point3d(5000,0,0),Vector3d.XAxis,Vector3d.YAxis);cut.DbEntity.AddToCurrentDocument();cut.Name="MCP Fixture Section";
            Log("update_before");McObjectManager.UpdateAll();
            Log("created",new{source_id=sourceId.ToString(),front_id=front.ID.ToString(),top_id=top.ID.ToString(),section_view_id=cut.ID.ToString(),section_id=section.ID.ToString()});
        }
        catch(System.Exception e){Log("fixture_failed_unknown",new{error=e.ToString()});}
        finally{Pending=false;}
    }
}
