using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class BlockMoveTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Check(bool ok){if(!ok)throw new Exception("Independent block move expectation failed.");}
        void Deny(Action action){try{action();}catch(DomainException){return;}throw new Exception("Unsafe block edit accepted.");}
        await test("block move geometry keeps nonzero base and scale with ordered world transforms",()=>{
            var p=new BlockPlacement(new(10,20),new(100,200),Math.PI/2,2);
            var move=new MoveSpec(new[]{"A"},new(5,-10));var rotate=new RotateSpec(new[]{"A"},new(5,10),Math.PI/2);
            var result=BlockMoveGeometry.Transform(BlockMoveGeometry.Transform(p,move),rotate);
            Check(BlockMoveGeometry.Close(result.Position.X,-175)&&BlockMoveGeometry.Close(result.Position.Y,110)&&BlockMoveGeometry.Close(result.Rotation,Math.PI)&&result.BasePoint==p.BasePoint&&result.Scale==2);
            var local=new Point2(13,24);var direct=BlockMoveGeometry.Transform(BlockMoveGeometry.Transform(p.Transform(local),move),rotate);var composed=result.Transform(local);
            Check(BlockMoveGeometry.Close(direct.X,composed.X)&&BlockMoveGeometry.Close(direct.Y,composed.Y));
            Check(BlockMoveGeometry.Transform(BlockMoveGeometry.Transform(p,rotate),move).Position!=result.Position);
            return Task.CompletedTask;
        });
        await test("block move bounds reject unsupported edits overflow and excessive owned content",()=>{
            var move=new MoveSpec(new[]{"A"},new(1,0));
            Deny(()=>BlockMoveGeometry.Transform(new Point2(1e6,0),move));
            Deny(()=>BlockMoveGeometry.Transform(new Point2(0,0),new CopySpec(new[]{"A"},new(1,0))));
            Deny(()=>BlockMoveGeometry.Transform(new Point2(0,0),new RotateSpec(new[]{"A"},new(double.NaN,0),1)));
            Deny(()=>BlockMoveGeometry.Transform(new BlockPlacement(new(0,0),new(0,0),0,-1),move));
            BlockMoveGeometry.Budget(9,65,10001);Deny(()=>BlockMoveGeometry.Budget(-1,0,0));Deny(()=>BlockMoveGeometry.Budget(1,-1,0));Deny(()=>BlockMoveGeometry.Budget(1,0,-1));
            Check(!BlockMoveGeometry.Close(double.NaN,0)&&!BlockMoveGeometry.Close(100.001,100));return Task.CompletedTask;
        });
        await test("block move property preservation allows rounding but rejects actual style text or scale changes",()=>{
            var before=Wire.Element(new{height=4.0,scale=.5,text="ID",style="AF",normal=new[]{0,0,1.0}});
            Check(BlockMoveGeometry.SameProperties(before,Wire.Element(new{height=4.000000000000001,scale=.5,text="ID",style="AF",normal=new[]{0,0,1.0}})));
            foreach(var after in new[]{Wire.Element(new{height=4.0,scale=.5000001,text="ID",style="AF",normal=new[]{0,0,1.0}}),
                Wire.Element(new{height=4.0,scale=.5,text="CHANGED",style="AF",normal=new[]{0,0,1.0}}),Wire.Element(new{height=4.0,scale=.5,text="ID",style="B0",normal=new[]{0,0,1.0}})})Check(!BlockMoveGeometry.SameProperties(before,after));
            return Task.CompletedTask;
        });
        await test("block move capability retains existing tool and operation catalog",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            Check(ToolCatalog.Tools.Select(t=>t.Name).Order().SequenceEqual(ToolCatalog.OperationNames.Append("get_guidance").Select(op=>"nanocad."+op).Order())
                && !string.IsNullOrWhiteSpace(tool.Description));
            var variants=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf");
            Check(variants.EnumerateArray().Count(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString() is "move" or "rotate")==2);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
