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
internal static class SheetMetalFixture
{
    internal const string CommandName="NCMCP_SHEETMETAL_FIXTURE";
    private const string Run=@"C:\NanoCAD MCP\artifacts\phase10-sheet-fixture-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase10-sheet-fixture-native\sheet-fixture.dxf";
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
            File.Move(Path.Combine(Run,"plan.json"),Path.Combine(Run,"plan.consumed.json"));
            var expected=new List<object>();
            foreach(bool open in new[]{false,true})
            {
                Log(open?"creating_open_sheet":"creating_closed_sheet");
                var sheet=Mc3dSolid.SmCreateSheetSolid();
                Need(sheet!=null,"Sheet metal feature unavailable");
                var solid=sheet!.Solid;McObjectManager.Add2Document(solid.DbEntity);
                var sketch=solid.AddPlanarSketch();sketch.DbEntity.Visibility=0;McObjectManager.Add2Document(sketch.DbEntity);
                var geometry=open?new Polyline3d(new List<Point3d>{new(1000,0,0),new(1100,0,0)}):Polyline3d.CreateRectangle(new Point3d(0,0,0),new Point3d(300,200,0));
                sketch.AddGeometry(McObjectId.NewID(),new EntityGeometry(geometry));
                var profile=sketch.CreateProfile();McObjectManager.Add2Document(profile.DbEntity);profile.DbEntity.Visibility=0;profile.AutoProcessExternalContours();
                sheet.ProfileID=profile.ID;sheet.ExtrusionType=SheetSolidExtrusionType.Both;McObjectManager.UpdateAll();
                using var variables=solid.DbEntity.Document.GetDocumentVariablesManager(Multicad.DatabaseServices.Constraints.Constraint2dNetType.MCS);
                Need(variables!=null,"MCS unavailable");
                variables!.SetVariableExpression(sheet.Dist1VarID,open?"20":"2");variables.SetVariableExpression(sheet.Dist2VarID,open?"30":"3");
                if(open){variables.SetVariableExpression(sheet.ThicknessVarID,"3");variables.SetVariableExpression(sheet.RadiusVarID,"12");}
                McObjectManager.UpdateAll();
                expected.Add(new{open_profile=open,body_id=solid.ID.ToString(),feature_id=sheet.FeatureEnt.ID.ToString(),profile_id=profile.ID.ToString(),
                    distance1=open?20:2,distance2=open?30:3,thickness=open?(double?)3:null,radius=open?(double?)12:null});
            }
            Log("created",new{expected});
        }
        catch(System.Exception e){Log("fixture_failed_unknown",new{error=e.ToString()});}
        finally{Pending=false;}
    }
}
