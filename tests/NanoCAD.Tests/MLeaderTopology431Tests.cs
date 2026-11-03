using System.Text.Json.Nodes;
using NanoCAD.Contracts;

internal static class MLeaderTopology431Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Need(bool value,string message){if(!value)throw new Exception(message);}
        static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("Ambiguous topology input was accepted.");}
        JsonObject Request()=>new(){["operation"]="edit_mleader_topology",["handles"]=new JsonArray("A1"),["expected_geometry_token"]=new string('A',64),["edits"]=new JsonArray(
            new JsonObject{["action"]="add_line",["leader_index"]=4,["vertices_wcs"]=new JsonArray(new JsonArray(0,0,0),new JsonArray(20,10,0))},
            new JsonObject{["action"]="insert_vertex",["leader_line_index"]=7,["vertex_index"]=1,["point_wcs"]=new JsonArray(5,10,0)},
            new JsonObject{["action"]="remove_vertex",["leader_line_index"]=7,["vertex_index"]=2},
            new JsonObject{["action"]="remove_line",["leader_line_index"]=8},
            new JsonObject{["action"]="remove_branch",["leader_index"]=5})};
        await test("block431 native callout topology preserves ordered actual selections",()=>
        {
            var parsed=MLeaderTopologyValidation431.Parse(Wire.Element(Request()),"$",["A1"]);
            Need(parsed.Edits.Length==5&&parsed.Edits[0].LeaderIndex==4&&parsed.Edits[1].LeaderLineIndex==7&&parsed.Edits[1].VertexIndex==1,"Native selection/order changed.");
            Need(parsed.Edits[0].Points.Length==2&&parsed.Edits[2].Points.Length==0,"Action geometry changed.");
            var fresh=Request();fresh["edits"]=new JsonArray(new JsonObject{["action"]="add_line",["vertices_wcs"]=new JsonArray(new JsonArray(0,0,0),new JsonArray(1,1,0))});
            Need(MLeaderTopologyValidation431.Parse(Wire.Element(fresh),"$",["A1"]).Edits.Single().LeaderIndex==null,"New native branch could not be requested.");
            Reject(()=>MLeaderTopologyValidation431.Batch([parsed,new EraseSpec(["A1"])]));return Task.CompletedTask;
        });
        await test("block431 callout topology rejects aliases stale-shape fields and degenerate insertion input",()=>
        {
            var missing=Request();missing.Remove("expected_geometry_token");Reject(()=>MLeaderTopologyValidation431.Parse(Wire.Element(missing),"$",["A1"]));
            var alias=Request();alias["edits"]![0]!["leader_index"]="left";Reject(()=>MLeaderTopologyValidation431.Parse(Wire.Element(alias),"$",["A1"]));
            var unknown=Request();unknown["edits"]![0]!["copy_style_from"]=7;Reject(()=>MLeaderTopologyValidation431.Parse(Wire.Element(unknown),"$",["A1"]));
            var collapsed=Request();collapsed["edits"]![0]!["vertices_wcs"]=new JsonArray(new JsonArray(1,1,0),new JsonArray(1,1,0));Reject(()=>MLeaderTopologyValidation431.Parse(Wire.Element(collapsed),"$",["A1"]));
            var plane=Request();plane["edits"]![1]!["point_wcs"]=new JsonArray(5,10,3);Reject(()=>MLeaderTopologyValidation431.Parse(Wire.Element(plane),"$",["A1"]));return Task.CompletedTask;
        });
    }
}
