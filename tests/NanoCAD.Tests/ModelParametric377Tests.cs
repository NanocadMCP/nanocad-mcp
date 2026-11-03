using System.Text.Json;
using NanoCAD.Contracts;

internal static class ModelParametric377Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool value,string message){if(!value)throw new Exception(message);}
        void Refuse(Action action){try{action();throw new Exception("Expected refusal");}catch(DomainException){}}
        static SketchConstraintCurve Circle(string id,double x,double y,double radius)=>new(id,"circle",Center:new(x,y),Radius:radius);
        await test("377 annulus selection retains exact outer and hole sketch members",()=>
        {
            var outer=Circle("outer",0,0,10);var hole=Circle("hole",0,0,3);
            var selected=ModelSketchRegions377.Select([hole,outer],new(8,0),()=>{});
            Need(ReferenceEquals(selected.Outer.Single(),outer)&&ReferenceEquals(selected.Holes.Single().Single(),hole),"Area selection must retain actual members, not regenerated circles.");return Task.CompletedTask;
        });
        await test("377 multiple holes preserve all immediate child boundaries",()=>
        {
            var selected=ModelSketchRegions377.Select([Circle("outer",0,0,12),Circle("a",-4,0,2),Circle("b",4,0,2)],new(0,7),()=>{});
            Need(selected.Holes.Select(loop=>loop.Single().Id).SequenceEqual(new[]{"a","b"}),"Both actual disjoint inner loops belong to the chosen material cell.");return Task.CompletedTask;
        });
        await test("377 nested island is not mistaken for immediate annular hole",()=>
        {
            var selected=ModelSketchRegions377.Select([Circle("outer",0,0,10),Circle("middle",0,0,6),Circle("island",0,0,2)],new(8,0),()=>{});
            Need(selected.Holes.Length==1&&selected.Holes[0][0].Id=="middle","Nested island must not be toggled into the outer atomic cell.");return Task.CompletedTask;
        });
        await test("377 region rejects boundary point and intersecting circles",()=>
        {
            Refuse(()=>ModelSketchRegions377.Select([Circle("outer",0,0,10),Circle("hole",0,0,3)],new(3,0),()=>{}));
            Refuse(()=>ModelSketchRegions377.Select([Circle("a",0,0,5),Circle("b",6,0,5)],new(-2,0),()=>{}));return Task.CompletedTask;
        });
        static JsonElement Input(object definition)=>JsonSerializer.SerializeToElement(new{operation="set_model_extrusion_extent",handles=new[]{"1FD"},
            feature_id="{00000000-0000-0000-0000-000000000001}",expected_profile_id="{00000000-0000-0000-0000-000000000002}",
            expected_sketch_id="{00000000-0000-0000-0000-000000000003}",expected_last_item_id="{00000000-0000-0000-0000-000000000004}",expected_extent_token=new string('A',64),definition});
        await test("377 secondary ThroughAll has no finite length or synthetic driver input",()=>
        {
            var edit=(ModelExtrusionExtentsSpec351)ModelExtrusionExtentsValidation351.Parse(Input(new{extent="distance",direction="different",native_distance=5,taper_angle=0,extent2="through_all",taper_angle2=0}),"$",["1FD"]);
            Need(edit.Extent2=="through_all"&&edit.NativeDistance2==null&&edit.NativeDistance==5,"ThroughAll is native termination, not a large fixed length.");return Task.CompletedTask;
        });
        await test("377 existing Different Dist and Symmetric contracts remain available",()=>
        {
            var different=(ModelExtrusionExtentsSpec351)ModelExtrusionExtentsValidation351.Parse(Input(new{extent="distance",direction="different",native_distance=5,taper_angle=0,native_distance2=3,taper_angle2=0}),"$",["1FD"]);
            var symmetric=(ModelExtrusionExtentsSpec351)ModelExtrusionExtentsValidation351.Parse(Input(new{extent="distance",direction="symmetric",native_distance=5,taper_angle=0}),"$",["1FD"]);
            Need(different.Extent2=="distance"&&different.NativeDistance2==3&&symmetric.Extent2=="distance"&&symmetric.NativeDistance2==null,"Existing payload meanings must stay unchanged.");return Task.CompletedTask;
        });
        await test("377 contradictory ThroughAll length and Symmetric second mode are refused",()=>
        {
            Refuse(()=>ModelExtrusionExtentsValidation351.Parse(Input(new{extent="distance",direction="different",native_distance=5,taper_angle=0,extent2="through_all",native_distance2=99,taper_angle2=0}),"$",["1FD"]));
            Refuse(()=>ModelExtrusionExtentsValidation351.Parse(Input(new{extent="distance",direction="symmetric",native_distance=5,taper_angle=0,extent2="through_all"}),"$",["1FD"]));return Task.CompletedTask;
        });
    }
}
