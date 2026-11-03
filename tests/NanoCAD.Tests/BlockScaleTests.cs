using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class BlockScaleTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Check(bool ok){if(!ok)throw new Exception("Independent block scale expectation failed.");}
        void Deny(Action action){try{action();}catch(DomainException e) when(e.Error.code=="INVALID_GEOMETRY"){return;}throw new Exception("Unsafe scale accepted.");}
        await test("block scale composes world scale move and rotation with base point and existing scale",()=>{
            var source=new BlockPlacement(new(10,20),new(100,200),Math.PI/2,2);
            var scale=new ScaleSpec(new[]{"A"},new(20,40),.25);var move=new MoveSpec(new[]{"A"},new(5,-10));var rotate=new RotateSpec(new[]{"A"},new(0,0),Math.PI/2);
            var result=BlockMoveGeometry.Transform(source,scale);
            Check(result.Position==new Point2(40,80)&&result.Scale==.5&&result.BasePoint==source.BasePoint&&result.Rotation==source.Rotation);
            var local=new Point2(13,24);var direct=source.Transform(local);
            foreach(var op in new EditSpec[]{scale,move,rotate})direct=BlockMoveGeometry.Transform(direct,op);
            result=BlockMoveGeometry.Transform(BlockMoveGeometry.Transform(result,move),rotate);var composed=result.Transform(local);
            Check(BlockMoveGeometry.Close(direct.X,composed.X)&&BlockMoveGeometry.Close(direct.Y,composed.Y)&&result.Scale==.5);
            Check(BlockMoveGeometry.Transform(BlockMoveGeometry.Transform(source,move),scale).Position!=BlockMoveGeometry.Transform(BlockMoveGeometry.Transform(source,scale),move).Position);
            return Task.CompletedTask;
        });
        await test("block scale rejects invalid factors cumulative scales coordinates and attribute height",()=>{
            Check(BlockMoveGeometry.UniformScale(1.5,1.5000000000000002,1.5));
            Check(!BlockMoveGeometry.UniformScale(1.5,1.50000001,1.5)&&!BlockMoveGeometry.UniformScale(1e-6,1.000001e-6,1e-6)&&!BlockMoveGeometry.UniformScale(1,1,-1)&&!BlockMoveGeometry.UniformScale(double.NaN,1,1));
            foreach(double factor in new[]{0,-1,1e-7,1e7,double.NaN,double.PositiveInfinity})Deny(()=>BlockMoveGeometry.Transform(new Point2(1,1),new ScaleSpec(new[]{"A"},new(0,0),factor)));
            Deny(()=>BlockMoveGeometry.Scale(2,new ScaleSpec(new[]{"A"},new(0,0),1e6)));
            Deny(()=>BlockMoveGeometry.Scale(.1,new ScaleSpec(new[]{"A"},new(0,0),1e-6)));
            Deny(()=>BlockMoveGeometry.Transform(new Point2(1e6,0),new ScaleSpec(new[]{"A"},new(0,0),2)));
            foreach(double height in new[]{1e-9,1e9})Deny(()=>BlockMoveGeometry.PropertiesAfter(Wire.Element(new{Height=height}),new ScaleSpec(new[]{"A"},new(0,0),height<1?.1:10),true));
            return Task.CompletedTask;
        });
        await test("block scale predicts only scale and attribute height retaining all other properties",()=>{
            var scale=new ScaleSpec(new[]{"A"},new(0,0),2);
            var root=Wire.Element(new{scale=new[]{.5,.5,.5},normal=new[]{0,0,1},definition="A",layer="B",color=7});
            Check(BlockMoveGeometry.SameProperties(BlockMoveGeometry.PropertiesAfter(root,scale,false),Wire.Element(new{scale=new[]{1,1,1},normal=new[]{0,0,1},definition="A",layer="B",color=7})));
            var a=Wire.Element(new{Height=3,Tag="ID",TextString="Text",WidthFactor=1,style="A",normal=new[]{0,0,1}});
            var expected=BlockMoveGeometry.PropertiesAfter(a,scale,true);
            Check(BlockMoveGeometry.SameProperties(expected,Wire.Element(new{Height=6,Tag="ID",TextString="Text",WidthFactor=1,style="A",normal=new[]{0,0,1}})));
            Check(!BlockMoveGeometry.SameProperties(expected,Wire.Element(new{Height=6,Tag="ID",TextString="Text",WidthFactor=2,style="A",normal=new[]{0,0,1}})));
            Check(BlockMoveGeometry.SameProperties(a,BlockMoveGeometry.PropertiesAfter(a,new MoveSpec(new[]{"A"},new(1,1)),true)));
            return Task.CompletedTask;
        });
        await test("block scale remains in the existing scale operation and bounded catalog",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            Check(ToolCatalog.Tools.Count==ToolCatalog.OperationNames.Length&&tool.Description!.Contains("move/rotate/uniform scale")&&tool.Description.Contains("1e-6..1e6"));
            var variant=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="scale");
            Check(variant.GetProperty("properties").GetProperty("factor").GetProperty("minimum").GetDouble()==1e-6);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}
