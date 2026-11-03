using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Diagnostic build only. One fixed owned file and immutable short-lived plan.
internal static class DrawingViewWriteProbe
{
    internal const string CommandName="NCMCP_DRAWINGVIEW_WRITEPROBE";
    private const string Run=@"C:\NanoCAD MCP\artifacts\phase10-break-edit-probe-v3-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase10-break-edit-probe-v3-native\break-edit.dwg";
    private const string Source=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase10-break-read-native\saved.dwg";
    private static bool _consumed;
    private static string? _plan;
    // Product-path rollback injection. The private plan is tied to one request and
    // a separate owned fixture; there is no test field in the public protocol.
    public static void CheckProductFailure(StructureWriteArguments args,DocumentState state)
    {
        const string root=@"C:\NanoCAD MCP\artifacts\phase9-section-create-native";
        const string target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase9-section-create-native\create-section.dwg";
        var path=Path.Combine(root,"failure-plan.json");
        if(!File.Exists(path))return;
        var text=File.ReadAllText(path);Need(text.Length<=16384,"Excessive failure plan");
        using var plan=JsonDocument.Parse(text);var value=plan.RootElement;
        if(value.GetProperty("operation_id").GetString()!=args.OperationId)return;
        using var process=Process.GetCurrentProcess();
        Need(value.GetProperty("bridge_instance_id").GetString()==args.BridgeId && value.GetProperty("document_id").GetString()==state.Id &&
            value.GetProperty("process_id").GetInt32()==process.Id && value.GetProperty("process_start_utc_ticks").GetInt64()==process.StartTime.ToUniversalTime().Ticks &&
            StructureWriteValidation.Parse(value.GetProperty("request")).Fingerprint()==args.Fingerprint(),"Failure plan context mismatch");
        var expiry=value.GetProperty("expires_at_utc").GetDateTimeOffset();
        Need(expiry>DateTimeOffset.UtcNow && expiry<DateTimeOffset.UtcNow.AddMinutes(10) && Path.GetFullPath(state.Document.Name)==target,"Failure plan fixture/window mismatch");
        File.Move(path,Path.Combine(root,"failure-plan.consumed.json"));
        throw new DomainException("CONTROLLED_VIEW_FAILURE","Intentional failure after generating all requested drawing views.");
    }
    public static void RecordProductSnapshot(StructureWriteArguments args,DocumentState state,string phase,JsonElement snapshot)
    {
        const string target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase9-section-create-native\create-section.dwg";
        if(Path.GetFullPath(state.Document.Name)!=target)return;
        Need(phase is "before" or "changed" or "restored" or "geometry","Unknown snapshot phase");
        var path=Path.Combine(@"C:\NanoCAD MCP\artifacts\phase9-section-create-native",args.OperationId+"-"+phase+".json");
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        JsonSerializer.Serialize(file,snapshot,Wire.Json);
    }
    public static void RecordProductGeometry(StructureWriteArguments args,DocumentState state,DrawingView view,IEnumerable<EntityGeometry> geometry)
    {
        const string target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase9-section-create-native\create-section.dwg";
        if(Path.GetFullPath(state.Document.Name)!=target)return;
        double[] P(Point3d p)=>new[]{p.X,p.Y,p.Z};
        var curves=geometry.Select(g=>g.AsCurve()).Where(c=>c!=null).Select(c=>new{start=P(c!.StartPoint),end=P(c.EndPoint)}).ToArray();
        RecordProductSnapshot(args,state,"geometry",Wire.Element(new{center=P(view.CenterPointOf3dEnts),scale=view.DbEntity.DScale,curves}));
    }
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
        Need(Convert.ToHexString(sha.ComputeHash(file))==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Source))),"Fixture bytes differ from accepted sheet source");
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
            DrawingViewBreakEditProbe.Execute(doc,Log);
        }
        catch(System.Exception e){Log("fixture_failed_unknown",new{error=e.ToString()});}
        finally{Pending=false;}
    }
    private static Mc3dSolid SourceBody(Document doc)
    {
        var active=McDocument.ActiveDocument;
        Need(active!=null && string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(doc.Name),StringComparison.OrdinalIgnoreCase),"Wrong MultiCAD document");
        var id=McObjectId.FromHandle(0x71,active!.ID);var body=id.GetObjectOfType<Mc3dSolid>();
        Need(body!=null && body.ID==id,"Source sheet identity unavailable");return body!;
    }
    private static void CreateViews(McObjectId sourceId,string label)
    {
        Log("creating_front",new{label});
        var front=new DrawingView();front.DirectionType=DrawingView.ViewDirectionType.kViewFront;front.SetIsSection(false);
        front.Set3dEntsIds(new List<McObjectId>{sourceId},true);
        front.ProjPlane=new Plane3d(Point3d.Origin,Vector3d.XAxis);
        front.ViewPlane=new Plane3d(new Point3d(2000,0,0),Vector3d.XAxis,Vector3d.YAxis);
        front.DbEntity.AddToCurrentDocument();front.Name="MCP "+label+" Front";front.DbEntity.DScale=0.5;front.DbEntity.Scale=1;
        Log("creating_top",new{label});
        var top=front.CreateChildView(DrawingView.ViewDirectionType.kViewTop,new Plane3d(new Point3d(2000,1000,0),Vector3d.XAxis,Vector3d.YAxis));
        top.DbEntity.AddToCurrentDocument();top.Name="MCP "+label+" Top";
        Log("update_before",new{label});McObjectManager.UpdateAll();Log("update_returned",new{label});
        Need(top.ParentViewID==front.ID && front.ChildViews.Contains(top.ID),"View parent/child association mismatch");
        Need(front.GetTop3dEntitiesInView().Contains(sourceId),"Front lost its requested source");
        Log("geometry_before",new{label});
        var frontGeometry=front.GetViewGeometry(true,0);var topGeometry=top.GetViewGeometry(true,0);
        Need(frontGeometry!=null && topGeometry!=null && frontGeometry.Count>0 && topGeometry.Count>0,"Computed projection is empty");
        Log("projection_available",new{label,front_geometry_count=frontGeometry!.Count,top_geometry_count=topGeometry!.Count,source_id=sourceId.ToString(),front_id=front.ID.ToString(),top_id=top.ID.ToString()});
    }
    private static JsonElement Snapshot(Document doc)
    {
        var source=SourceBody(doc);var history=source.GetPartContents(false).Select(x=>x.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        object geometry;string[] handles;string[] blocks;var views=new List<object>();
        using(var tx=doc.Database.TransactionManager.StartTransaction())
        {
            var table=(Db.BlockTable)tx.GetObject(doc.Database.BlockTableId,Db.OpenMode.ForRead);
            Need(table.Cast<Db.ObjectId>().Count()<=256,"Unexpected block-table size");
            blocks=table.Cast<Db.ObjectId>().Where(id=>!id.IsErased).Select(id=>id.Handle.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var model=(Db.BlockTableRecord)tx.GetObject(table[Db.BlockTableRecord.ModelSpace],Db.OpenMode.ForRead);
            var ids=model.Cast<Db.ObjectId>().Where(id=>!id.IsErased && !id.IsEffectivelyErased).ToArray();Need(ids.Length<=256,"Unexpected model size");
            handles=ids.Select(id=>id.Handle.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            Need(doc.Database.TryGetObjectId(new Db.Handle(0x71),out var nativeSource),"Native source missing");
            var solid=(Db.Solid3d)tx.GetObject(nativeSource,Db.OpenMode.ForRead);var mass=solid.MassProperties;var bounds=solid.GeometricExtents;
            geometry=new{volume=mass.Volume,centroid=new[]{mass.Centroid.X,mass.Centroid.Y,mass.Centroid.Z},min=new[]{bounds.MinPoint.X,bounds.MinPoint.Y,bounds.MinPoint.Z},max=new[]{bounds.MaxPoint.X,bounds.MaxPoint.Y,bounds.MaxPoint.Z}};
            foreach(var id in ids)
            {
                var mcId=McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID);var view=mcId.GetObjectOfType<DrawingView>();if(view==null)continue;
                views.Add(new{handle=id.Handle.ToString(),mc_id=mcId.ToString(),name=view.Name,parent=view.ParentViewID.ToString(),children=view.ChildViews?.Select(x=>x.ToString()).ToArray()??Array.Empty<string>(),sources=view.GetTop3dEntitiesInView()?.Select(x=>x.ToString()).ToArray(),status=view.Status.ToString(),scale=view.DbEntity.DScale});
            }
        }
        return JsonSerializer.SerializeToElement(new{source=new{id=source.ID.ToString(),history,geometry},handles,blocks,views});
    }
    private static bool Equivalent(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object=>a.EnumerateObject().Count()==b.EnumerateObject().Count() && a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var other)&&Equivalent(p.Value,other)),
            JsonValueKind.Array=>a.GetArrayLength()==b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(pair=>Equivalent(pair.First,pair.Second)),
            JsonValueKind.Number=>Math.Abs(a.GetDouble()-b.GetDouble())<=1e-8*Math.Max(1,Math.Max(Math.Abs(a.GetDouble()),Math.Abs(b.GetDouble()))),
            _=>a.ToString()==b.ToString()
        };
    }
}

