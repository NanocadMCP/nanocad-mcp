using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class BasicEditing334Tests
{
    private static JsonElement Request(object[] operations,string space="model")=>Wire.Element(new{
        bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),operation_id=Guid.NewGuid().ToString("D"),
        expected_revision="1",units="Millimeters",target_space=space,operations});
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool value){if(!value)throw new Exception("Connected editing334 contract assertion failed.");}
        void Reject(Action action,string? code=null)
        {try{action();}catch(DomainException error)when(code==null||error.Error.code==code){return;}throw new Exception("Invalid connected edit was accepted.");}
        await test("block334 region ownership references recovery and actual public schema",()=>
        {
            object Boolean(string op,string[] tools)=>new{operation="boolean_region",handles=new[]{"a1"},@operator=op,tool_handles=tools};
            var parsed=ModifyValidation.Parse(Request([Boolean("subtract",["b1","c1"])],"paper"));
            Need(parsed.Operations.Single() is RegionBooleanEditSpec{Handles:["A1"],ToolHandles:["B1","C1"]}&&WriteRecovery.NotGuaranteed(parsed));
            Need(EditReferences.All(parsed.Operations[0]).SequenceEqual(new[]{"A1","B1","C1"}));
            Reject(()=>ModifyValidation.Parse(Request([Boolean("subtract",["a1"])])));
            Reject(()=>ModifyValidation.Parse(Request([Boolean("union",["b1","B1"])])));
            Reject(()=>ModifyValidation.Parse(Request([Boolean("intersection",[])])));
            Reject(()=>ModifyValidation.Parse(Request([Boolean("union",["b1"]),new{operation="erase",handles=new[]{"b1"}}])),"INVALID_OPERATION_ORDER");
            var tool=ToolCatalog.Tools.Single(value=>value.Name=="nanocad.modify_entities");
            Need(tool.InputSchema.GetRawText().Contains("boolean_region")&&tool.OutputSchema!.Value.GetRawText().Contains("region_boolean_changes"));
            var inspect=ToolCatalog.Tools.Single(value=>value.Name=="nanocad.inspect_objects").OutputSchema!.Value.GetRawText();
            Need(inspect.Contains("boolean_geometry")&&inspect.Contains("native_planar_region_brep"));
            return Task.CompletedTask;
        });
        await test("block334 native table appearance selections are explicit and unambiguous",()=>
        {
            object Operation(object properties)=>new{operation="set_table_cell_format",handles=new[]{"A1"},table_token=new string('A',64),cells=new[]{new{row=0,column=1,properties}}};
            var parsed=ModifyValidation.Parse(Request([Operation(new{color_rgb=new[]{20,40,60},background=new{mode="color",color_aci=3},
                grids=new[]{new{line="HorizontalBottom",visible=true,color_rgb=new[]{90,80,70},lineweight_hundredth_mm=25,linetype_handle="c1"}}})]));
            var edit=(TableEditingSpec)parsed.Operations.Single();
            Need(edit.Cells is [{Column:1,Properties:{ColorRgb:not null,Background:{None:false,ColorAci:3},Grids:[{Line:"HorizontalBottom",LinetypeHandle:"C1"}]}}]);
            Reject(()=>ModifyValidation.Parse(Request([Operation(new{color_aci=1,color_rgb=new[]{1,2,3}})])));
            Reject(()=>ModifyValidation.Parse(Request([Operation(new{background=new{mode="none",color_aci=1}})])));
            Reject(()=>ModifyValidation.Parse(Request([Operation(new{grids=new[]{new{line="HorizontalBottom",visible=true},new{line="HorizontalBottom",visible=false}}})])));
            Reject(()=>ModifyValidation.Parse(Request([Operation(new{grids=new[]{new{line="All",visible=true}}})])));
            Need(ToolCatalog.Tools.Single(value=>value.Name=="nanocad.inspect_objects").OutputSchema!.Value.GetRawText().Contains("appearance_formats"));
            return Task.CompletedTask;
        });
        await test("block334 large parameter DAG has topological evaluation cycle and cancellation protection",()=>
        {
            const int count=2048;
            var variables=Enumerable.Range(0,count).Select(index=>new ModelExpressionVariable("id"+index,"V"+index,index==0?"1":"V"+(index-1)+"+1",
                index+1,index+1<count?["id"+(index+1)]:[],false)).Reverse().ToArray();
            int checkpoints=0;
            var plan=ModelExpressionPlanning.Prepare(variables,"id0","2",true,()=>checkpoints++);
            Need(plan.AffectedIds.Length==count&&plan.ExpectedValues["id2047"]==2049&&plan.ExpectedValues["id0"]==2);
            Reject(()=>ModelExpressionPlanning.Prepare(variables,"id0","2",false),"PARAMETER_HAS_DEPENDENTS");
            var cycle=variables.Select(value=>value.Id=="id1024"?value with{Expression="V1025+1"}:value).ToArray();
            Reject(()=>ModelExpressionPlanning.Prepare(cycle,"id0","2",true),"CYCLIC_MODEL_EXPRESSION");
            int visited=0;bool cancelled=false;int stop=checkpoints*9/10;
            try{ModelExpressionPlanning.Prepare(variables,"id0","2",true,()=>{if(++visited==stop)throw new OperationCanceledException();});}
            catch(OperationCanceledException){cancelled=true;}
            Need(cancelled&&visited==stop&&stop>count);
            var diamond=new[]{new ModelExpressionVariable("a","A","1",1,["b","c"],false),new("b","B","A+1",2,["d"],false),
                new("c","C","A*2",2,["d"],false),new("d","D","B+C+B",6,[],false)};
            Need(ModelExpressionPlanning.Prepare(diamond,"a","3",true).ExpectedValues["d"]==14);
            return Task.CompletedTask;
        });
        await test("block334 long native feature chains preserve complete order and reject cycles",()=>
        {
            var history=Enumerable.Range(0,4096).Select(index=>new ModelSequenceLink("n"+index,index==0?null:"n"+(index-1))).Reverse().ToArray();
            var featureIds=Enumerable.Range(0,2048).Select(index=>"n"+(2*index)).Reverse().ToArray();
            var result=ModelFeatureSequence.FromHistory(history,featureIds,"n0","n4094");
            Need(result.Length==2048&&result[0]=="n0"&&result[^1]=="n4094");
            var cyclic=history.Select(value=>value.Id=="n3"?value with{Previous="n3"}:value).ToArray();
            Reject(()=>ModelFeatureSequence.FromHistory(cyclic,featureIds,"n0","n4094"),"MODEL_ORDER_UNAVAILABLE");
            // A long auxiliary predecessor span exercises cancellation beyond
            // the inventory pass, including the nonrecursive predecessor walk.
            int checkpoints=0;string[] sparse=["n4095","n0"];
            Need(ModelFeatureSequence.FromHistory(history,sparse,"n0","n4095",()=>checkpoints++).SequenceEqual(new[]{"n0","n4095"}));
            bool cancelled=false;int visited=0,stop=checkpoints*9/10;
            try{ModelFeatureSequence.FromHistory(history,sparse,"n0","n4095",()=>{if(++visited==stop)throw new OperationCanceledException();});}
            catch(OperationCanceledException){cancelled=true;}Need(cancelled&&visited==stop&&stop>history.Length);
            return Task.CompletedTask;
        });
        await test("block334 complete drawing variable graph and retained profiles exceed legacy counts",()=>
        {
            const int count=1024;
            var variables=Enumerable.Range(0,count).Select(index=>new ModelExpressionVariable("id"+index,"V"+index,index==0?"1":"V"+(index-1)+"+1",
                index+1,index+1<count?["id"+(index+1)]:[],false)).Reverse().ToArray();
            DrawingVariablePlanning.VerifyConsumers(variables);
            Need(DrawingVariablePlanning.Consumers(variables)["id0"] is ["id1"]);
            var disconnected=variables.Concat(new[]{new ModelExpressionVariable("cycle1","Cycle1","Cycle2+1",1,["cycle2"],false),
                new ModelExpressionVariable("cycle2","Cycle2","Cycle1+1",1,["cycle1"],false)}).ToArray();
            Reject(()=>DrawingVariablePlanning.Consumers(disconnected),"CYCLIC_MODEL_EXPRESSION");
            var points=Enumerable.Range(0,128).Select(index=>new Point2(10*Math.Cos(index*2*Math.PI/128),10*Math.Sin(index*2*Math.PI/128))).ToArray();
            var profile=new ModelProfileSpec("polycurve",null,0,points,new double[points.Length]);
            var curves=ModelPolycurve.Curves(profile);
            Need(curves.Length==128&&DrawingArcContours.Order(curves).Length==128);
            Need(DrawingProfileGeometry.Measure(curves,5).Volume>0&&DrawingProfileGeometry.Profile(curves).Vertices!.Length==128);
            var input=Wire.Element(new{bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),operation_id=Guid.NewGuid().ToString("D"),
                expected_revision="1",units="Millimeters",entities=new[]{new{type="Solid3d",kind="model_extrusion",plane=new{origin=new[]{0,0,0},x_axis=new[]{1,0,0},y_axis=new[]{0,1,0}},
                    profile=new{kind="polygon",vertices=points.Select(point=>new[]{point.X,point.Y}).ToArray()},distance=5,direction="positive",editable_profile=true}}});
            Need(CreateValidation.Parse(input).Entities.Single() is ModelExtrusionSpec{EditableProfile:true,Profile:{Vertices:{Length:128}}});
            int checkpoints=0;_ = DrawingArcContours.Order(curves,()=>checkpoints++);
            bool cancelled=false;int seen=0,stop=checkpoints*9/10;
            try{DrawingArcContours.Order(curves,()=>{if(++seen==stop)throw new OperationCanceledException();});}
            catch(OperationCanceledException){cancelled=true;}Need(cancelled&&stop>curves.Length);
            return Task.CompletedTask;
        });
        await test("block334 stored formulas roundtrip rename and retain arithmetic association",()=>
        {
            // Canonical text and one distinct reference do not establish that
            // a create/rename request supplies an identifier rather than math.
            object[] Names(string name)=>[
                new{operation="name_model_parameter",handles=new[]{"A1"},variable_id="{11111111-1111-1111-1111-111111111111}",expected_name="previous",name},
                new{operation="create_model_variable",handles=new[]{"A1"},name,value=2},
                new{operation="create_drawing_variable",handles=new[]{"A1"},name,value=2},
                new{operation="name_drawing_parameter",handles=new[]{"A1"},variable_id="{11111111-1111-1111-1111-111111111111}",expected_name="previous",name}];
            foreach(string name in new[]{"A","_A9","DepthΔ2",new string((char)0x0394,64)}) // Numeric code points retain technical Unicode glyph coverage.
            {
                Need(ModelArithmetic.Parse(name).IsExactIdentifier(name));
                Need(ModelArithmetic.Parse("A+AA").RenameReference("A",name)==name+"+AA");
                foreach(var operation in Names(name))
                    Need(ModifyValidation.Parse(Request([operation])).Operations.Single() switch{
                        ModelParameterNameEdit edit=>edit.Name==name,ModelUserVariableEdit edit=>edit.Name==name,
                        DrawingProfileEdit edit=>edit.Name==name,_=>false});
            }
            foreach(string name in new[]{"A+1","-A","+A","A+A","A*1","(A)"," A","A ","A B",new string('A',65)})
            {
                foreach(var operation in Names(name))Reject(()=>ModifyValidation.Parse(Request([operation])));
                Reject(()=>ModelArithmetic.Parse("A+1").RenameReference("A",name));
            }
            var flat=ModelArithmetic.Parse(string.Join('+',Enumerable.Repeat("A",40)));
            var expanded=flat.RenameReference("A",new string('B',64));
            Need(expanded.Length>1024&&ModelArithmetic.ParseStored(expanded).References.SequenceEqual(new[]{new string('B',64)})&&
                ModelArithmetic.ParseStored(expanded).Evaluate(_=>2)==80);
            Need(ModelArithmetic.ParseStored(flat.Canonical).Evaluate(_=>3)==120);
            string legacy="A";for(int i=0;i<4096;i++)legacy="("+legacy+"+A)";
            int checkpoints=0;var stored=ModelArithmetic.ParseStored(legacy,()=>checkpoints++);
            int parseCheckpoints=checkpoints;
            Need(stored.Evaluate(_=>1)==4097&&ModelArithmetic.ParseStored(stored.Canonical).Evaluate(_=>1)==4097);
            bool cancelled=false;int seen=0,stop=parseCheckpoints*9/10;
            try{ModelArithmetic.ParseStored(legacy,()=>{if(++seen==stop)throw new OperationCanceledException();});}
            catch(OperationCanceledException){cancelled=true;}Need(cancelled&&seen==stop);
            double Read(string name)=>name=="A"?1e16:name=="B"?-1e16:1;
            var right=ModelArithmetic.Parse("A+(B+C)");var left=ModelArithmetic.Parse("(A+B)+C");
            Need(ModelArithmetic.ParseStored(right.Canonical).Evaluate(Read)==0&&ModelArithmetic.ParseStored(left.Canonical).Evaluate(Read)==1);
            Need(ModelArithmetic.ParseStored(ModelArithmetic.Parse("A/(B/C)").Canonical).Evaluate(name=>name=="A"?12:name=="B"?6:2)==4);
            Need(ModelArithmetic.ParseStored(ModelArithmetic.Parse("A-(B-C)").Canonical).Evaluate(name=>name=="A"?12:name=="B"?6:2)==8);
            foreach(string invalid in new[]{new string('(',18)+"1"+new string(')',18),string.Join('+',Enumerable.Repeat("1",66)),new string(' ',1024)+"1"})Reject(()=>ModelArithmetic.Parse(invalid));
            return Task.CompletedTask;
        });
    }
}
