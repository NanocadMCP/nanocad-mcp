using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class HatchPatternTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var rectangle=new[]{new Point2(0,0),new Point2(20,0),new Point2(20,10),new Point2(0,10)};
        var boundary=rectangle.Select(p=>new[]{p.X,p.Y}).ToArray();
        string bridge=Guid.NewGuid().ToString("D"),document=Guid.NewGuid().ToString("D"),operation=Guid.NewGuid().ToString("D");
        object Entity(string pattern="ANSI31",double angle=0,double scale=1)=>new{type="Hatch",boundary,pattern_name=pattern,pattern_angle=angle,pattern_scale=scale};
        CreateArguments Parse(object entity)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=document,operation_id=operation,expected_revision="7",units="Millimeters",entities=new[]{entity}}));
        void Reject(object entity){try{Parse(entity);}catch(DomainException){return;}throw new Exception("Invalid hatch accepted.");}
        await test("patterned hatch preserves SOLID defaults and fingerprints pattern angle and scale",()=>{
            var old=Parse(new{type="Hatch",boundary});var explicitSolid=Parse(Entity("SOLID"));
            if(old.Entities[0] is not HatchSpec {PatternName:"SOLID",PatternAngle:0,PatternScale:1}||old.Fingerprint()!=explicitSolid.Fingerprint())throw new Exception("SOLID default changed.");
            var ansi=Parse(Entity());foreach(var changed in new[]{explicitSolid,Parse(Entity(angle:0.3)),Parse(Entity(scale:2))})
                if(changed.Fingerprint()==ansi.Fingerprint())throw new Exception("Pattern intent omitted from replay identity.");
            return Task.CompletedTask;
        });
        await test("legacy polygon hatch rejects external patterns invalid scale and meaningless SOLID settings",()=>{
            foreach(string pattern in new[]{"USER","ansi31","../ANSI31.pat","GRADIENT"})Reject(Entity(pattern));
            Reject(Entity(angle:-0.1));Reject(Entity(angle:Math.PI*2));Reject(Entity(scale:0.009));Reject(Entity(scale:101));
            Reject(Entity("SOLID",angle:1));Reject(Entity("SOLID",scale:2));
            Reject(new{type="Hatch",boundary,pattern_name="ANSI31",pattern_double=true});
            Reject(new{type="Hatch",boundary=new[]{new[]{0,0},new[]{100001,0},new[]{100001,1},new[]{0,1}},pattern_name="ANSI31"});
            return Task.CompletedTask;
        });
        await test("patterned hatch density conservatively bounds independently counted rectangle intersections",()=>{
            // Horizontal lines y=k*2 intersect this rectangle six times (including
            // both edges); shifting the box cannot increase that to more than six.
            var line=new HatchPatternLine(0,0,0,0,2,0);
            double bound=HatchPatternDensity.Bound(rectangle,line);
            if(bound<6||!double.IsFinite(bound))throw new Exception("Horizontal bound invalid.");
            var shifted=rectangle.Select(p=>new Point2(p.X+900000000,p.Y-900000000)).ToArray();
            if(HatchPatternDensity.Bound(shifted,line)!=bound)throw new Exception("World origin affects work bound.");
            // 45-degree lines with perpendicular spacing2 span (20+10)/sqrt(2):
            // at most floor(span/2)+1 lines for every phase of their base point.
            var rotated=new HatchPatternLine(Math.PI/4,0,0,-Math.Sqrt(2),Math.Sqrt(2),0);
            int independent=(int)Math.Floor(30/Math.Sqrt(2)/2)+1;
            if(HatchPatternDensity.Bound(rectangle,rotated)<independent)throw new Exception("Rotated bound undercounts.");
            if(HatchPatternDensity.Bound(rectangle,line with{OffsetY=1})<11)throw new Exception("Scale shrink undercounts.");
            return Task.CompletedTask;
        });
        await test("patterned hatch density observes dense patterns and rejects dashed nonfinite or zero-spacing definitions",()=>{
            void Fail(HatchPatternLine line,string code){try{HatchPatternDensity.Bound(rectangle,line);}catch(DomainException e)when(e.Error.code==code){return;}throw new Exception("Unsafe native pattern accepted.");}
            if(HatchPatternDensity.Bound(rectangle,new(0,0,0,0,0.0001,0))<=2000)throw new Exception("Dense finite estimate was truncated.");
            Fail(new(0,0,0,0,2,1),"HATCH_PATTERN_UNSUPPORTED");Fail(new(double.NaN,0,0,0,2,0),"HATCH_PATTERN_UNSUPPORTED");
            Fail(new(0,double.PositiveInfinity,0,0,2,0),"HATCH_PATTERN_UNSUPPORTED");
            Fail(new(0,0,0,1,0,0),"HATCH_PATTERN_UNSUPPORTED");
            return Task.CompletedTask;
        });
        await test("configured hatch spacing is invariant under rotation and scales exactly once",()=>{
            foreach(double scale in new[]{0.5,1,2,100})
            {
                double expected=HatchPatternDensity.Bound(rectangle,new(0,0,0,0,2*scale,0));
                foreach(double angle in new[]{0,Math.PI/4,Math.PI/2,Math.PI,0.3})
                    if(HatchPatternDensity.Bound(rectangle,new(angle,0,0,-2*scale*Math.Sin(angle),2*scale*Math.Cos(angle),0))!=expected)throw new Exception("Configured offset was transformed twice.");
            }
            return Task.CompletedTask;
        });
        await test("hatch keeps legacy polygon limits and publishes native source groups with focused Spline guidance",async()=>{
            DefineBlockEdit Block(object member)=>BlockWriteValidation.ParseDefine(Wire.Element(new{operation="define_block",name="HATCH_PATTERN_TEST",base_point=new[]{0,0},entities=new[]{member}}));
            if(Block(Entity()).Entities.Single() is not HatchSpec {PatternName:"ANSI31"})throw new Exception("Independent local block Hatch was refused.");
            foreach(var member in new object[]{new{type="Hatch",boundary_handle="AB"},new{type="Hatch",boundary_groups=new[]{new[]{"AB"}}}})
            {
                try{Block(member);throw new Exception("Block definition accepted drawing-root Hatch association.");}
                catch(DomainException error)when(error.Error.code=="UNSUPPORTED_BLOCK_CONTENT"){}
            }
            var tools=ToolCatalog.Tools;
            var variants=tools.Single(t=>t.Name=="nanocad.create_entities").InputSchema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray()
                .Where(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()=="Hatch").ToArray();
            var legacy=variants.Single(v=>v.GetProperty("properties").TryGetProperty("boundary",out _));
            var direct=variants.Single(v=>v.GetProperty("properties").TryGetProperty("boundary_handle",out _));
            var grouped=variants.Single(v=>v.GetProperty("properties").TryGetProperty("boundary_groups",out _));
            var patterns=legacy.GetProperty("properties").GetProperty("pattern_name").GetProperty("enum").EnumerateArray().Select(p=>p.GetString()).ToArray();
            var blockHatches=BlockWriteSchema.Define().GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray()
                .Where(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()=="Hatch").ToArray();
            if(tools.Count!=ToolCatalog.OperationNames.Length+1||!tools.Any(t=>t.Name=="nanocad.get_guidance")||!patterns.SequenceEqual(new[]{"SOLID","ANSI31"})||
                direct.GetProperty("properties").GetProperty("pattern_name").TryGetProperty("enum",out _)||grouped.GetProperty("properties").GetProperty("pattern_name").TryGetProperty("enum",out _)||
                !blockHatches.Any(v=>v.GetProperty("properties").TryGetProperty("boundary",out _))||blockHatches.Any(v=>v.GetProperty("properties").TryGetProperty("boundary_handle",out _)||v.GetProperty("properties").TryGetProperty("boundary_groups",out _)))throw new Exception("Legacy/native Hatch authoring scopes were conflated.");
            var native=Parse(new{type="Hatch",boundary_groups=new[]{new[]{"00ab"},new[]{"bc"}},pattern_name="ANSI32",pattern_angle=.4,pattern_scale=2500});
            if(native.Entities.Single() is not AssociativeHatchSpec {PatternName:"ANSI32",PatternScale:2500} associated||
                !associated.Groups().SelectMany(group=>group).SequenceEqual(new[]{"AB","BC"}))throw new Exception("Native pattern or complete source-group intent lost.");
            Reject(new{type="Hatch",boundary_handle="AB",pattern_name="../ANSI31.pat"});Reject(new{type="Hatch",boundary_handle="AB",pattern_scale=0});
            Reject(new{type="Hatch",boundary_groups=new[]{new[]{"AB"},new[]{"00ab"}}});
            var result=await new ToolService().CallAsync("get_guidance",new Dictionary<string,JsonElement>{
                ["topic"]=Wire.Element("schema"),["operation"]=Wire.Element("create_entities"),["variant"]=Wire.Element("Hatch")},CancellationToken.None);
            using var focused=JsonDocument.Parse(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text);
            var help=focused.RootElement.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray()
                .Single(v=>v.GetProperty("properties").TryGetProperty("boundary_groups",out _));
            string scope=help.GetProperty("description").GetString()!;
            if(!scope.Contains("Spline")||!scope.Contains("WCS XY/Z0")||help.GetProperty("properties").GetRawText()!=grouped.GetProperty("properties").GetRawText())throw new Exception("Focused native Hatch source/plane contract is missing or differs from published fields.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(tools,Wire.Json));
        });
    }
}

