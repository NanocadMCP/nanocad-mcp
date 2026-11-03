using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelArithmeticTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool valid){if(!valid)throw new Exception("Linked parameter planning assertion failed.");}
        void Reject(Action action,string? code=null)
        {try{action();}catch(DomainException error)when(code==null||error.Error.code==code){return;}throw new Exception("Invalid arithmetic/graph accepted.");}
        await test("placement formulas allow signed axes without relaxing forming dimensions",()=>
        {
            ModelExpressionVariable[] vars=[new("u","Layout","2",2,["x","y","a"],false),
                new("x","ShiftX","Layout*3",6,[],true,false,true),
                new("y","ShiftY","-Layout",-2,[],true,false,true),
                new("a","Tilt","Layout*45",90,[],false,true,true)];
            var negative=ModelExpressionPlanning.Prepare(vars,"u","-2",true);
            Need(negative.ExpectedValues["x"]==-6&&negative.ExpectedValues["y"]==2&&negative.ExpectedValues["a"]==-90);
            var zero=ModelExpressionPlanning.Prepare(vars,"u","0",true);Need(zero.ExpectedValues.Values.All(v=>v==0));
            Reject(()=>ModelExpressionPlanning.Prepare(vars,"u","9",true),"INVALID_GEOMETRY");
            Reject(()=>ModelExpressionPlanning.Prepare(vars,"x","1000001",false),"INVALID_GEOMETRY");
            Reject(()=>ModelExpressionPlanning.Prepare(vars,"u","3",false),"PARAMETER_HAS_DEPENDENTS");
            Reject(()=>ModelExpressionPlanning.Prepare([new("d","Depth","1",1,[])],"d","0",false),"INVALID_GEOMETRY");
            Reject(()=>ModelExpressionPlanning.Prepare([new("r","Sweep","90",90,[],false,true)],"r","-90",false),"INVALID_GEOMETRY");
            return Task.CompletedTask;
        });
        await test("model parameter naming binds identity and preserves exact reference tokens",()=>
        {
            Need(ModelArithmetic.Parse("A+AA/2").RenameReference("A","DepthΔ")=="DepthΔ+AA/2");
            Need(ModelArithmetic.Parse("-A + 2*A").RenameReference("A","Depth")=="-Depth+2*Depth");
            Reject(()=>ModelArithmetic.Parse("A+1").RenameReference("A","A+B"));
            object Op(string name)=>new{operation="name_model_parameter",handles=new[]{"A"},variable_id="{11111111-1111-1111-1111-111111111111}",expected_name="dist2",name};
            object Request(object[] operations)=>new{bridge_instance_id="11111111-1111-1111-1111-111111111111",document_id="22222222-2222-2222-2222-222222222222",operation_id="33333333-3333-3333-3333-333333333333",expected_revision="1",units="Millimeters",operations};
            var first=ModifyValidation.Parse(Wire.Element(Request(new[]{Op("Depth")})));
            Need(first.Operations.Single() is ModelParameterNameEdit edit&&edit.Name=="Depth");
            Need(first.Fingerprint()!=ModifyValidation.Parse(Wire.Element(Request(new[]{Op("Height")}))).Fingerprint());
            foreach(var bad in new[]{"dist2","DIST2","2Depth","Depth+1","Depth value"})Reject(()=>ModifyValidation.Parse(Wire.Element(Request(new[]{Op(bad)}))));
            Reject(()=>ModifyValidation.Parse(Wire.Element(Request(new[]{Op("Depth"),Op("Height")}))));
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetRawText();
            Need(schema.Contains("name_model_parameter")&&schema.Contains("expected_name"));
            return Task.CompletedTask;
        });
        await test("user driver creation identity scalar arithmetic and distance propagation",()=>
        {
            object Op(string name,double value)=>new{operation="create_model_variable",handles=new[]{"A"},name,value};
            object Request(object[] operations)=>new{bridge_instance_id="11111111-1111-1111-1111-111111111111",document_id="22222222-2222-2222-2222-222222222222",operation_id="33333333-3333-3333-3333-333333333333",expected_revision="1",units="Millimeters",operations};
            var first=ModifyValidation.Parse(Wire.Element(Request(new[]{Op("Task",-5)})));
            Need(first.Operations.Single() is ModelUserVariableEdit edit&&edit.Value==-5&&WriteRecovery.NotGuaranteed(first));
            Need(first.Fingerprint()!=ModifyValidation.Parse(Wire.Element(Request(new[]{Op("Task",0)}))).Fingerprint());
            foreach(var name in new[]{"1A","A+B","Bad name",new string('A',65)})Reject(()=>ModifyValidation.Parse(Wire.Element(Request(new[]{Op(name,5)}))));
            Reject(()=>ModifyValidation.Parse(Wire.Element(Request(new[]{Op("A",1e13)}))));
            Reject(()=>ModifyValidation.Parse(Wire.Element(Request(new[]{Op("A",1),Op("B",2)}))));
            var vars=new[]{new ModelExpressionVariable("a","Driver","0",0,new[]{"b"},false),
                new ModelExpressionVariable("b","Length","Driver+10",10,new[]{"c"}),
                new ModelExpressionVariable("c","Boss","Length/2",5,Array.Empty<string>())};
            var plan=ModelExpressionPlanning.Prepare(vars,"a","-2",true);
            Need(plan.ExpectedValues["a"]==-2&&plan.ExpectedValues["b"]==8&&plan.ExpectedValues["c"]==4);
            Reject(()=>ModelExpressionPlanning.Prepare(vars,"a","-10",true),"INVALID_GEOMETRY");
            Reject(()=>ModelExpressionPlanning.Prepare(vars,"a","1e13",true),"INVALID_GEOMETRY");
            Need(ModelVariableMetadata.IsUser("User, Reference")&&!ModelVariableMetadata.IsUser("User, Model3D"));
            Need(ModelVariableMetadata.SameTypeOrTargetReference("User","User, Reference",true,true));
            Need(!ModelVariableMetadata.SameTypeOrTargetReference("User","Model3D, Reference",true,true));
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            Need(tool.InputSchema.GetRawText().Contains("create_model_variable")&&tool.OutputSchema!.Value.GetRawText().Contains("model_variables_created"));
            var change=new ModelParameterChange("a",null,"0","-2",0,-2);
            Need(Wire.Element(change).GetProperty("feature_id").ValueKind==System.Text.Json.JsonValueKind.Null);
            return Task.CompletedTask;
        });
        await test("model variable Reference flag follows only target expression dependencies",()=>
        {
            Need(ModelVariableMetadata.SameTypeOrTargetReference("Model3D","Model3D, Reference",true,true));
            Need(ModelVariableMetadata.SameTypeOrTargetReference("Model3D, Reference","Model3D",true,false));
            Need(!ModelVariableMetadata.SameTypeOrTargetReference("Model3D","Model3D, Reference",false,true));
            Need(!ModelVariableMetadata.SameTypeOrTargetReference("Model3D","Model3D, Reference",true,false));
            Need(!ModelVariableMetadata.SameTypeOrTargetReference("Model3D","Model3D, Reference, ReadOnly",true,true));
            Need(!ModelVariableMetadata.SameTypeOrTargetReference("Model3D","User, Reference",true,true));
            return Task.CompletedTask;
        });
        await test("model arithmetic precedence exact names bounded syntax and finite results",()=>
        {
            var expression=ModelArithmetic.Parse("(HeightΔ + Width*2)/4 - -1e-3");
            Need(expression.References.SequenceEqual(new[]{"HeightΔ","Width"}));
            Need(Math.Abs(expression.Evaluate(n=>n=="HeightΔ"?20:10)-10.001)<1e-12);
            Need(ModelArithmetic.Parse("((Width)) + +2.0").Canonical==ModelArithmetic.Parse("Width+2").Canonical);
            foreach(string bad in new[]{""," ","Width=2","abs(Width)","1 mm","1;2","1\n2","NaN()","1e309","1e",".","'Width'","A[0]",new string('(',18)+"1"+new string(')',18),string.Join('+',Enumerable.Repeat("1",66))})
                Reject(()=>ModelArithmetic.Parse(bad));
            Reject(()=>ModelArithmetic.Parse("1/(Width-2)").Evaluate(_=>2));
            Reject(()=>ModelArithmetic.Parse("1e308*1e308").Evaluate(_=>0));
            Reject(()=>ModelArithmetic.Parse("Width").Evaluate(_=>double.NaN));
            return Task.CompletedTask;
        });
        await test("angular expressions share a driver with depths and retain distinct bounds and units",()=>
        {
            var vars=new[]{new ModelExpressionVariable("u","Config","2",2,["a","d"],false),
                new ModelExpressionVariable("a","Sweep","Config*60",120,[],false,true),
                new ModelExpressionVariable("d","Bore","Config/2",1,[],true)};
            var plan=ModelExpressionPlanning.Prepare(vars,"u","3",true);
            Need(plan.ExpectedValues["a"]==180&&plan.ExpectedValues["d"]==1.5);
            Reject(()=>ModelExpressionPlanning.Prepare(vars,"u","3",false),"PARAMETER_HAS_DEPENDENTS");
            Reject(()=>ModelExpressionPlanning.Prepare(vars,"u","7",true),"INVALID_GEOMETRY");
            Reject(()=>ModelExpressionPlanning.Prepare(vars,"u","0.001",true),"INVALID_GEOMETRY");
            Need(ModelExpressionPlanning.Prepare(vars,"a","360",false).ExpectedValues["a"]==360);
            Need(ModelExpressionPlanning.Prepare(vars,"a",".1",false).ExpectedValues["a"]==.1);
            Need(ModelExpressionPlanning.Prepare(vars,"a",".05",false).ExpectedValues["a"]==.05);
            foreach(string bad in new[]{"0","360.1","-1","5e-324"})Reject(()=>ModelExpressionPlanning.Prepare(vars,"a",bad,false),"INVALID_GEOMETRY");
            var refAngle=ModelExpressionPlanning.Prepare(vars,"d","Sweep/60",false);
            Need(refAngle.ExpectedValues["d"]==2&&refAngle.ReferencedIds.SequenceEqual(new[]{"a"}));
            var change=new ModelParameterChange("a","f","120","Config*60",120,180,"degrees");
            Need(Wire.Element(change).GetProperty("value_unit").GetString()=="degrees");
            Need(ModelArithmetic.Parse("Sweep+Sweep2").RenameReference("Sweep","Sector")=="Sector+Sweep2");
            return Task.CompletedTask;
        });
        ModelExpressionVariable[] Chain()=>new[]{
            new ModelExpressionVariable("a","Height","20",20,new[]{"b"}),
            new ModelExpressionVariable("b","Boss","Height/2",10,new[]{"c"}),
            new ModelExpressionVariable("c","Step","Boss+2",12,Array.Empty<string>()),
            new ModelExpressionVariable("d","Width","40",40,Array.Empty<string>())};
        await test("model arithmetic plans cascaded distances using independent native reference values",()=>
        {
            var plan=ModelExpressionPlanning.Prepare(Chain(),"a","Width*.75",true);
            Need(plan.AffectedIds.SequenceEqual(new[]{"a","b","c"})&&plan.ReferencedIds.SequenceEqual(new[]{"d"}));
            Need(plan.ExpectedValues["a"]==30&&plan.ExpectedValues["b"]==15&&plan.ExpectedValues["c"]==17);
            var literal=ModelExpressionPlanning.Prepare(Chain(),"c","9",false);
            Need(literal.AffectedIds.SequenceEqual(new[]{"c"})&&literal.ExpectedValues["c"]==9&&literal.ReferencedIds.Length==0);
            return Task.CompletedTask;
        });
        await test("model arithmetic rejects accidental cascades cycles missing values and ambiguous names",()=>
        {
            Reject(()=>ModelExpressionPlanning.Prepare(Chain(),"a","30",false),"PARAMETER_HAS_DEPENDENTS");
            Reject(()=>ModelExpressionPlanning.Prepare(Chain(),"a","Boss/2",true),"CYCLIC_MODEL_EXPRESSION");
            Reject(()=>ModelExpressionPlanning.Prepare(Chain(),"a","Height+1",true),"CYCLIC_MODEL_EXPRESSION");
            Reject(()=>ModelExpressionPlanning.Prepare(Chain(),"a","width",true),"VARIABLE_NAME_AMBIGUOUS");
            var duplicate=Chain();duplicate[3]=duplicate[3] with{Name="Boss"};
            Reject(()=>ModelExpressionPlanning.Prepare(duplicate,"c","Boss",false),"VARIABLE_NAME_AMBIGUOUS");
            var unavailable=Chain();unavailable[3]=unavailable[3] with{Value=null};
            Reject(()=>ModelExpressionPlanning.Prepare(unavailable,"a","Width/2",true),"VARIABLE_VALUE_UNAVAILABLE");
            var missing=Chain();missing[0]=missing[0] with{DownstreamVariables=new[]{"missing"}};
            Reject(()=>ModelExpressionPlanning.Prepare(missing,"a","30",true),"VARIABLES_UNAVAILABLE");
            var cyclic=Chain();cyclic[2]=cyclic[2] with{DownstreamVariables=new[]{"a"}};
            Reject(()=>ModelExpressionPlanning.Prepare(cyclic,"a","30",true),"CYCLIC_MODEL_EXPRESSION");
            Reject(()=>ModelExpressionPlanning.Prepare(Chain(),"a","0.001",true),"INVALID_GEOMETRY");
            var longChain=Enumerable.Range(0,17).Select(i=>new ModelExpressionVariable(i.ToString(),"V"+i,"10",10,i<16?new[]{(i+1).ToString()}:Array.Empty<string>())).ToArray();
            var complete=ModelExpressionPlanning.Prepare(longChain,"0","20",true);
            Need(complete.AffectedIds.Length==17&&complete.ExpectedValues["0"]==20&&complete.ExpectedValues["16"]==10);
            return Task.CompletedTask;
        });
        await test("model arithmetic write identity recovery schema and changed parameter provenance",()=>
        {
            string id=Guid.NewGuid().ToString("D"),variable=Guid.NewGuid().ToString("B");
            Dictionary<string,object> Input()=>new(){["bridge_instance_id"]=id,["document_id"]=id,["operation_id"]=id,["expected_revision"]="1",
                ["units"]="Millimeters",["handle"]="AB",["variable_id"]=variable,["expected_expression"]="10",["value"]=20};
            ModelParameterArguments Parse(Dictionary<string,object> input)=>ModelParameterValidation.Parse(Wire.Element(input));
            var ordinary=Parse(Input());Need(!ordinary.UsesExpressionPlan&&!WriteRecovery.NotGuaranteed(ordinary));
            var input=Input();input.Remove("value");input["expression"]="Height/2";
            var expression=Parse(input);Need(expression.UsesExpressionPlan&&WriteRecovery.NotGuaranteed(expression)&&expression.Expression=="Height/2");
            input["propagate"]=true;var cascade=Parse(input);Need(cascade.Propagate&&cascade.Fingerprint()!=expression.Fingerprint());
            input["value"]=20;Reject(()=>Parse(input));input.Remove("expression");Need(Parse(input).UsesExpressionPlan);
            input["propagate"]="true";Reject(()=>Parse(input));
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.set_model_parameter");
            Need(tool.Description!.Contains("ROLLBACK_NOT_GUARANTEED")&&tool.InputSchema.GetRawText().Contains("propagate")&&tool.OutputSchema!.Value.GetRawText().Contains("parameter_changes"));
            var legacy=new ModelParameterResult(id,"committed",false,new(id,id,"AB"),variable,variable,"10","20",20,"Millimeters",100,"not_tested");
            Need(!Wire.Element(legacy).TryGetProperty("parameter_changes",out _));
            var changed=legacy with{parameter_changes=new[]{new ModelParameterChange(variable,variable,"10","Height/2",10,20)}};
            Need(Wire.Element(changed).GetProperty("parameter_changes")[0].GetProperty("expression").GetString()=="Height/2");
            return Task.CompletedTask;
        });
    }
}
