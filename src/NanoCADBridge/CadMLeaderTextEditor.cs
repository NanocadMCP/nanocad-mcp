using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadMLeaderTextEditor
{
    static void Need(bool yes,string message,string code="UNSUPPORTED_MLEADER_CONTENT")
    {if(!yes)throw new DomainException(code,message);}
    internal static JsonElement Capture(MLeader leader,Action cancel)
    {
        var transaction=leader.Database?.TransactionManager.TopTransaction;
        if(transaction!=null&&!leader.ObjectId.IsNull)CadMLeaderGeometry388.Ownership(leader,transaction,cancel);
        var geometry=CadMLeaderGeometry388.CaptureGeometry(leader,cancel);
        using var text=CadAnnotationTransformEditor.OwnedText(leader);
        Need(text!=null&&MLeaderTextLiteral.Supported(text.Contents,0,16384),"Formatted or Field-bearing text requires its content adapter.");
        var content=JsonNode.Parse(geometry.Content.GetRawText())!.AsObject();content.Remove("contents");
        var connections=new List<object>();var budget=new SerializedCapacity357(cancel:cancel);
        foreach(int branch in leader.GetLeaderIndexes())
        {
            cancel();var dogleg=leader.GetDogleg(branch);
            Need(leader.TextAttachmentDirection==TextAttachmentDirection.AttachmentHorizontal&&Math.Abs(dogleg.Length-1)<=1e-9&&Math.Abs(dogleg.Z)<=1e-9,
                "Literal editing retains ordinary horizontal native doglegs.");
            double[] P(Point3d p)=>[p.X,p.Y,p.Z];
            var landings=new List<double[]>();
            foreach(int line in leader.GetLeaderLineIndexes(branch))
            {cancel();var p=P(leader.GetLastVertex(line)+dogleg*leader.GetDoglegLength(branch));budget.Add(p);landings.Add(p);}
            var item=new{branch,point=P(leader.ConnectionPoint(-dogleg,leader.TextAttachmentDirection)),landing_connections=landings};budget.Add(item);connections.Add(item);
        }
        var result=Wire.Element(new{multi_line_snapshot=true,text=text!.Contents,content,lines=geometry.Lines,display=geometry.Display,layout=geometry.Layout,connections,
            metadata=transaction!=null?CadMLeaderGeometry388.Metadata(leader,transaction,cancel):default(JsonElement?)});
        SerializedCapacity357.Check(result,cancel:cancel);return result;
    }
    internal static void Apply(MLeader leader,ReplaceMLeaderTextSpec edit,Action cancel)
    {
        var before=Capture(leader,cancel);
        Need(before.GetProperty("text").GetString()==edit.ExpectedText,"MLeader text differs from expected_text.","TEXT_CONTENT_CHANGED");
        cancel();using(var text=CadAnnotationTransformEditor.OwnedText(leader)){Need(text!=null,"MLeader text unavailable.");text!.Contents=edit.Text;leader.MText=text;}
        var after=Capture(leader,cancel);MLeaderTextVerification.Verify(before,after,edit.Text);
    }
}
