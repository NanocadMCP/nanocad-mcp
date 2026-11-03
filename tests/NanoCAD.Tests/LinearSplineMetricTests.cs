using NanoCAD.Contracts;

internal static class LinearSplineMetricTests
{
    private static SplineGeometry Triangle(double[]? knots=null,double[]? weights=null)
    {
        var p=new[]{new[]{50d,30d,0d},new[]{70d,30d,0d},new[]{70d,45d,0d},new[]{50d,30d,0d}};
        return new(1,weights!=null,true,true,true,false,p,knots??new[]{0d,0d,1d/3,2d/3,1d,1d},weights??Array.Empty<double>(),1e-7,1e-7,0,knots?[^1]??1,p[0],p[^1]);
    }
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("linear closed spline metrics retain exact segment length and enclosing bounds across parameter domains",()=>{
            foreach(var curve in new[]{Triangle(),Triangle(new[]{0d,0d,1d,2d,3d,3d}),Triangle(weights:new[]{1d,.01,100d,1d})})
            {
                var result=LinearSplineMetrics.TryCalculate(curve)??throw new Exception("Missing exact metric");
                if(result.Length!=60||!result.Min.SequenceEqual(new[]{50d,30d,0d})||!result.Max.SequenceEqual(new[]{70d,45d,0d}))throw new Exception("Triangle metric differs from20+15+25 or misses the apex");
            }
            return Task.CompletedTask;
        });
        await test("linear closed spline metrics reject gaps malformed weights and unsupported smooth curves",()=>{
            var curve=Triangle();
            if(LinearSplineMetrics.TryCalculate(curve with{closed=false,periodic=false})!=null||
                LinearSplineMetrics.TryCalculate(curve with{knots=new[]{0d,0d,.5,.5,1d,1d}})!=null)throw new Exception("Unsupported form approximated as a connected polygon");
            var smooth=curve with{degree=2,knots=new[]{0d,0d,0d,.5,1d,1d,1d}};
            if(LinearSplineMetrics.TryCalculate(smooth)!=null)throw new Exception("Smooth spline approximated by control polygon");
            try{LinearSplineMetrics.TryCalculate(curve with{rational=true,weights=new[]{1d,-1d,1d,1d}});}catch(DomainException){return Task.CompletedTask;}
            throw new Exception("Nonpositive rational weight accepted");
        });
        await test("closed spline create and attribute-free block definitions preserve closure intent",()=>{
            object entity=new{type="Spline",closed=true,degree=1,control_points=new[]{new[]{0d,0d},new[]{20d,0d},new[]{20d,15d},new[]{0d,0d}},knots=new[]{0d,0d,1d/3,2d/3,1d,1d},weights=Array.Empty<double>()};
            var args=CreateValidation.Parse(Wire.Element(new{bridge_instance_id=Guid.NewGuid().ToString(),document_id=Guid.NewGuid().ToString(),expected_revision="1",operation_id=Guid.NewGuid().ToString(),units="Millimeters",entities=new[]{entity}}));
            if(args.Entities.Single() is not SplineSpec{Closed:true})throw new Exception("Closure intent lost");
            var spec=(SplineSpec)args.Entities.Single();
            var controls=spec.ControlPoints.Select(p=>new[]{p.X,p.Y,0}).ToArray();var actual=Triangle() with{control_points=controls,start_point=controls[0],end_point=controls[^1]};
            SplineCreation.RequireMatch(spec,actual);
            void Deny(Action operation,string code){try{operation();}catch(DomainException e) when(e.Error.code==code){return;}throw new Exception("Expected guard: "+code);}
            Deny(()=>SplineCreation.RequireMatch(spec with{Closed=false},actual),"NATIVE_SPLINE_MISMATCH");
            CurvedEditScope.Require(actual);
            var definition=BlockWriteValidation.ParseDefine(Wire.Element(new{operation="define_block",name="ClosedTest",base_point=new[]{0,0},entities=new[]{entity},attributes=Array.Empty<object>()}));
            if(definition.Entities.Single() is not SplineSpec{Closed:true}||definition.Attributes.Length!=0)throw new Exception("Closed block geometry changed");
            Deny(()=>BlockWriteValidation.ParseDefine(Wire.Element(new{operation="define_block",name="ClosedWithAttribute",base_point=new[]{0,0},entities=new[]{entity},
                attributes=new[]{new{tag="TAG",default_text="x",position=new[]{0,0},height=2}}})),"UNSUPPORTED_BLOCK_CONTENT");
            return Task.CompletedTask;
        });
    }
}
