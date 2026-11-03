using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;
internal static class BasicModelingTests
{
 static CreateArguments Create(string json)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=Guid.NewGuid(),document_id=Guid.NewGuid(),operation_id=Guid.NewGuid(),expected_revision="1",units="Millimeters",entities=new[]{JsonSerializer.Deserialize<JsonElement>(json)}}));
 static ModifyArguments Edit(JsonObject json)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=Guid.NewGuid(),document_id=Guid.NewGuid(),operation_id=Guid.NewGuid(),expected_revision="1",units="Millimeters",operations=new[]{json}}));
 internal static async Task Run(Func<string,Func<Task>,Task> test){
  await test("basic modeling general contracts preserve profile path and modes",()=>{
   var sweep=Create("""{"type":"Solid3d","kind":"sweep","profile_handle":"A","path_handle":"B","base_point":[0,0,0],"align":false,"bank":true,"twist_angle":1.2,"scale_factor":2}""").Entities.Single() as CurveSolidSpec;
   if(sweep==null||sweep.PathHandle!="B"||sweep.Profiles[0]!="A"||sweep.Twist!=1.2||sweep.Scale!=2||!sweep.Bank)throw new Exception("Sweep parameters lost");
   var loft=Create("""{"type":"Solid3d","kind":"loft","section_handles":["A","B","C"],"guide_handles":["D"],"path_handle":null,"interpolation":"ruled"}""").Entities.Single() as CurveSolidSpec;
   if(loft==null||!loft.Ruled||loft.Profiles.Length!=3||loft.Guides.Single()!="D")throw new Exception("Loft data lost");return Task.CompletedTask;
  });
  await test("basic modeling parametric sweep recovery and closed profile validation",()=>{
   const string request="""{"type":"Solid3d","kind":"model_sweep","plane":{"origin":[0,0,0],"x_axis":[1,0,0],"y_axis":[0,1,0]},"profile":{"kind":"circle","center":[0,0],"radius":2},"path_handles":["AB"],"orientation":"normal_to_path","taper_angle":0}""";
   var a=Create(request);if(a.Entities.Single() is not ModelSweepSpec||!WriteRecovery.NotGuaranteed(a))throw new Exception("Native sweep routing/recovery");
   try{Create(request.Replace("normal_to_path","invented"));throw new Exception("Bad orientation accepted");}catch(DomainException){}return Task.CompletedTask;
  });
  await test("basic modeling face edits reject ambiguous topology references",()=>{
   var e=JsonNode.Parse("""{"operation":"presspull_faces","handles":["A"],"topology_token":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","faces":["1"],"distance":-2}""")!.AsObject();
   var a=Edit(e);if(a.Operations.Single() is not SolidFaceEditSpec f||f.Distance!=-2||!WriteRecovery.NotGuaranteed(a))throw new Exception("Face edit routing");
   e["faces"]=new JsonArray("1","1");try{Edit(e);throw new Exception("Duplicate face accepted");}catch(DomainException){}return Task.CompletedTask;
  });
  await test("basic modeling extrusion termination rejects a missing face binding",()=>{
   var e=JsonNode.Parse("""{"operation":"set_model_extrusion_extent","handles":["A"],"expected_last_item_id":"{11111111-1111-1111-1111-111111111111}","feature_id":"{22222222-2222-2222-2222-222222222222}","definition":{"extent":"through_all","direction":"negative","distance":5,"taper_angle":0}}""")!.AsObject();
   if(Edit(e).Operations.Single() is not ModelManufacturingEdit)throw new Exception("Extent route missing");
   e["definition"]!["extent"]="to_face";try{Edit(e);throw new Exception("Unbound terminal accepted");}catch(DomainException){}return Task.CompletedTask;
  });
  await test("basic modeling published schema exposes standard operations",()=>{
   var create=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities").InputSchema.GetRawText();var edit=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetRawText();
   foreach(var k in new[]{"profile_handle","section_handles","model_sweep","model_loft","Helix","subtract_boundaries"})if(!create.Contains(k))throw new Exception("Missing "+k);
   foreach(var k in new[]{"presspull_faces","transform_faces","taper_faces","remove_faces","extrude_faces","set_model_sweep","add_model_loft","axis_start","set_model_extrusion_extent","target_body_handle"})if(!edit.Contains(k))throw new Exception("Missing "+k);
   if(edit.Contains("cut_helical_thread")||ServerGuidance.Instructions.Contains("cut_helical_thread"))throw new Exception("Abandoned thread API still published");return Task.CompletedTask;
  });
 }
}
