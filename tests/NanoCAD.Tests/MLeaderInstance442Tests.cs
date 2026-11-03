using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

internal static class MLeaderInstance442Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("MLeader442 multi-arrow text preserves every actual branch arrow and style while glyphs reflow",()=>
        {
            var before=Wire.Element(new{multi_line_snapshot=true,text="Old",content=new{raw_style="A",line_styles=new[]{new{line=17,arrow=2},new{line=33,arrow=3}}},
                lines=new[]{new{leader_index=5,leader_line_index=17,vertices_wcs=new[]{new[]{0.0,0,0},new[]{10.0,20,0}}},new{leader_index=8,leader_line_index=33,vertices_wcs=new[]{new[]{30.0,0,0},new[]{10.0,20,0}}}},
                display=new{layer="B"},layout=new{text_location=new[]{15.0,22,0},embedded=new{location=new[]{15.0,22,0},height=4.0,width=20.0},branches=new[]{new{branch=5,dogleg_length=5.0},new{branch=8,dogleg_length=5.0}}},
                connections=new[]{new{branch=5,point=new[]{15.0,20,0},landing_connections=new[]{new[]{15.0,20,0}}},new{branch=8,point=new[]{15.0,20,0},landing_connections=new[]{new[]{15.0,20,0}}}},metadata=new{handle="AF",owner="1F"}});
            var after=JsonNode.Parse(before.GetRawText())!;after["text"]="Longer replacement";after["layout"]!["text_location"]![1]=24;after["layout"]!["embedded"]!["location"]![1]=24;after["layout"]!["embedded"]!["height"]=8;
            MLeaderTextVerification.Verify(before,Wire.Element(after),"Longer replacement");
            void Reject(Action<JsonNode> mutate)
            {
                var bad=after.DeepClone();mutate(bad);
                try{MLeaderTextVerification.Verify(before,Wire.Element(bad),"Longer replacement");}catch(DomainException e)when(e.Error.code=="NATIVE_MLEADER_MISMATCH"){return;}
                throw new Exception("A changed retained branch/arrow/style/owner was accepted.");
            }
            Reject(n=>n["lines"]![1]!["vertices_wcs"]![0]![0]=31);
            Reject(n=>n["lines"]![1]!["leader_index"]=5);
            Reject(n=>n["content"]!["line_styles"]![1]!["arrow"]=4);
            Reject(n=>n["metadata"]!["owner"]="2F");
            Reject(n=>n["connections"]![1]!["point"]![0]=16);
            return Task.CompletedTask;
        });
        await test("MLeader442 instance contract binds native attributes token and explicit XY content placement",()=>
        {
            var token=new string('A',64);
            var edit=MLeaderInstanceValidation442.Parse(Wire.Element(new{operation="set_mleader_instance",handles=new[]{"AF"},instance_token=token,
                properties=new{content_position_wcs=new[]{30,40,0},arrow_size=2.5},block_attributes=new[]{new{attribute_definition_handle="b2",expected_value="7",value="8"}}}),"$",["AF"]);
            if(edit.Attributes.Single()!=new MLeaderBlockAttribute442("B2","8","7")||edit.InstanceToken!=token||edit.Properties.GetProperty("content_position_wcs")[0].GetInt32()!=30)throw new Exception("Literal attribute/source identity or explicit position lost.");
            void Reject(object value)
            {try{MLeaderInstanceValidation442.Parse(Wire.Element(value),"$",["AF"]);}catch(DomainException){return;}throw new Exception("Invalid MLeader instance edit accepted.");}
            Reject(new{operation="set_mleader_instance",handles=new[]{"AF"},instance_token=token,properties=new{content_position_wcs=new[]{1,2,3}}});
            Reject(new{operation="set_mleader_instance",handles=new[]{"AF"},instance_token=token,properties=new{arrow_size=-1}});
            Reject(new{operation="set_mleader_instance",handles=new[]{"AF"},instance_token=token,properties=new{}});
            Reject(new{operation="set_mleader_instance",handles=new[]{"AF"},instance_token=token,block_attributes=new[]{new{attribute_definition_handle="B2",expected_value="7",value="8"},new{attribute_definition_handle="b2",expected_value="7",value="9"}}});
            try{MLeaderInstanceValidation442.Batch([edit,new ReplaceMLeaderTextSpec(["AF"],"old","new")]);throw new Exception("Dependent edits bypassed the sole operation boundary.");}catch(DomainException){}
            return Task.CompletedTask;
        });
        await test("MLeader442 block balloon uses actual source handles and preserves explicit native construction intent",()=>
        {
            object Create(object[] attributes)=>new{type="MLeader",content_type="block",vertices=new[]{new[]{0,0},new[]{20,20}},block_position=new[]{25,20},block_handle="b0",block_scale=2.0,block_rotation=-.5,
                block_connection_type="ConnectBase",mleader_style="Standard",attributes};
            var spec=MLeaderInstanceValidation442.ParseCreate(Wire.Element(Create([new{attribute_definition_handle="B2",value="17"}])),"$","0");
            if(spec.BlockHandle!="B0"||spec.BlockScale!=2||spec.BlockRotation!=-.5||spec.BlockConnection!="ConnectBase"||spec.Attributes.Single().Value!="17")throw new Exception("Block balloon construction data lost.");
            var defaults=MLeaderInstanceValidation442.ParseCreate(Wire.Element(new{type="MLeader",content_type="block",vertices=new[]{new[]{0,0},new[]{20,20}},block_position=new[]{25,20},block_handle="B0",mleader_style="Standard",attributes=Array.Empty<object>()}),"$","0");
            if(defaults.BlockScale!=1||defaults.BlockRotation!=0||defaults.BlockConnection!="ConnectExtents")throw new Exception("Native defaults were guessed inconsistently.");
            try{MLeaderInstanceValidation442.ParseCreate(Wire.Element(Create([new{attribute_definition_handle="B2",value="%<field>%"}])),"$","0");throw new Exception("Field accepted as balloon literal.");}catch(DomainException){}
            return Task.CompletedTask;
        });
    }
}
