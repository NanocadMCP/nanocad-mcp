using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class LoftTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),op=Guid.NewGuid().ToString("D");
        object Section(double z,double radius=2,double x=0)=>new{center=new[]{x,0,z},radius};
        object Loft(params object[] sections)=>new{type="Solid3d",kind="loft",interpolation="smooth",sections};
        CreateArguments Parse(params object[] entities)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=op,expected_revision="1",units="Millimeters",entities}));
        void Reject(object item){try{Parse(item);}catch(DomainException){return;}throw new Exception("Invalid loft accepted.");}
        await test("solid loft preserves all circular sections in ledger intent",()=>
        {
            var value=Parse(Loft(Section(-3),Section(5,4,7)));
            var spec=(LoftedSolidSpec)value.Entities.Single();
            if(spec.Sections.Length!=2 || spec.Sections[1]!=new CircularLoftSection(new Point3(7,0,5),4))throw new Exception("Section geometry lost.");
            foreach(var changed in new[]{Loft(Section(-3),Section(6,4,7)),Loft(Section(-3),Section(5,3,7)),Loft(Section(-3),Section(5,4,8)),Loft(Section(-3),Section(0),Section(5,4,7))})
                if(Parse(changed).Fingerprint()==value.Fingerprint())throw new Exception("Changed loft shares fingerprint.");
            return Task.CompletedTask;
        });
        await test("solid loft rejects unordered intersecting planes and oversized scope",()=>
        {
            Reject(Loft(Section(0)));Reject(Loft(Section(0),Section(0)));Reject(Loft(Section(1),Section(0)));
            Reject(Loft(Section(0),Section(2,0)));Reject(Loft(Section(0),Section(1e6+1)));
            Reject(Loft(Section(0),Section(1,2,1e6+1)));Reject(Loft(Enumerable.Range(0,9).Select(i=>Section(i)).ToArray()));
            Reject(new{type="Solid3d",kind="loft",sections=new[]{Section(0),Section(10)},smooth=true});
            Reject(new{type="Solid3d",kind="loft",interpolation="automatic",sections=new[]{Section(0),Section(10)}});
            return Task.CompletedTask;
        });
        await test("solid ruled loft explicit intent differs from smooth",()=>
        {
            var ruled=Parse(new{type="Solid3d",kind="loft",interpolation="ruled",sections=new[]{Section(0),Section(10)}});
            if(((LoftedSolidSpec)ruled.Entities[0]).Interpolation!="ruled"||ruled.Fingerprint()==Parse(Loft(Section(0),Section(10))).Fingerprint())throw new Exception("Interpolation intent lost.");
            return Task.CompletedTask;
        });
        await test("solid ruled loft independent mass handles slope and translation",()=>
        {
            var sections=new[]{new CircularLoftSection(new Point3(1000000,-20,5),2),new CircularLoftSection(new Point3(1000006,-14,11),4)};
            var m=RuledLoftGeometry.Mass(sections);
            double t=17d/28;
            if(Math.Abs(m.Volume-56*Math.PI)>1e-10||Math.Abs(m.Centroid.X-(1000000+6*t))>1e-9||Math.Abs(m.Centroid.Y-(-20+6*t))>1e-10||Math.Abs(m.Centroid.Z-(5+6*t))>1e-10)throw new Exception("Wrong affine frustum mass.");
            return Task.CompletedTask;
        });
        await test("solid ruled loft accepts different interpolation but rejects invalid samples",()=>
        {
            var sections=new[]{new CircularLoftSection(new Point3(0,0,0),2),new CircularLoftSection(new Point3(4,2,10),3)};
            var side=(from t in new[]{0d,.25,.5,.75,1d} from a in new[]{0d,Math.PI/2,Math.PI,Math.PI*1.5,Math.PI*2} select new Point3(4*t+(2+t)*Math.Cos(a),2*t+(2+t)*Math.Sin(a),10*t)).ToArray();
            var bottom=Enumerable.Repeat(new Point3(0,0,0),25).ToArray();var top=Enumerable.Repeat(new Point3(4,2,10),25).ToArray();
            RuledLoftGeometry.VerifySurface(sections,new[]{side,bottom,top});
            void Bad(Point3[][] f){try{RuledLoftGeometry.VerifySurface(sections,f);}catch(DomainException){return;}throw new Exception("Invalid surface accepted.");}
            var distorted=side.ToArray();distorted[12]=distorted[12] with{X=distorted[12].X+.01};
            RuledLoftGeometry.VerifySurface(sections,new[]{distorted,bottom,top});
            var invalid=side.ToArray();invalid[3]=new Point3(double.NaN,0,0);Bad(new[]{invalid,bottom,top});
            Bad(Array.Empty<Point3[]>());Bad(new[]{side.Take(24).ToArray()});
            return Task.CompletedTask;
        });
        await test("solid loft respects shared body budget block exclusion and schema",()=>
        {
            var loft=Loft(Section(0),Section(10));
            try{Parse(Enumerable.Repeat(loft,9).ToArray());throw new Exception("Body limit bypassed.");}catch(DomainException){}
            try{CreateValidation.ParseEntities(Wire.Element(new[]{loft}));throw new Exception("Loft entered block definition scope.");}catch(DomainException){}
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}
