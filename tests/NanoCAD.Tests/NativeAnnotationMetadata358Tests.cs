using System.Text.Json;
using NanoCAD.Contracts;

internal static class NativeAnnotationMetadata358Tests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static JsonElement Common(string id,string owner)=>Wire.Element(new{handle=id,owner_handle=owner,runtime_type="native",extension_handle=(string?)null,xdata=(object?)null,reactors=Array.Empty<string>()});
    static JsonElement Field(string code)=>Wire.Element(new{common=Common("F","D"),table_formula_field=new{handle="F",owner_handle="D",field_code=code,object_reference_code=code,evaluator_id="native_formula",private_state_complete=false}});
    static JsonElement Dictionary(string id,string owner,object[] entries)=>Wire.Element(new{common=Common(id,owner),dictionary_hard_owned=true,entries});
    static object Edge(string name,string child,string owner,bool owned=true)=>new{name,handle=child,owner_handle=owner,runtime_type="native",owned};
    static JsonElement Table(params JsonElement[] records)=>Wire.Element(new{handle="A",owner="B",extension="D",xdata=(object?)null,owned=records});
    static void Rejected(Action action)
    {try{action();throw new Exception("Unexpected metadata mutation was admitted.");}catch(DomainException e)when(e.Error.code=="NATIVE_TABLE_MISMATCH") {}}
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block358 formula ownership normalization preserves non-owning aliases",()=>
        {
            var root=Dictionary("D","A",[Edge("formula","F","D"),Edge("aliases","E","D")]);var alias=Dictionary("E","D",[Edge("alias","F","D",false)]);
            var before=Table(root,alias,Field("=SUM(B2:B3)"));var after=Table(root,alias,Field("=SUM(B2:B4)"));
            NativeTableFormulaMetadata358.Verify(before,after,["F"],["F"],()=>{});
            Rejected(()=>NativeTableFormulaMetadata358.Verify(before,Table(root,Dictionary("E","D",[]),Field("=SUM(B2:B4)")),["F"],["F"],()=>{}));
            Rejected(()=>NativeTableFormulaMetadata358.Verify(before,Table(root,alias,Field("=SUM(B2:B4)")),[],[],()=>{}));
            return Task.CompletedTask;
        });
        await test("block358 formula retirement preserves unrelated dictionary content and order",()=>
        {
            var retained=Wire.Element(new{common=Common("X","D"),stored="retained"});
            var before=Table(Dictionary("D","A",[Edge("left","X","D"),Edge("formula","F","D"),Edge("soft","Z","O",false)]),Field("=B2"),retained);
            var after=Table(Dictionary("D","A",[Edge("left","X","D"),Edge("soft","Z","O",false)]),retained);
            NativeTableFormulaMetadata358.Verify(before,after,["F"],[],()=>{});
            Rejected(()=>NativeTableFormulaMetadata358.Verify(before,Table(Dictionary("D","A",[Edge("soft","Z","O",false),Edge("left","X","D")]),retained),["F"],[],()=>{}));
            Rejected(()=>NativeTableFormulaMetadata358.Verify(before,Table(Dictionary("D","A",[Edge("left","X","D")]),retained),["F"],[],()=>{}));
            return Task.CompletedTask;
        });
        await test("block358 additive dimension XData retains old groups and refuses references",()=>
        {
            object App(string name)=>new{code=1001,value=name};object Text(string value)=>new{code=1000,value};
            JsonElement Metadata(object[]? data,string owner="B")=>Wire.Element(new{owner,xdata=data,owned=Array.Empty<object>()});
            var before=Metadata([App("OLD"),Text("retain")]);var after=Metadata([App("NATIVE"),Text("new"),App("OLD"),Text("retain")]);
            Need(DimensionAddedXData358.MetadataPreserved(before,after,()=>{},out var added)&&added.GetArrayLength()==1,"Nonreferential addition lost its precise typed observation.");
            Need(!DimensionAddedXData358.MetadataPreserved(before,Metadata([App("OLD"),Text("changed"),App("NATIVE"),Text("new")]),()=>{},out _),"Old group changed.");
            Need(!DimensionAddedXData358.MetadataPreserved(before,Metadata([App("OLD"),Text("retain"),App("NATIVE"),new{code=1005,value="FF"}]),()=>{},out _),"Unrequested persistent handle admitted.");
            Need(!DimensionAddedXData358.MetadataPreserved(before,Metadata([App("OLD"),Text("retain"),App("NATIVE"),Text("new")],"foreign"),()=>{},out _),"Root owner changed.");
            var warnings=new List<string>{"Unrelated warning retained."};
            // Response escaping and cumulative observations must stay inside the
            // prewrite reserve without imposing a dimension-count ceiling.
            for(int index=0;index<100;index++)DimensionAddedXDataWarning358.Append(warnings,index.ToString("X"),Wire.Element(new{typed=new string('"',10000)}),()=>{});
            Need(warnings.Count==2&&warnings[0]=="Unrelated warning retained.","Observation aggregation lost another warning or grew by entity count.");
            Need(JsonSerializer.SerializeToUtf8Bytes(warnings[1],Wire.Json).Length<=DimensionAddedXDataWarning358.SerializedWarningByteBudget,"Escaped aggregate warning exceeds its reserved bytes.");
            Need(warnings[1].Contains("\"observation_count\":100",StringComparison.Ordinal)&&warnings[1].Contains("\"complete\":false",StringComparison.Ordinal),"Compacted additions must disclose their count and omitted typed sample.");
            return Task.CompletedTask;
        });
    }
}
