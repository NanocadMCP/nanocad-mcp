using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using CadApplication=HostMgd.ApplicationServices.Application;

[assembly:CommandClass(typeof(NanoCADBridge.Acceptance.BlockMultilineFixture442))]
[assembly:ExtensionApplication(typeof(NanoCADBridge.Acceptance.BlockMultilineFixture442))]

namespace NanoCADBridge.Acceptance;

// Separate SDK-only seed assembly. This is not a product MCP operation and it
// exercises no product editor. Root loads/invokes its one command during the
// owned bootstrap startup, then tests actual placement only through MCP.
public sealed class BlockMultilineFixture442:IExtensionApplication
{
    private const string Bootstrap=@"C:\NanoCAD MCP\artifacts\day-20261021-annotations442\bootstrap.dxf";
    private const string Output=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\annotations442-seed\block-multiline442.dwg";
    private const string Evidence=@"C:\NanoCAD MCP\artifacts\annotations442";
    private static bool attempted;
    private static bool initialized;
    public void Initialize()
    {
        if(initialized)return;initialized=true;
        // Test-start loads this explicit module only for the owned bootstrap.
        // Wait for the first idle, detach before doing any native work, then
        // acquire the actual document lock. No input automation/queued command.
        CadApplication.Idle+=SeedAtIdle;
    }
    public void Terminate()=>CadApplication.Idle-=SeedAtIdle;
    private static void SeedAtIdle(object? sender,EventArgs args)
    {
        CadApplication.Idle-=SeedAtIdle;
        var document=CadApplication.DocumentManager.MdiActiveDocument;
        try
        {
            Need(document!=null&&!document.IsReadOnly&&string.Equals(Path.GetFullPath(document.Name),Bootstrap,StringComparison.OrdinalIgnoreCase),
                "Startup fixture refused: active document is not the exact owned bootstrap.");
            using var locked=document.LockDocument();new BlockMultilineFixture442().Seed();
        }
        catch(System.Exception error)
        {
            // Seed records started/failed itself once writing begins. This
            // receipt also makes a startup guard/locking refusal observable.
            if(Directory.Exists(Evidence)&&!File.Exists(Path.Combine(Evidence,"block-fixture-startup-failed442.json")))
                SaveReceipt("block-fixture-startup-failed442.json",new{bootstrap=Bootstrap,active_document=document?.Name,attempted,error=error.ToString(),at_utc=DateTimeOffset.UtcNow});
        }
    }
    private static void Need([DoesNotReturnIf(false)]bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static MText Borrow(DBText source)
    {
        var value=source is AttributeReference a?a.MTextAttribute:((AttributeDefinition)source).MTextAttributeDefinition;
        if(value==null||value.AutoDelete||value.UnmanagedObject==IntPtr.Zero)
        {if(value!=null)GC.SuppressFinalize(value);throw new InvalidOperationException("Unproved native embedded MText ownership.");}
        return value;
    }
    private static void Release(MText text)
    {if(text.AutoDelete){GC.SuppressFinalize(text);throw new InvalidOperationException("Embedded ownership changed.");}text.Dispose();}
    private static object Multiline(DBText source)
    {
        var text=Borrow(source);
        try{return new{contents=text.Contents,position=new[]{text.Location.X,text.Location.Y,text.Location.Z},direction=new[]{text.Direction.X,text.Direction.Y,text.Direction.Z},
            normal=new[]{text.Normal.X,text.Normal.Y,text.Normal.Z},text.TextHeight,text.Width,attachment=text.Attachment.ToString(),style=text.TextStyleId.Handle.ToString(),
            text.BackgroundFill,text.UseBackgroundColor,text.BackgroundScaleFactor,column_type=text.ColumnType.ToString()};}
        finally{Release(text);GC.KeepAlive(source);}
    }
    private static void ConfigureMultiline(DBText source,string contents)
    {
        var text=Borrow(source);
        try{text.Contents=contents;text.Width=32;text.TextHeight=3;text.Attachment=AttachmentPoint.TopLeft;text.BackgroundFill=true;text.UseBackgroundColor=true;text.BackgroundScaleFactor=1.1;}
        finally{Release(text);GC.KeepAlive(source);}
        if(source is AttributeReference a)a.UpdateMTextAttribute();else ((AttributeDefinition)source).UpdateMTextAttributeDefinition();
    }
    private static void SaveReceipt(string name,object value)
    {using var file=new FileStream(Path.Combine(Evidence,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(file,value,new JsonSerializerOptions{WriteIndented=true});}
    [CommandMethod("NCMCP_SEED_BLOCK_MULTILINE442",CommandFlags.Modal)]
    public void Seed()
    {
        var document=CadApplication.DocumentManager.MdiActiveDocument;
        Need(!attempted,"Fixture seed is single-use; no silent retries.");
        Need(document!=null&&!document.IsReadOnly&&string.Equals(Path.GetFullPath(document.Name),Bootstrap,StringComparison.OrdinalIgnoreCase),
            "Fixture seed may write only the exact annotations442 bootstrap, never a user drawing.");
        Need(!File.Exists(Output)&&!File.Exists(Path.Combine(Evidence,"block-fixture-started442.json"))&&!File.Exists(Path.Combine(Evidence,"block-fixture-ready442.json")),
            "Fixture output/evidence already exists; retained evidence is never replaced.");
        Need(Directory.Exists(Path.GetDirectoryName(Output)!)&&Directory.Exists(Evidence),"Root must prepare the explicit fixture output/evidence directories.");
        attempted=true;bool committed=false;
        SaveReceipt("block-fixture-started442.json",new{bootstrap=Bootstrap,output=Output,process_id=Environment.ProcessId,at_utc=DateTimeOffset.UtcNow});
        try
        {
            var database=document.Database;object receipt;
            database.Insunits=UnitsValue.Millimeters;
            using(var transaction=database.TransactionManager.StartTransaction())
            {
                var blocks=(BlockTable)transaction.GetObject(database.BlockTableId,OpenMode.ForWrite);
                Need(!blocks.Has("NCMCP_MULTILINE442"),"Fixture block already exists; no duplicate seed.");
                var definition=new BlockTableRecord{Name="NCMCP_MULTILINE442",Origin=Point3d.Origin,Units=UnitsValue.Millimeters};
                blocks.Add(definition);transaction.AddNewlyCreatedDBObject(definition,true);
                var outline=new Polyline();for(int i=0;i<4;i++)outline.AddVertexAt(i,new Point2d(i is 1 or 2?40:0,i>=2?20:0),0,0,0);outline.Closed=true;
                definition.AppendEntity(outline);transaction.AddNewlyCreatedDBObject(outline,true);
                var multiline=new AttributeDefinition{Tag="MULTILINE",Prompt="Multiline fixture",TextString="Default",Height=3,Position=new Point3d(3,15,0),TextStyleId=database.Textstyle};
                definition.AppendEntity(multiline);transaction.AddNewlyCreatedDBObject(multiline,true);
                // SDK managed property calls native convertIntoMTextAttributeDefinition.
                // It and Update execute only after the definition is resident.
                multiline.IsMTextAttributeDefinition=true;ConfigureMultiline(multiline,@"{\C1;Default}\PSecond line");
                var single=new AttributeDefinition{Tag="SINGLE",Prompt="Single fixture",TextString="Single default",Height=2.5,Position=new Point3d(3,4,0),TextStyleId=database.Textstyle};
                definition.AppendEntity(single);transaction.AddNewlyCreatedDBObject(single,true);
                var model=(BlockTableRecord)transaction.GetObject(blocks[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
                var reference=new BlockReference(new Point3d(100,40,0),definition.ObjectId);model.AppendEntity(reference);transaction.AddNewlyCreatedDBObject(reference,true);
                var attributes=new List<AttributeReference>();
                foreach(var template in new[]{multiline,single})
                {
                    var attribute=new AttributeReference();reference.AttributeCollection.AppendAttribute(attribute);transaction.AddNewlyCreatedDBObject(attribute,true);
                    attribute.SetAttributeFromBlock(template,reference.BlockTransform);
                    if(template.IsMTextAttributeDefinition){if(!attribute.IsMTextAttribute)attribute.IsMTextAttribute=true;ConfigureMultiline(attribute,@"{\C3;M442}\PFull second line");}
                    else attribute.TextString="Keep single";
                    attributes.Add(attribute);
                }
                Need(multiline.IsMTextAttributeDefinition&&attributes[0].IsMTextAttribute&&attributes.All(a=>a.OwnerId==reference.ObjectId),"Native fixture identities/types differ.");
                receipt=new{bootstrap=Bootstrap,output=Output,definition_handle=definition.Handle.ToString(),block_handle=reference.Handle.ToString(),
                    attribute_handles=attributes.Select(a=>new{handle=a.Handle.ToString(),a.Tag,a.IsMTextAttribute,owner=a.OwnerId.Handle.ToString()}).ToArray(),
                    definition_multiline=Multiline(multiline),occurrence_multiline=Multiline(attributes[0]),scope="seed-only native static block with actual resident multiline definition/occurrence and a single-line peer"};
                transaction.Commit();committed=true;
            }
            database.SaveAs(Output,DwgVersion.Current);
            SaveReceipt("block-fixture-ready442.json",new{saved=true,committed,receipt,at_utc=DateTimeOffset.UtcNow});
        }
        catch(System.Exception error)
        {SaveReceipt("block-fixture-failed442.json",new{committed,output=Output,error=error.ToString(),at_utc=DateTimeOffset.UtcNow,warning="No success/rollback is inferred; inspect retained bootstrap/output."});throw;}
    }
}
