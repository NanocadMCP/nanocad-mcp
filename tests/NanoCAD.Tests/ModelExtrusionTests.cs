using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelExtrusionTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string id=Guid.NewGuid().ToString("D");
        object Frame()=>new{origin=new[]{10d,20,30},x_axis=new[]{0d,1,0},y_axis=new[]{0d,0,1}};
        object Circle(double radius=3)=>new{kind="circle",center=new[]{5d,7},radius};
        object Entity(object profile,object? plane=null,string direction="positive",double distance=12)=>new{type="Solid3d",kind="model_extrusion",plane=plane??Frame(),profile,distance,direction};
        CreateArguments Parse(params object[] entities)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="9",units="Millimeters",entities}));
        void Reject(params object[] entities){try{Parse(entities);}catch(DomainException){return;}throw new Exception("Invalid model profile accepted.");}
        bool Near(double a,double b)=>Math.Abs(a-b)<1e-8;
        await test("model extrusion arbitrary plane oracle and history contract preserve geometry",()=>
        {
            var args=Parse(Entity(Circle()));var spec=(ModelExtrusionSpec)args.Entities.Single();var oracle=ModelExtrusionGeometry.Measure(spec);
            if(!Near(oracle.Volume,108*Math.PI)||oracle.Centroid!=new Point3(16,25,37)||oracle.Min!=new Point3(10,22,34)||oracle.Max!=new Point3(22,28,40))throw new Exception("Circle plane transform or extent oracle differs.");
            var negative=ModelExtrusionGeometry.Measure(spec with{Direction="negative"});
            if(negative.Centroid!=new Point3(4,25,37)||negative.Min!=new Point3(-2,22,34)||negative.Max!=new Point3(10,28,40))throw new Exception("Negative extrusion direction differs.");
            double diagonal=1/Math.Sqrt(2);
            var oblique=ModelExtrusionGeometry.Measure((ModelExtrusionSpec)Parse(Entity(new{kind="circle",center=new[]{0d,0},radius=2},
                new{origin=new[]{0d,0,0},x_axis=new[]{diagonal,diagonal,0},y_axis=new[]{0d,0,1}},distance:10)).Entities.Single());
            if(!Near(oblique.Volume,40*Math.PI)||!Near(oblique.Centroid.X,5*diagonal)||!Near(oblique.Centroid.Y,-5*diagonal)||
                !Near(oblique.Min.X,-2*diagonal)||!Near(oblique.Min.Y,-12*diagonal)||!Near(oblique.Max.X,12*diagonal)||!Near(oblique.Max.Y,2*diagonal))throw new Exception("Oblique circle projection differs.");
            if(args.Fingerprint()==Parse(Entity(Circle(4))).Fingerprint()||args.Fingerprint()==Parse(Entity(Circle(),direction:"negative")).Fingerprint())throw new Exception("Request identity omits profile or direction.");
            if(!WriteRecovery.NotGuaranteed(args))throw new Exception("Native model creation hides recovery limits.");
            return Task.CompletedTask;
        });
        await test("model extrusion polygon oracle is independent of contour winding",()=>
        {
            var vertices=new[]{new[]{0d,0},new[]{12d,0},new[]{12d,4},new[]{4d,4},new[]{4d,10},new[]{0d,10}};
            var frame=new{origin=new[]{-20d,0,0},x_axis=new[]{1d,0,0},y_axis=new[]{0d,1,0}};
            ExtrusionOracle Get(double[][] p)=>ModelExtrusionGeometry.Measure((ModelExtrusionSpec)Parse(Entity(new{kind="polygon",vertices=p},frame,"negative",5)).Entities.Single());
            var a=Get(vertices);var b=Get(vertices.Reverse().ToArray());
            if(!Near(a.Volume,360)||!Near(a.Centroid.X,-20+14d/3)||!Near(a.Centroid.Y,11d/3)||!Near(a.Centroid.Z,-2.5)||!Near(a.Centroid.X,b.Centroid.X)||!Near(a.Centroid.Y,b.Centroid.Y)||!Near(a.Volume,b.Volume))throw new Exception("Concave polygon area/centroid/winding differs.");
            return Task.CompletedTask;
        });
        await test("model extrusion rejects ambiguous planes mixed writes and invalid contours",()=>
        {
            Reject(Entity(Circle(),new{origin=new[]{0d,0,0},x_axis=new[]{2d,0,0},y_axis=new[]{0d,1,0}}));
            Reject(Entity(Circle(),new{origin=new[]{0d,0,0},x_axis=new[]{1d,0,0},y_axis=new[]{1d,0,0}}));
            Reject(Entity(Circle()),new{type="Circle",center=new[]{0d,0},radius=2});
            Reject(Entity(new{kind="polygon",vertices=new[]{new[]{0d,0},new[]{2d,2},new[]{0d,2},new[]{2d,0}}}));
            Reject(Entity(Circle(0)));Reject(Entity(Circle(),direction:"symmetric"));Reject(Entity(Circle(),distance:0));
            Reject(Entity(new{kind="circle",center=new[]{0d,0},radius=3,holes=new[]{1}}));
            Reject(Entity(new[]{1,2}));
            return Task.CompletedTask;
        });
        await test("model extrusion catalog explains history and non-guaranteed recovery",async()=>
        {
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities");
            var entry=tool.InputSchema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray()
                .Single(e=>e.GetProperty("properties").TryGetProperty("kind",out var kind)&&kind.TryGetProperty("const",out var v)&&v.GetString()=="model_extrusion");
            if(!entry.GetProperty("properties").TryGetProperty("profile",out _))throw new Exception("Legacy model profile input disappeared.");
            var result=await new ToolService().CallAsync("get_guidance",new Dictionary<string,System.Text.Json.JsonElement>{
                ["topic"]=Wire.Element("schema"),["operation"]=Wire.Element("create_entities"),["variant"]=Wire.Element("model_extrusion")},CancellationToken.None);
            using var help=System.Text.Json.JsonDocument.Parse(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text);
            string description=help.RootElement.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("description").GetString()!;
            if(!description.Contains("ROLLBACK_NOT_GUARANTEED")||!description.Contains("legacy branch creates no sketch constraints")||!description.Contains("inspect_model_history"))throw new Exception("Native model creation scope is misleading.");
        });
    }
}
