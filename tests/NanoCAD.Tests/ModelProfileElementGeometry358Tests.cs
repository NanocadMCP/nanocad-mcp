using NanoCAD.Contracts;

internal static class ModelProfileElementGeometry358Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Need(bool value,string message){if(!value)throw new Exception(message);}
        static SketchConstraintCurve Line(string id,double ax,double ay,double bx,double by)=>new(id,"line",new(ax,ay),new(bx,by));
        static SketchConstraintCurve Arc(string id,double start,double sweep)=>new(id,"arc",new(5*Math.Cos(start),5*Math.Sin(start)),
            new(5*Math.Cos(start+sweep),5*Math.Sin(start+sweep)),new(0,0),5,sweep);
        await test("block358 mixed native boundaries preserve physical coverage across split and merge",()=>
        {
            var original=new[]{Line("chord",0,-5,0,5),Arc("semicircle",-Math.PI/2,Math.PI)};
            var split=new[]{Arc("arc2",0,Math.PI/2),Line("top",0,5,0,0),Arc("arc1",-Math.PI/2,Math.PI/2),Line("bottom",0,0,0,-5)};
            Need(ModelProfileElementGeometry358.SameBoundary(original,split,()=>{}),"Native reversed/split collinear segments and a seam-crossing semicircle must cover the same complete physical boundary.");
            Need(ModelProfileElementGeometry358.SameBoundary(split,original,()=>{}),"Coverage must also work when the native boundary merges those segments.");
            Need(!ModelProfileElementGeometry358.SameBoundary(original,split.Where(value=>value.Id!="arc1").ToArray(),()=>{}),"A missing half-arc is actual boundary loss.");
            Need(!ModelProfileElementGeometry358.SameBoundary(original,split.Append(Line("extra",1,0,2,0)).ToArray(),()=>{}),"An extra disjoint segment must not be hidden by one-way coverage.");
            return Task.CompletedTask;
        });
        await test("block358 circular coverage detects real gaps and rejects an open surviving cycle",()=>
        {
            var circle=new[]{new SketchConstraintCurve("circle","circle",Center:new(0,0),Radius:5)};
            var halves=new[]{Arc("first",0,Math.PI),Arc("second",Math.PI,Math.PI)};
            Need(ModelProfileElementGeometry358.SameBoundary(circle,halves,()=>{}),"A whole native Circle and two connected semicircles cover the same boundary.");
            Need(!ModelProfileElementGeometry358.SameBoundary(circle,new[]{halves[0],Arc("gap",Math.PI+0.01,Math.PI-0.01)},()=>{}),"A physical circular gap must be rejected rather than closed by tessellation or labels.");
            var closed=new[]{Line("a",0,0,4,0),Line("b",4,0,4,3),Line("c",4,3,0,3),Line("d",0,3,0,0)};
            _=ModelProfileElementGeometry358.Single(closed,()=>{});
            try{_=ModelProfileElementGeometry358.Single(closed[..3],()=>{});throw new Exception("Open surviving boundary was admitted.");}
            catch(DomainException){}
            return Task.CompletedTask;
        });
    }
}
