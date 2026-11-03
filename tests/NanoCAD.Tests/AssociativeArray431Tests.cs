using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class AssociativeArray431Tests
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Deny(Action action,string code)
    {try{action();}catch(DomainException error){Require(error.Error.code==code,error.Message);return;}throw new Exception("Invalid native array edit accepted.");}
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block431 native rectangular array preserves large host counts and explicit source intent",()=>
        {
            var value=Wire.Element(new{operation="create_associative_rectangular_array",handles=new[]{"A","B"},base_point=new[]{12d,18},columns=10000,rows=10000,column_spacing=5d,row_spacing=8d});
            var edit=(CreateAssociativeRectangularArray431)AssociativeArrayValidation431.Parse(value,"$",["A","B"],"create_associative_rectangular_array");
            Require(edit.Columns==10000&&edit.Rows==10000&&edit.Handles.Length==2&&edit.BasePoint.Y==18,"Native count/source/base point was lost or artificially limited.");
            var invalid=Wire.Element(new{operation="create_associative_rectangular_array",handles=new[]{"A"},base_point=new[]{0d,0},columns=0,rows=1,column_spacing=5d,row_spacing=8d});
            Deny(()=>AssociativeArrayValidation431.Parse(invalid,"$",["A"],"create_associative_rectangular_array"),"INVALID_ARGUMENT");
            return Task.CompletedTask;
        });
        await test("block431 native array edits require current token and do not fabricate omitted driver values",()=>
        {
            string token=new('A',64);var value=Wire.Element(new{operation="set_associative_rectangular_array",handles=new[]{"A"},array_token=token,column_spacing=12d});
            var edit=(SetAssociativeRectangularArray431)AssociativeArrayValidation431.Parse(value,"$",["A"],"set_associative_rectangular_array");
            Require(edit.Columns==null&&edit.Rows==null&&edit.RowSpacing==null&&edit.ColumnSpacing==12&&edit.ArrayToken==token,"Sparse field semantics lost.");
            var oriented=(SetAssociativeRectangularArray431)AssociativeArrayValidation431.Parse(Wire.Element(new{operation="set_associative_rectangular_array",handles=new[]{"A"},array_token=token,axes_angle_degrees=90d,position_wcs=new[]{125d,55,0}}),"$",["A"],"set_associative_rectangular_array");
            Require(oriented.Columns==null&&oriented.Rows==null&&oriented.ColumnSpacing==null&&oriented.AxesAngleDegrees==90&&oriented.PositionWcs==new Point3(125,55,0),"Native angle/placement-only edit must not fabricate count or spacing changes.");
            Deny(()=>AssociativeArrayValidation431.Parse(Wire.Element(new{operation="set_associative_rectangular_array",handles=new[]{"A"},array_token=token,axes_angle_degrees=180d}),"$",["A"],"set_associative_rectangular_array"),"INVALID_ARGUMENT");
            Deny(()=>AssociativeArrayValidation431.Parse(Wire.Element(new{operation="set_associative_rectangular_array",handles=new[]{"A"},array_token=token}),"$",["A"],"set_associative_rectangular_array"),"INVALID_ARGUMENT");
            Deny(()=>AssociativeArrayValidation431.Batch([edit,new EraseSpec(["B"])]),"INVALID_OPERATION_ORDER");
            return Task.CompletedTask;
        });
        await test("block441 native polar and path creation retain explicit placement and real path references",()=>
        {
            var polar=(CreateAssociativePolarArray441)AssociativeArrayValidation441.Parse(Wire.Element(new{operation="create_associative_polar_array",handles=new[]{"A","B"},base_point=new[]{0d,0},center=new[]{80d,40},radius=20d,start_angle_degrees=30d,count=int.MaxValue,angle_step_degrees=-12d,rotate_items=false}),"$",["A","B"],"create_associative_polar_array");
            Require(polar.Count==int.MaxValue&&polar.Center==new Point2(80,40)&&polar.BasePoint==new Point2(0,0)&&polar.Radius==20&&polar.StartAngleDegrees==30&&polar.AngleStepDegrees==-12&&!polar.RotateItems,"Polar native count, source anchor, intended placement or clockwise orientation was lost.");
            var path=(CreateAssociativePathArray441)AssociativeArrayValidation441.Parse(Wire.Element(new{operation="create_associative_path_array",handles=new[]{"A","B"},base_point=new[]{3d,4},path_handle="c1",count=100000,item_spacing=2.5,align_items=true}),"$",["A","B"],"create_associative_path_array");
            Require(path.PathHandle=="C1"&&path.Count==100000&&path.ItemSpacing==2.5&&path.AlignItems&&path.BasePoint==new Point2(3,4)&&EditReferences.All(path).Contains("C1"),"Actual retained path admission or explicit creation geometry was lost.");
            Deny(()=>AssociativeArrayValidation441.Parse(Wire.Element(new{operation="create_associative_path_array",handles=new[]{"A"},base_point=new[]{0d,0},path_handle="A",count=2,item_spacing=5d,align_items=true}),"$",["A"],"create_associative_path_array"),"INVALID_ARGUMENT");
            Deny(()=>AssociativeArrayValidation441.Parse(Wire.Element(new{operation="create_associative_polar_array",handles=new[]{"A"},base_point=new[]{0d,0},center=new[]{0d,0},radius=5d,start_angle_degrees=0d,count=0,angle_step_degrees=90d,rotate_items=true}),"$",["A"],"create_associative_polar_array"),"INVALID_ARGUMENT");
            Deny(()=>AssociativeArrayValidation431.Batch([polar,path]),"INVALID_OPERATION_ORDER");return Task.CompletedTask;
        });
        await test("block441 associative array sparse edits and published kind union preserve existing rectangle contract",()=>
        {
            string token=new('A',64);
            var polar=(SetAssociativePolarArray441)AssociativeArrayValidation441.Parse(Wire.Element(new{operation="set_associative_polar_array",handles=new[]{"A"},array_token=token,count=8}),"$",["A"],"set_associative_polar_array");
            var path=(SetAssociativePathArray441)AssociativeArrayValidation441.Parse(Wire.Element(new{operation="set_associative_path_array",handles=new[]{"A"},array_token=token,align_items=false}),"$",["A"],"set_associative_path_array");
            Require(polar.Count==8&&polar.AngleStepDegrees==null&&polar.RotateItems==null&&path.Count==null&&path.ItemSpacing==null&&path.AlignItems==false&&path.PathHandle==null&&!EditReferences.All(path).Contains("C1"),"Sparse edit fabricated unrequested native driver values or an omitted path target.");
            var retarget=(SetAssociativePathArray441)AssociativeArrayValidation441.Parse(Wire.Element(new{operation="set_associative_path_array",handles=new[]{"A"},array_token=token,path_handle="c1"}),"$",["A"],"set_associative_path_array");
            Require(retarget.PathHandle=="C1"&&retarget.Count==null&&retarget.ItemSpacing==null&&retarget.AlignItems==null&&EditReferences.All(retarget).Contains("C1"),"Explicit path-only edit lost its actual target reference or fabricated scalar edits.");
            Deny(()=>AssociativeArrayValidation441.Parse(Wire.Element(new{operation="set_associative_path_array",handles=new[]{"A"},array_token=token,path_handle=(string?)null}),"$",["A"],"set_associative_path_array"),"INVALID_ARGUMENT");
            Deny(()=>AssociativeArrayValidation441.Parse(Wire.Element(new{operation="set_associative_path_array",handles=new[]{"A"},array_token=token,path_handle="A"}),"$",["A"],"set_associative_path_array"),"INVALID_ARGUMENT");
            Deny(()=>AssociativeArrayValidation441.Parse(Wire.Element(new{operation="set_associative_polar_array",handles=new[]{"A"},array_token=token,angle_step_degrees=0d}),"$",["A"],"set_associative_polar_array"),"INVALID_ARGUMENT");
            Deny(()=>AssociativeArrayValidation441.Parse(Wire.Element(new{operation="set_associative_path_array",handles=new[]{"A"},array_token=token}),"$",["A"],"set_associative_path_array"),"INVALID_ARGUMENT");
            Deny(()=>AssociativeArrayValidation441.Parse(Wire.Element(new{operation="set_associative_path_array",handles=new[]{"A"},array_token=token,count=3,item_spacing=-1d}),"$",["A"],"set_associative_path_array"),"INVALID_ARGUMENT");
            var schema=ToolCatalog.Tools.Single(tool=>tool.Name=="nanocad.inspect_objects").OutputSchema!.Value.GetProperty("$defs").GetProperty("block_reference").GetProperty("properties").GetProperty("associative_array");
            var kinds=schema.GetProperty("oneOf").EnumerateArray().Select(branch=>branch.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()).ToHashSet();
            Require(kinds.SetEquals(new[]{"rectangular","polar","path"})&&schema.GetProperty("oneOf").EnumerateArray().Single(branch=>branch.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()=="rectangular").GetProperty("properties").TryGetProperty("columns",out _),"Existing rectangular read contract or actual array-kind union was replaced.");
            var pathProperties=schema.GetProperty("oneOf").EnumerateArray().Single(branch=>branch.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()=="path").GetProperty("properties");
            Require(pathProperties.GetProperty("path_handle").GetProperty("type").EnumerateArray().Select(type=>type.GetString()).ToHashSet().SetEquals(new[]{"string","null"})&&
                pathProperties.GetProperty("path_binding_observation").GetProperty("properties").GetProperty("state").GetProperty("enum").EnumerateArray().Select(state=>state.GetString()).ToHashSet().SetEquals(new[]{"live_identity","unavailable"}),
                "Published path read must explicitly preserve unavailable identity without fabricating a handle.");
            var binding=new AssociativePathBindingObservation441("unavailable","native_leaf_not_live",true,false,null,false,null,null,null,"not_observed");
            var partial=new AssociativePathArrayObservation441(token,"path","A",["B"],null,5,1,1,6,"Measure",true,0,0,1,1,0,[0,0,0],[0,0,1],[0,0,0],[1,1,1],0,"unavailable","unavailable",binding);
            var observed=Wire.Element(partial);
            Require(observed.GetProperty("path_handle").ValueKind==System.Text.Json.JsonValueKind.Null&&observed.GetProperty("count").GetInt32()==5&&
                observed.GetProperty("item_spacing").GetDouble()==6&&observed.GetProperty("source_handles")[0].GetString()=="B"&&
                observed.GetProperty("path_binding_observation").GetProperty("native_curve_geometry_available").ValueKind==System.Text.Json.JsonValueKind.Null,
                "Partial native path read must retain useful parameters/source IDs and distinguish unobserved geometry from false.");
            var live=Wire.Element(partial with{path_handle="C1",path_binding_observation=binding with{state="live_identity",unavailable_reason=null,native_leaf_handle="C1",native_leaf_live=true,same_space=true,same_database=true}});
            Require(live.GetProperty("path_handle").GetString()=="C1"&&live.GetProperty("path_binding_observation").GetProperty("unavailable_reason").ValueKind==System.Text.Json.JsonValueKind.Null,
                "An observed actual live native path must remain distinguishable from the partial read.");
            var variants=ToolCatalog.Tools.Single(tool=>tool.Name=="nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Select(branch=>branch.GetProperty("properties").GetProperty("operation")).Where(operation=>operation.TryGetProperty("const",out _)).Select(operation=>operation.GetProperty("const").GetString()).ToHashSet();
            var setPathSchema=ToolCatalog.Tools.Single(tool=>tool.Name=="nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(branch=>branch.GetProperty("properties").GetProperty("operation").TryGetProperty("const",out var operation)&&operation.GetString()=="set_associative_path_array");
            Require(setPathSchema.GetProperty("properties").TryGetProperty("path_handle",out _)&&!setPathSchema.GetProperty("required").EnumerateArray().Any(field=>field.GetString()=="path_handle")&&
                setPathSchema.GetProperty("anyOf").EnumerateArray().Any(branch=>branch.GetProperty("required").EnumerateArray().Any(field=>field.GetString()=="path_handle")),
                "Published set path must admit an explicit optional target as a meaningful edit without requiring it for sparse scalar changes.");
            Require(new[]{"create_associative_polar_array","set_associative_polar_array","create_associative_path_array","set_associative_path_array","create_associative_rectangular_array","set_associative_rectangular_array"}.All(variants.Contains),"Native array creation/edit operations are absent from actual published schema.");return Task.CompletedTask;
        });
    }
}
