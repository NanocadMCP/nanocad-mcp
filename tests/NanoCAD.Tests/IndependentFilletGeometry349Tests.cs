using NanoCAD.Contracts;

internal static class IndependentFilletGeometry349Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block349 finite tangent fillet selected rays in tilted coordinates",()=>
        {
            double Dot(double[] a,double[] b)=>a.Zip(b,(x,y)=>x*y).Sum();
            double[] Sub(double[] a,double[] b)=>a.Zip(b,(x,y)=>x-y).ToArray();
            double Length(double[] a)=>Math.Sqrt(Dot(a,a));
            void Need(bool value){if(!value)throw new Exception("Finite fillet geometry mismatch.");}
            double[] corner=[23,-17,9];
            // One genuinely tilted plane and both acute/obtuse ray interiors.
            double[] first=[.6,0,.8], secondBasis=[0,1,0];
            foreach(double degrees in new[]{20.0,60,90,120,160})
            {
                double angle=degrees*Math.PI/180;
                var second=first.Zip(secondBasis,(a,b)=>a*Math.Cos(angle)+b*Math.Sin(angle)).ToArray();
                var p1=corner.Zip(first,(p,u)=>p+100*u).ToArray();
                var p2=corner.Zip(second,(p,u)=>p+110*u).ToArray();
                foreach(bool swap in new[]{false,true})
                {
                    var a=swap?second:first;var b=swap?first:second;
                    var plan=IndependentFilletGeometry349.BetweenLines(corner,swap?p2:p1,swap?p1:p2,5,1e-8,1e-10);
                    var along1=Sub(plan.FirstPoint,corner);var along2=Sub(plan.SecondPoint,corner);
                    Need(Dot(along1,a)>0&&Dot(along2,b)>0&&Length(along1)<100&&Length(along2)<110);
                    Need(Length(Sub(along1,a.Select(u=>u*plan.Setback).ToArray()))<1e-8);
                    Need(Length(Sub(along2,b.Select(u=>u*plan.Setback).ToArray()))<1e-8);
                    var r1=Sub(plan.FirstPoint,plan.Center);var r2=Sub(plan.SecondPoint,plan.Center);
                    Need(Math.Abs(Length(r1)-5)<1e-8&&Math.Abs(Length(r2)-5)<1e-8);
                    Need(Math.Abs(Dot(r1,a))<1e-8&&Math.Abs(Dot(r2,b))<1e-8);
                    Need(Math.Abs(Length(plan.Normal)-1)<1e-8&&Math.Abs(Dot(plan.Normal,a))<1e-8&&Math.Abs(Dot(plan.Normal,b))<1e-8);
                    Need(plan.Sweep>0&&plan.Sweep<Math.PI&&Math.Abs(plan.Sweep-(Math.PI-angle))<1e-8);
                }
            }
            return Task.CompletedTask;
        });
        await test("block349 finite fillet refuses consuming source and ambiguous rays",()=>
        {
            void Reject(double[] first,double[] second,double radius)
            {
                try {IndependentFilletGeometry349.BetweenLines([0,0,0],first,second,radius,1e-8,1e-10);}
                catch(DomainException){return;}
                throw new Exception("A collapsed/extended straight fillet was accepted.");
            }
            Reject([3,0,0],[0,10,0],5);
            Reject([5,0,0],[0,5,0],5);
            Reject([10,0,0],[20,0,0],2);
            Reject([10,0,0],[-20,0,0],2);
            Reject([10,0,0],[0,10,0],0);
            Reject([double.NaN,0,0],[0,10,0],2);
            return Task.CompletedTask;
        });
    }
}
