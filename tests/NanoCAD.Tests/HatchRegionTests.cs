using NanoCAD.Contracts;

internal static class HatchRegionTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        Point2[] Rect(double x,double y,double w,double h)=>new[]{new Point2(x,y),new Point2(x+w,y),new Point2(x+w,y+h),new Point2(x,y+h)};
        var outer=Rect(0,0,100,60);
        void Reject(Point2[] p,params HatchHole[] holes)
        {try{HatchRegionValidation.Validate(p,holes);}catch(DomainException e)when(e.Error.code=="INVALID_GEOMETRY"){return;}throw new Exception("Ambiguous hatch region accepted");}
        await test("hatch region accepts exact disjoint circular and polygon voids independently of winding",()=>
        {
            var holes=new[]{new HatchHole(Rect(10,10,20,12)),new HatchHole(Center:new(65,30),Radius:8)};
            HatchRegionValidation.Validate(outer,holes);HatchRegionValidation.Validate(outer.Reverse().ToArray(),holes);
            return Task.CompletedTask;
        });
        await test("hatch region rejects tangent overlapping nested and concavity crossing holes",()=>
        {
            Reject(outer,new HatchHole(Center:new(5,20),Radius:5));
            Reject(outer,new HatchHole(Center:new(20,20),Radius:8),new(Center:new(30,20),Radius:8));
            Reject(outer,new HatchHole(Rect(10,10,30,30)),new(Center:new(20,20),Radius:2));
            Reject(outer,new HatchHole(Rect(10,10,30,30)),new(Rect(15,15,4,4)));
            Reject(outer,new HatchHole(Rect(10,10,30,30)),new(Center:new(45,20),Radius:6));
            var concave=new[]{new Point2(0,0),new Point2(40,0),new Point2(40,40),new Point2(25,40),new Point2(25,15),new Point2(15,15),new Point2(15,40),new Point2(0,40)};
            Reject(concave,new HatchHole(new[]{new Point2(10,30),new Point2(30,30),new Point2(20,5)}));
            return Task.CompletedTask;
        });
        await test("hatch region contract preserves exact circle intent and accounts for inner edges",()=>
        {
            var parsed=CreateValidation.ParseEntities(Wire.Element(new[]{new{type="Hatch",boundary=outer.Select(p=>new[]{p.X,p.Y}),
                holes=new[]{new{kind="circle",center=new[]{20,20},radius=4}}}}),allowDimensions:true);
            if(parsed.Single() is not HatchSpec {Holes:[{Radius:4,Center:not null,Vertices:null}]})throw new Exception("Circle hole lost");
            var line=new HatchPatternLine(0,0,0,0,10,0);
            if(HatchPatternDensity.Bound(outer,line,2)<=HatchPatternDensity.Bound(outer,line))throw new Exception("Hole work omitted");
            return Task.CompletedTask;
        });
    }
}

