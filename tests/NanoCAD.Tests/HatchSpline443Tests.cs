using NanoCAD.Contracts;
using Span443=NanoCAD.Contracts.HatchBoundaryGeometry.ParameterSpan443;

internal static class HatchSpline443Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("Hatch443 native overlap coverage accepts reversed split ranges in each independent parameter domain",()=>
        {
            // The same closed curve may have a different seam, direction and
            // parameter scale in stored Hatch NURBS. No shared basis is needed.
            if(HatchBoundaryGeometry.Uncovered443(0,1,[new(.6,1),new(.6,0)]).Length!=0||
                HatchBoundaryGeometry.Uncovered443(40,140,[new(100,40),new(140,90)]).Length!=0)
                throw new Exception("Complete native overlap coverage rejected a representation change.");
            return Task.CompletedTask;
        });
        await test("Hatch443 overlap union retains true gaps and clips excess native domain ranges",()=>
        {
            var gaps=HatchBoundaryGeometry.Uncovered443(2,12,[new(-100,4),new(6,9),new(20,11),new(6,9)]);
            if(!gaps.SequenceEqual(new[]{new Span443(4,6),new Span443(9,11)}))
                throw new Exception("Overlap union hid a missing interior span or invented exterior coverage.");
            if(!HatchBoundaryGeometry.Uncovered443(2,12,[]).SequenceEqual(new[]{new Span443(2,12)}))
                throw new Exception("Empty native overlap was accepted as geometric coverage.");
            return Task.CompletedTask;
        });
        await test("Hatch443 overlap coverage rejects nonfinite data and honours cancellation",()=>
        {
            foreach(var invalid in new[]{new[]{double.NaN,1},new[]{1.0,1},new[]{0.0,double.PositiveInfinity}})
            {
                try{HatchBoundaryGeometry.Uncovered443(invalid[0],invalid[1],[]);throw new Exception("Invalid native domain accepted.");}
                catch(DomainException error)when(error.Error.code=="INVALID_GEOMETRY"){}
            }
            try{HatchBoundaryGeometry.Uncovered443(0,1,[new(0,double.NaN)]);throw new Exception("Nonfinite native overlap accepted.");}
            catch(DomainException error)when(error.Error.code=="INVALID_GEOMETRY"){}
            try{HatchBoundaryGeometry.Uncovered443(0,1,[new(0,1)],()=>throw new OperationCanceledException());throw new Exception("Native coverage ignored cancellation.");}
            catch(OperationCanceledException){}
            return Task.CompletedTask;
        });
    }
}
