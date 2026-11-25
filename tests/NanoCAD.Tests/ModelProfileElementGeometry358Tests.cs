using NanoCAD.Contracts;
using System.Text.Json.Nodes;

internal static class ModelProfileElementGeometry358Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Need(bool value,string message){if(!value)throw new Exception(message);}
        await test("closed profile traversal retains winding and adjacency across a cyclic start change", () =>
        {
            var loop = new SketchConstraintCurve[] {
                new("bottom", "line", new(0,0), new(4,0)),
                new("corner", "arc", new(4,0), new(5,1), new(4,1), 1, Math.PI/2),
                new("right", "line", new(5,1), new(5,4)),
                new("top", "line", new(5,4), new(0,4)),
                new("left", "line", new(0,4), new(0,0)) };
            var rotated = loop.Skip(2).Concat(loop.Take(2)).ToArray();
            var reversed = loop.Reverse().ToArray();
            Need(ClosedProfileTraversal.Winding(loop,()=>{}) == 1 && ClosedProfileTraversal.Winding(rotated,()=>{}) == 1, "A rotated native contour must retain its direction.");
            Need(ClosedProfileTraversal.Winding(reversed,()=>{}) == -1, "Reversed traversal must remain distinguishable.");
            Need(ClosedProfileTraversal.Winding(new[]{loop[0], loop[2], loop[1], loop[3], loop[4]},()=>{}) == 0, "A broken adjacency must not be normalized.");
            string Canon(string s) { int k=ClosedProfileTraversal.Start(s.Select(c=>c.ToString()).ToArray(),()=>{}); return s[k..] + s[..k]; }
            Need(Canon("ABACB") == Canon("ACBAB") && Canon("ABACB") != Canon("ABBAC"), "Only cyclic equivalence preserves the perimeter order.");
            Need(Canon("ABAB") == Canon("BABA"), "Periodic patterns need a deterministic start.");
            return Task.CompletedTask;
        });
        await test("closed profile compares original positions unless both snapshots prove their traversal", () =>
        {
            static JsonObject Loop(string order) => new()
            {
                ["elements"] = new JsonArray(order.Select((kind, i) => (JsonNode)new JsonObject
                {
                    ["element_id"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["geometry"] = new JsonObject { ["kind"] = kind.ToString() }
                }).ToArray())
            };
            static string CompareSide(string order, int own, int peer)
            {
                var loop = Loop(order);
                ClosedProfileTraversal.AlignForComparison(loop, own, peer, () => {});
                return loop.ToJsonString();
            }
            string original = Loop("BABC").ToJsonString();
            Need(CompareSide("BABC", 1, 0) == original && CompareSide("BABC", 0, 1) == original, "One-sided proof must leave both original sequences identical.");
            Need(CompareSide("BABC", 1, 0) != CompareSide("ABCB", 0, 1), "A changed order without both witnesses must still fail.");
            Need(CompareSide("BABC", 1, 1) == CompareSide("ABCB", 1, 1), "Both witnessed rotations must agree.");
            Need(CompareSide("BABC", 1, -1) != CompareSide("BABC", -1, 1), "Opposite winding must stay distinguishable.");
            return Task.CompletedTask;
        });
        static SketchConstraintCurve Line(string id,double ax,double ay,double bx,double by)=>new(id,"line",new(ax,ay),new(bx,by));
        static SketchConstraintCurve Arc(string id,double start,double sweep)=>new(id,"arc",new(5*Math.Cos(start),5*Math.Sin(start)),
            new(5*Math.Cos(start+sweep),5*Math.Sin(start+sweep)),new(0,0),5,sweep);
        await test("native arc coverage respects the angular cut at the top right rounded holder corner", () =>
        {
            var member = Arc("native member", 0, Math.PI / 2);
            var profile = Arc("selected perimeter", -1.8e-15, Math.PI / 2 + 2.7e-15);
            Need(ModelProfileElementGeometry358.SameBoundary([member], [profile], () => {}), "Equivalent native quadrants lost coverage at the angular cut.");
            Need(!ModelProfileElementGeometry358.SameBoundary([member], [Arc("physical gap", 1e-4, Math.PI / 2 - 1e-4)], () => {}), "A resolved angular gap must remain detectable.");
            return Task.CompletedTask;
        });
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
