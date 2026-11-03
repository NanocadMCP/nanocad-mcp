using NanoCAD.Contracts;

internal static class ModelSketchSelection412Tests
{
    static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("selection412 interior ray avoids recorded native semicircle junction roundoff",()=>
        {
            // Exact WCS observations from reconcile-crossbar412/002 history:
            // same radius2 Circle split into two native semicircles. Their
            // independently rounded center/end ordinates straddle the query
            // ray; a fixed horizontal count incorrectly returned outside.
            var plane=new SketchPlaneSpec(new(134,0,8),new(1,0,0),new(0,0,-1));
            var actual=new[]{
                ModelPolycurve.FromSketchArc("0",new(134,0,7.999999999999999),2,new(0,1,0),new(1,0,0),0,Math.PI,plane),
                ModelPolycurve.FromSketchArc("1",new(134,0,8),2,new(0,1,0),new(-1,0,-4.440892098500626e-16),0,Math.PI,plane)
            };
            foreach(var (angle,offset) in new[]{(0d,new Point2(0,0)),(.371,new Point2(245,-135)),(-2.2,new Point2(-800,450))})
            {
                Point2 Move(Point2 p)=>new(offset.X+Math.Cos(angle)*p.X-Math.Sin(angle)*p.Y,
                    offset.Y+Math.Sin(angle)*p.X+Math.Cos(angle)*p.Y);
                SketchConstraintCurve Curve(SketchConstraintCurve c)=>c with
                    {Start=Move(c.Start!),End=Move(c.End!),Center=c.Center==null?null:Move(c.Center)};
                void Check(SketchConstraintCurve[] curves,Point2 query,bool expected,string reason)=>
                    Need(ModelSketchSelection362.Inside(curves,Move(query),()=>{})==expected,reason);
                void Boundary(SketchConstraintCurve[] curves,Point2 query)
                {
                    try{ModelSketchSelection362.Inside(curves,Move(query),()=>{});throw new Exception("Boundary point accepted.");}
                    catch(DomainException error){Need(error.Error.code=="PROFILE_REGION_AMBIGUOUS","Wrong boundary refusal.");}
                }
                var copies=actual.Select(Curve).ToArray();
                Check(copies,new(0,0),true,"Recorded Circle center must be inside after rigid placement.");
                Check(copies.Reverse().ToArray(),new(0,0),true,"Native traversal order must not change containment.");
                Check(copies,new(3,0),false,"Outside point must remain outside.");
                Boundary(copies,new(2,0));Boundary(copies,new(0,2));
                // The same general ray route also preserves mixed Line/Arc
                // containment; there is no Circle-only shortcut.
                var mixed=new[]{new SketchConstraintCurve("line","line",new(-2,0),new(2,0)),
                    new SketchConstraintCurve("arc","arc",new(2,0),new(-2,0),new(0,0),2,Math.PI)}.Select(Curve).ToArray();
                Check(mixed,new(0,1),true,"Mixed Line/Arc interior must remain inside.");
                Check(mixed,new(0,-1),false,"Mixed Line/Arc exterior must remain outside.");
                Boundary(mixed,new(0,0));
            }
            return Task.CompletedTask;
        });
    }
}
