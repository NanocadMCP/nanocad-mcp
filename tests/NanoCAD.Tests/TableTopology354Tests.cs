using NanoCAD.Contracts;

internal static class TableTopology354Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool value,string message){if(!value)throw new Exception(message);}
        void Refuse(string code,Action action)
        {try{action();throw new Exception("Expected "+code);}catch(DomainException error)when(error.Error.code==code){}}
        await test("block354 structural references retain dollar meaning and numeric literals",()=>
        {
            Need(NativeTableFormula350.RemapStructural("=SUM($Z$2:AA4)+$AB3*1.25e-2",false,true,25,2)=="=SUM($AB$2:AC4)+$AD3*1.25e-2",
                "Structural column insertion must move dollar references with their retained cells, including column rollover, while preserving the scientific literal.");
            Need(NativeTableFormula350.RemapStructural("SUM($A$2:B4)",true,true,2,2)=="SUM($A$2:B6)","Interior insertion must expand the native aggregate rectangle.");
            Need(NativeTableFormula350.RemapStructural("SUM($A$2:B4)",true,true,1,2)=="SUM($A$4:B6)","Insertion before the range must move both endpoints, including fixed row flags.");
            return Task.CompletedTask;
        });
        await test("block354 deleted formula ranges contract without inventing missing references",()=>
        {
            Need(NativeTableFormula350.RemapStructural("SUM(A2:B5)+$C$6",true,false,2,2)=="SUM(A2:B3)+$C$4","Partial deleted range must contract and retained anchored scalar must follow its native cell.");
            Need(NativeTableFormula350.RemapStructural("AVERAGE($A$3:B6)",true,false,2,2)=="AVERAGE($A$3:B4)","A partly removed first range endpoint must become the actual first remaining row.");
            Refuse("TABLE_FORMULA_REFERENCE_REMOVED",()=>NativeTableFormula350.RemapStructural("A3+$B$7",true,false,2,2));
            Refuse("TABLE_FORMULA_REFERENCE_REMOVED",()=>NativeTableFormula350.RemapStructural("COUNT(A3:B4)",true,false,2,2));
            return Task.CompletedTask;
        });
        await test("block354 replacements use final coordinates with complete cycle and boundary admission",()=>
        {
            var input=Wire.Element(new{operation="delete_table_rows",handles=new[]{"A5"},table_token=new string('A',64),index=0,count=1,
                formula_replacements=new[]{new{row=1,column=1,formula="A1"}}});
            var parsed=TableEditingValidation.Parse(input,"$",["A5"]);
            var selected=parsed.FormulaReplacements!.Single();Need(selected.Row==1&&selected.Column==1&&selected.Formula=="A1","Replacement keeps its before-operation anchor but its expression is already in final coordinates.");
            var anchor=new TableFormulaReference343(0,1);var syntax=NativeTableFormula350.Expand(NativeTableFormula350.Inspect(selected.Formula),1,2,()=>{});
            var graph=syntax.References.ToDictionary(cell=>cell,_=>Array.Empty<TableFormulaReference343>());graph[anchor]=syntax.References;
            NativeTableFormula343.RequireAcyclic(graph,[anchor]);
            var cycle=NativeTableFormula350.Expand(NativeTableFormula350.Inspect("B1+1"),1,2,()=>{});
            var closure=cycle.References.ToDictionary(cell=>cell,_=>Array.Empty<TableFormulaReference343>());closure[anchor]=cycle.References;
            Refuse("TABLE_FORMULA_CYCLE",()=>NativeTableFormula343.RequireAcyclic(closure,[anchor]));
            Refuse("CELL_OUT_OF_RANGE",()=>NativeTableFormula350.Expand(NativeTableFormula350.Inspect("A2"),1,2,()=>{}));
            return Task.CompletedTask;
        });
    }
}
