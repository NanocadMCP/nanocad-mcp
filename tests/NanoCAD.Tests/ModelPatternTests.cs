using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelPatternTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string id=Guid.NewGuid().ToString("D"),feature=Guid.NewGuid().ToString("B"),pattern=Guid.NewGuid().ToString("B");
        ModifyArguments Parse(params object[] operations)=>ModifyValidation.Parse(Wire.Element(new{
            bridge_instance_id=id,document_id=id,expected_revision="1",operation_id=id,units="Millimeters",operations}));
        object Add(int x=3,int y=2)=>new{operation="add_model_rectangular_pattern",handles=new[]{"AB"},
            expected_last_item_id=feature,source_feature_id=feature,direction_x_handle="10",direction_y_handle="11",
            count_x=x,count_y=y,step_x=20,step_y=15};
        object Set()=>new{operation="set_model_rectangular_pattern",handles=new[]{"AB"},
            expected_last_item_id=pattern,feature_id=pattern,count_x=4,count_y=2,step_x=18,step_y=15,
            expected_count_x=3,expected_count_y=2,expected_step_x=20,expected_step_y=15};
        object Group(params string[] sources)=>new{operation="add_model_rectangular_pattern",handles=new[]{"AB"},
            expected_last_item_id=pattern,source_feature_ids=sources,direction_x_handle="10",direction_y_handle="11",
            count_x=1,count_y=2,step_x=20,step_y=12};
        object GroupSet(params string[] sources)=>new{operation="set_model_rectangular_pattern",handles=new[]{"AB"},
            expected_last_item_id=pattern,feature_id=pattern,expected_source_feature_ids=sources,
            count_x=1,count_y=2,step_x=20,step_y=10,expected_count_x=1,expected_count_y=2,expected_step_x=20,expected_step_y=12};
        object Element(int x=2,int y=1,bool expected=false,bool desired=true,string? last=null)=>new{operation="set_model_pattern_element",handles=new[]{"AB"},
            expected_last_item_id=last??pattern,feature_id=pattern,expected_source_feature_id=feature,
            expected_count_x=3,expected_count_y=2,expected_step_x=20,expected_step_y=15,
            index_x=x,index_y=y,expected_suppressed=expected,suppressed=desired};
        void Need(bool okay){if(!okay)throw new Exception("Native pattern contract differs.");}
        void Reject(params object[] operations){try{Parse(operations);}catch(DomainException){return;}throw new Exception("Invalid pattern request accepted.");}
        await test("native pattern element accepts later history while retaining distinct identities",()=>{
            var last=Guid.NewGuid().ToString("B");
            var edit=(ModelPatternElementSpec)Parse(Element(last:last)).Operations.Single();
            Need(edit.ExpectedLastItemId==last.ToUpperInvariant()&&edit.FeatureId==pattern.ToUpperInvariant()
                &&edit.ExpectedSourceFeatureId==feature.ToUpperInvariant());
            return Task.CompletedTask;
        });
        await test("native pattern contracts retain source directions count step and preconditions",()=>{
            var added=(ModelPatternSpec)Parse(Add()).Operations.Single();var updated=(ModelPatternSpec)Parse(Set()).Operations.Single();
            Need(added.SourceFeatureId==feature.ToUpperInvariant()&&added.DirectionXHandle=="10"&&added.CountX==3&&added.CountY==2);
            Need(updated.FeatureId==pattern.ToUpperInvariant()&&updated.ExpectedCountX==3&&updated.CountX==4);
            Need(WriteRecovery.NotGuaranteed(Parse(Add()))&&WriteRecovery.NotGuaranteed(Parse(Set())));
            return Task.CompletedTask;
        });
        await test("rectangular source group preserves original IDs independently of its later insertion anchor",()=>{
            string second=Guid.NewGuid().ToString("B");
            var added=(ModelPatternSpec)Parse(Group(feature,second)).Operations.Single();
            var updated=(ModelPatternSpec)Parse(GroupSet(feature,second)).Operations.Single();
            Need(added.SourceFeatureId==null&&added.SourceFeatureIds!.SequenceEqual(new[]{feature.ToUpperInvariant(),second.ToUpperInvariant()})&&added.ExpectedLastItemId==pattern.ToUpperInvariant());
            Need(updated.SourceFeatureIds==null&&updated.ExpectedSourceFeatureIds!.SequenceEqual(added.SourceFeatureIds)&&updated.StepY==10);
            Reject(Group());Reject(Group(feature,feature.ToUpperInvariant()));Reject(GroupSet());Reject(GroupSet(pattern));
            Reject(new{operation="add_model_rectangular_pattern",handles=new[]{"AB"},expected_last_item_id=pattern,
                source_feature_id=feature,source_feature_ids=new[]{feature},direction_x_handle="10",direction_y_handle="11",count_x=1,count_y=2,step_x=20,step_y=12});
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            Need(tool.InputSchema.GetRawText().Contains("expected_source_feature_ids")&&tool.OutputSchema!.Value.GetRawText().Contains("native_source_item_ids"));
            return Task.CompletedTask;
        });
        await test("rectangular source replacement requires an explicit original group and retains separate desired identities",()=>{
            string second=Guid.NewGuid().ToString("B");
            object Retarget(object? previous,object? desired,object replace)=>new{
                operation="set_model_rectangular_pattern",handles=new[]{"AB"},expected_last_item_id=pattern,
                feature_id=pattern,expected_source_feature_ids=previous,source_feature_ids=desired,
                count_x=4,count_y=2,step_x=20,step_y=12,
                expected_count_x=3,expected_count_y=2,expected_step_x=20,expected_step_y=12,
                replace_driver_formulas=replace};
            var edit=(ModelPatternSpec)Parse(Retarget(new[]{feature},new[]{feature,second},false)).Operations.Single();
            Need(edit.ExpectedSourceFeatureIds!.SequenceEqual(new[]{feature.ToUpperInvariant()})&&
                edit.SourceFeatureIds!.SequenceEqual(new[]{feature.ToUpperInvariant(),second.ToUpperInvariant()})&&!edit.ReplaceDriverFormulas);
            var replace=(ModelPatternSpec)Parse(Retarget(new[]{feature},new[]{second},true)).Operations.Single();
            Need(replace.ReplaceDriverFormulas);
            Reject(Retarget(null,new[]{second},false));Reject(Retarget(new[]{feature},new[]{pattern},false));
            Reject(Retarget(new[]{feature},new[]{second,second.ToUpperInvariant()},false));
            Reject(Retarget(new[]{feature},new[]{second},"true"));
            return Task.CompletedTask;
        });
        await test("native pattern rejects invalid counts directions mixed writes and exposes output",()=>{
            Parse(Add(1,1));Parse(Add(32,3));
            Reject(Add(),new{operation="move",handles=new[]{"AB"},displacement=new[]{1d,0d}});
            Reject(new{operation="add_model_rectangular_pattern",handles=new[]{"AB"},expected_last_item_id=feature,
                source_feature_id=feature,direction_x_handle="10",direction_y_handle="10",
                count_x=3,count_y=2,step_x=20,step_y=15});
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            Need(tool.InputSchema.GetRawText().Contains("add_model_rectangular_pattern")&&
                tool.InputSchema.GetRawText().Contains("set_model_rectangular_pattern")&&
                tool.OutputSchema!.Value.GetRawText().Contains("model_pattern_changes"));
            return Task.CompletedTask;
        });
        await test("native pattern element exclusion has exact zero based preconditions",()=>{
            var edit=(ModelPatternElementSpec)Parse(Element()).Operations.Single();
            Need(edit.FeatureId==pattern.ToUpperInvariant()&&edit.ExpectedSourceFeatureId==feature.ToUpperInvariant()&&
                edit.IndexX==2&&edit.IndexY==1&&!edit.ExpectedSuppressed&&edit.Suppressed);
            Need(WriteRecovery.NotGuaranteed(Parse(Element())));
            Reject(Element(0,0));Reject(Element(3,0));Reject(Element(1,2));Reject(Element(1,0,true,true));
            Reject(Element(),new{operation="move",handles=new[]{"AB"},displacement=new[]{1d,0d}});
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            Need(tool.InputSchema.GetRawText().Contains("set_model_pattern_element")&&
                tool.OutputSchema!.Value.GetRawText().Contains("model_pattern_element_changes"));
            return Task.CompletedTask;
        });
    }
}
