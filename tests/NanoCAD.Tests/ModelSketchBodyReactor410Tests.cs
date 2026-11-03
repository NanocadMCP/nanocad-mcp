using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCADBridge;

internal static class ModelSketchBodyReactor410Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        JsonObject Snapshot(bool duplicates=false)=>JsonNode.Parse(duplicates
            ? """{"root":{"handle":"$CURRENT_BODY_REPRESENTATION","runtime_type":"Solid3d","reactors":["AB","AB"],"xdata":[{"app":"ENGINEERING","value":"retained intent"}]},"owned":[{"common":{"handle":"D1","owner_handle":"$CURRENT_BODY_REPRESENTATION","runtime_type":"Xrecord","reactors":["CD","CD"]},"payload":[{"code":1,"value":"user record"}]}]}"""
            : """{"root":{"handle":"$CURRENT_BODY_REPRESENTATION","runtime_type":"Solid3d","reactors":["AB"],"xdata":[{"app":"ENGINEERING","value":"retained intent"}]},"owned":[{"common":{"handle":"D1","owner_handle":"$CURRENT_BODY_REPRESENTATION","runtime_type":"Xrecord","reactors":["CD"]},"payload":[{"code":1,"value":"user record"}]}]}""")!.AsObject();
        JsonArray Links(JsonObject value,bool owned)=>(owned?value["owned"]![0]!["common"]!:value["root"]!)["reactors"]!.AsArray();
        bool Retained(JsonObject before,JsonObject after,out string reason,out JsonElement normalized,params string[] targets)=>
            ModelSketchBodyStorage364.Retained(Wire.Element(before),Wire.Element(after),[],()=>{},out reason,out normalized,
                newReactorTargets:targets.Length==0?null:targets);
        void Need(bool condition,string message){if(!condition)throw new Exception(message);}
        void Allowed(JsonObject before,JsonObject after,params string[] targets)
        {
            Need(Retained(before,after,out var reason,out var normalized,targets),"Expected preserved metadata: "+reason);
            Need(normalized.GetRawText()==Wire.Element(before).GetRawText(),"Allowed link normalization changed existing user data or multiplicity.");
        }
        void Refused(JsonObject before,JsonObject after,string component,params string[] targets)
        {
            Need(!Retained(before,after,out var reason,out _,targets),"Unproved metadata mutation was accepted.");
            Need(reason==component,"Wrong refusal component: "+reason);
        }
        await test("410 reactor exact new target may link root and existing owned record",()=>
        {
            var before=Snapshot();var after=(JsonObject)before.DeepClone();Links(after,false).Add("f2");Links(after,true).Add("F2");
            Allowed(before,after,"F2");return Task.CompletedTask;
        });
        await test("410 reactor no additions retains exact old snapshot without an allowance",()=>
        {
            var before=Snapshot();var after=(JsonObject)before.DeepClone();Allowed(before,after);
            Need(Retained(before,after,out var reason,out _)&&reason=="exact_body_owned_metadata","Unchanged metadata was reported as a waived change.");
            return Task.CompletedTask;
        });
        await test("410 reactor foreign extra link is refused at root and owned record",()=>
        {
            foreach(bool owned in new[]{false,true})
            {
                var before=Snapshot();var after=(JsonObject)before.DeepClone();Links(after,false).Add("F2");Links(after,true).Add("F2");Links(after,owned).Add("E0");
                Refused(before,after,owned?"body_owned_reactor_link_changed_without_exact_new_native_target":"body_root_reactor_link_changed_without_exact_new_native_target","F2");
            }
            return Task.CompletedTask;
        });
        await test("410 reactor old link removal remains refused despite an allowed new link",()=>
        {
            foreach(bool owned in new[]{false,true})
            {
                var before=Snapshot();var after=(JsonObject)before.DeepClone();Links(after,owned).Clear();Links(after,owned).Add("F2");
                Refused(before,after,owned?"body_owned_reactor_link_changed_without_exact_new_native_target":"body_root_reactor_link_changed_without_exact_new_native_target","F2");
            }
            return Task.CompletedTask;
        });
        await test("410 reactor duplicate old link multiplicity is retained and cannot be reduced",()=>
        {
            var before=Snapshot(duplicates:true);var after=(JsonObject)before.DeepClone();Links(after,false).Add("F2");Links(after,true).Add("F2");
            Allowed(before,after,"F2");
            foreach(bool owned in new[]{false,true})
            {
                var missing=(JsonObject)after.DeepClone();Links(missing,owned).RemoveAt(0);
                Refused(before,missing,owned?"body_owned_reactor_link_changed_without_exact_new_native_target":"body_root_reactor_link_changed_without_exact_new_native_target","F2");
            }
            return Task.CompletedTask;
        });
        await test("410 reactor target allowance never waives user payload or XData changes",()=>
        {
            var before=Snapshot();var additions=(JsonObject)before.DeepClone();Links(additions,false).Add("F2");Links(additions,true).Add("F2");
            var xdata=(JsonObject)additions.DeepClone();xdata["root"]!["xdata"]![0]!["value"]="changed intent";
            Refused(before,xdata,"body_metadata_root_changed","F2");
            var payload=(JsonObject)additions.DeepClone();payload["owned"]![0]!["payload"]![0]!["value"]="changed user record";
            Refused(before,payload,"retained_body_metadata_changed_outside_exact_new_native_storage","F2");
            return Task.CompletedTask;
        });
        await test("410 reactor old null observations persist while a new null link is refused",()=>
        {
            var before=Snapshot();Links(before,false).Add((JsonNode?)null);Links(before,true).Add((JsonNode?)null);
            var retained=(JsonObject)before.DeepClone();Links(retained,false).Add("F2");Links(retained,true).Add("F2");Allowed(before,retained,"F2");
            foreach(bool owned in new[]{false,true})
            {
                var added=(JsonObject)retained.DeepClone();Links(added,owned).Add((JsonNode?)null);
                Refused(before,added,owned?"body_owned_reactor_link_changed_without_exact_new_native_target":"body_root_reactor_link_changed_without_exact_new_native_target","F2");
            }
            return Task.CompletedTask;
        });
    }
}
