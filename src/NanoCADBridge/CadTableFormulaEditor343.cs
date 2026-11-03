using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Only the native table evaluates formulas. These helpers compare stored
// expressions and independent inputs separately from native evaluated caches.
internal static class CadTableFormulaEditor343
{
    internal static Snapshot Read(Table table,Action cancel)=>Capture(table,cancel,requireWorldXy:false,allowFormattedText:true,allowNumeric:true,allowFormulas:true);
    internal static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    internal static string[] BoundedWarnings(IEnumerable<string> warnings,Action cancel)
    {
        var result=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);int bytes=2,omitted=0;
        foreach(string warning in warnings)
        {
            cancel();if(!seen.Add(warning))continue;int size=JsonSerializer.SerializeToUtf8Bytes(warning,Wire.Json).Length+1;
            if(bytes+size<=32768){result.Add(warning);bytes+=size;}else omitted++;
        }
        if(omitted>0)result.Add("TABLE_DIAGNOSTICS_COMPACTED: "+omitted+" additional distinct warning details exceed the actual32KiB diagnostic budget; stored native data was not truncated. Inspect current formula/evaluation metadata and the resulting table.");
        return result.ToArray();
    }
    internal static ContentValue NormalizeEvaluation(ContentValue before,ContentValue after)=>before.formula!=null&&before.formula==after.formula?
        before with{native_value=after.native_value,double_bits=after.double_bits,evaluation_unavailable_reason=after.evaluation_unavailable_reason,
            scale=before.auto_scale&&after.auto_scale?after.scale:before.scale}:before;
    internal static bool FormulaCell(Snapshot snapshot,CellValue cell)=>snapshot.Values[checked(cell.row*snapshot.Columns+cell.column)]?.formula!=null;
    internal static void ObserveEvaluation(Snapshot before,Snapshot after,Action cancel,List<string> warnings)
    {
        if(before.Values.Any(value=>value?.native_formula_field!=null)||after.Values.Any(value=>value?.native_formula_field!=null))warnings.Add(CadTableFormulaField358.Warning);
        var changed=new List<object>();long bytes=0;int omitted=0;bool unavailable=false;
        for(int i=0;i<before.Values.Length;i++)
        {
            cancel();var old=before.Values[i];var actual=after.Values[i];if(actual?.formula==null)continue;
            unavailable|=actual.evaluation_unavailable_reason!=null;
            if(old!=null&&old.formula==actual.formula&&Exact(old.native_value,actual.native_value)&&old.evaluation_unavailable_reason==actual.evaluation_unavailable_reason&&old.scale==actual.scale)continue;
            var cell=after.Cells[i];var observation=new{row=cell.row,column=cell.column,formula=actual.formula,before_value=old?.native_value,
                native_value=actual.native_value,evaluation_unavailable_reason=actual.evaluation_unavailable_reason,
                before_scale=old?.scale,scale=actual.scale,auto_scale=actual.auto_scale};
            int size=JsonSerializer.SerializeToUtf8Bytes(observation,Wire.Json).Length;
            if(bytes+size<=16384){changed.Add(observation);bytes+=size;}else omitted++;
        }
        if(changed.Count>0||omitted>0)warnings.Add("TABLE_FORMULA_EVALUATED_VALUES: Native formula observations="+
            JsonSerializer.Serialize(new{table_identity=after.Identity,cells=changed,omitted_diagnostics=omitted},Wire.Json)+". Inspect stored expressions, displayed values and table readability.");
        if(unavailable)warnings.Add("TABLE_FORMULA_EVALUATION_UNVERIFIED: A stored native formula has no observable finite scalar result; inspect evaluation_unavailable_reason and the rendered table. No external value was substituted.");
    }
    private static CellValue Cell(Snapshot state,TableFormulaReference343 reference)
    {
        Need(reference.Row>=0&&reference.Column>=0&&reference.Row<state.Rows&&reference.Column<state.Columns,
            "Native formula reference "+NativeTableFormula343.CellName(reference)+" is outside this table.","CELL_OUT_OF_RANGE");
        return state.Cells[checked(reference.Row*state.Columns+reference.Column)];
    }
    private static void Validate(Table table,Snapshot before,TableFormulaEditingSpec343 edit,Database db,Action cancel)
    {
        Need(!table.ObjectId.IsNull&&table.Database==db,"Formula editing requires the exact resident native Table.","OUT_OF_SCOPE");
        CadTableToken349.Require(table,before,edit.TableToken,edit.Operation,cancel);
        using(var reactors=table.GetPersistentReactorIds())Need(reactors.Count==0,"Persistent table dependencies require their owning adapter.");
        var requests=edit.Cells.ToDictionary(c=>new TableFormulaReference343(c.Row,c.Column));
        foreach(var request in edit.Cells)
        {
            cancel();var cell=Cell(before,new(request.Row,request.Column));
            Need(Anchor(cell),"Select merged formula cells by their top-left native anchor.","INVALID_ARGUMENT");
            Need(cell.content_editable&&cell.format_editable&&cell.format!=null,"Selected formula cell has protected or unavailable native content/format.");
        }
        // Examine only the selected native dependency closure. Unrelated
        // opaque formulas remain stored and never become an unrelated refusal.
        var graph=new Dictionary<TableFormulaReference343,TableFormulaReference343[]>();var pending=new Stack<TableFormulaReference343>(requests.Keys);
        var budget=new ModelInventoryBudget(cancel);var leaves=new Dictionary<TableFormulaReference343,ContentValue?>();
        var strictReferences=new HashSet<TableFormulaReference343>();
        while(pending.Count>0)
        {
            cancel();var key=pending.Pop();if(graph.ContainsKey(key))continue;
            var cell=Cell(before,key);Need(Anchor(cell),"Native formula references must address visible merged anchors, not covered cells.","INVALID_ARGUMENT");
            var value=before.Values[checked(key.Row*before.Columns+key.Column)];
            string? formula=requests.TryGetValue(key,out var request)?request.Formula:value?.formula;
            if(formula==null)
            {
                leaves.Add(key,value);graph.Add(key,Array.Empty<TableFormulaReference343>());budget.Add(new{key});continue;
            }
            Need(request!=null||value!=null&&value.data_type is "Long" or "Double","Referenced native formula has no numeric storage datatype.","UNSUPPORTED_TABLE_FORMULA");
            var syntax=NativeTableFormula350.Expand(NativeTableFormula343.Inspect(formula,cancel),before.Rows,before.Columns,cancel);
            var aggregate=(syntax.AggregateOnlyReferences??[]).ToHashSet();var refs=syntax.References;
            foreach(var reference in refs){cancel();if(!aggregate.Contains(reference))strictReferences.Add(reference);}
            graph.Add(key,refs);budget.Add(new{key,refs});
            foreach(var reference in refs){cancel();pending.Push(reference);}
        }
        // A leaf reached through any arithmetic edge stays numeric even
        // when another selected aggregate also reaches it. Native aggregates
        // alone ignore ordinary blank/String cells; no value is evaluated here.
        foreach(var (key,value) in leaves)
        {
            cancel();if(strictReferences.Contains(key))
                Need(value!=null&&value.data_type is "Long" or "Double","Native arithmetic references require an existing numeric scalar or selected/native numeric formula anchor.","UNSUPPORTED_TABLE_FORMULA");
            else Need(value==null||value.data_type is "String" or "Long" or "Double","Native aggregates require ordinary empty/String or numeric scalar cells; other content needs its adapter.","UNSUPPORTED_TABLE_FORMULA");
        }
        NativeTableFormula343.RequireAcyclic(graph,requests.Keys,cancel);
    }
    internal static void CheckSource(Table table,TableFormulaEditingSpec343 edit,Database db,Transaction tr,Action cancel)
    {var before=Read(table,cancel);Validate(table,before,edit,db,cancel);var metadata=CadTableValueEditor336.Metadata(table,tr,cancel);new ModelInventoryBudget(cancel).Add(new{before,metadata});}
    internal static string[] Apply(Table table,TableFormulaEditingSpec343 edit,Database db,Transaction tr,Action cancel)
    {
        var before=Read(table,cancel);Validate(table,before,edit,db,cancel);var metadata=CadTableValueEditor336.Metadata(table,tr,cancel);
        new ModelInventoryBudget(cancel).Add(new{before,metadata});var id=table.ObjectId;var owner=table.OwnerId;
        foreach(var request in edit.Cells)
        {
            cancel();int row=request.Row,column=request.Column;
            if(table.Cells[row,column].Contents.Count==0){table.SetValue(row,column,0.0);cancel();}
            table.SetDataType(row,column,0,new DataTypeParameter(DataType.Double,Enum.Parse<UnitType>(request.UnitType)));cancel();
            if(request.DataFormat is {} format){table.SetDataFormat(row,column,0,format);cancel();}
            table.SetFormula(row,column,0,NativeTableFormula343.NativeExpression(request.Formula,cancel));cancel();
        }
        table.GenerateLayout();cancel();table.RecomputeTableBlock(true);cancel();var after=Read(table,cancel);var warnings=new List<string>();
        var selected=edit.Cells.Select(cell=>(cell.Row,cell.Column)).ToArray();
        CadTableFormulaField358.Verify(metadata,CadTableValueEditor336.Metadata(table,tr,cancel),before,after,selected,selected,cancel);
        if(before.Values.Any(value=>value?.native_formula_field!=null)||after.Values.Any(value=>value?.native_formula_field!=null))warnings.Add(CadTableFormulaField358.Warning);
        Need(table.ObjectId==id&&table.OwnerId==owner,
            "Native formula editing changed table identity/owner or retained owned metadata.","NATIVE_TABLE_MISMATCH");
        Need(before.Rows==after.Rows&&before.Columns==after.Columns&&before.Common.GetRawText()==after.Common.GetRawText(),
            "Native formula editing changed table topology/placement/root display or table-wide defaults.","NATIVE_TABLE_MISMATCH");
        var requests=edit.Cells.ToDictionary(c=>(c.Row,c.Column));
        for(int index=0;index<before.Cells.Length;index++)
        {
            cancel();var old=before.Cells[index];var cell=after.Cells[index];var oldValue=before.Values[index];var value=after.Values[index];
            if(!requests.TryGetValue((old.row,old.column),out var request))
            {
                Need(Exact(old with{text=oldValue?.formula!=null?cell.text:old.text},cell)&&Exact(before.CellOverrides[index],after.CellOverrides[index])&&
                    (oldValue==null?value==null:value!=null&&Exact(NormalizeEvaluation(oldValue,value),value)),
                    "Native formula editing changed an unselected stored expression, independent raw input, merge/protection or cell appearance.","NATIVE_TABLE_MISMATCH");continue;
            }
            var actualValue=value??throw new DomainException("NATIVE_TABLE_MISMATCH","Requested formula has no native content.");
            var actualFormat=cell.format??throw new DomainException("NATIVE_TABLE_MISMATCH","Requested formula has no native cell format.");
            var actualFormula=actualValue.formula??throw new DomainException("NATIVE_TABLE_MISMATCH","Requested formula was not stored as a native formula.",false,
                new{row=cell.row,column=cell.column,requested_formula_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(request.Formula))),
                    observed_data_type=actualValue.data_type,observed_unit_type=actualValue.unit_type,native_formula_observed=false,
                    before_token=before.Token,after_token=after.Token,scope="completed_managed_native_formula_snapshot; no_rollback_claim"});
            Need(actualValue.data_type=="Double"&&actualValue.unit_type==request.UnitType&&cell.content_count==1,
                "Native formula/type/unit storage differs from the explicit request.","NATIVE_TABLE_MISMATCH");
            Need(NativeTableFormula343.SameMeaning(request.Formula,actualFormula,cancel),"Native stored formula changed the requested arithmetic or local cell references.","NATIVE_TABLE_MISMATCH");
            if(request.Formula!=actualFormula)warnings.Add("TABLE_FORMULA_NORMALIZED: Native formula text was normalized; read each selected cell's actual stored formula. Arithmetic/reference association and absolute-anchor flags remain checked.");
            if(request.DataFormat is {} format)Need(format.Length==0||actualFormat.data_format==format,"Native explicit formula data format differs from the request.","NATIVE_TABLE_MISMATCH");
            if(request.DataFormat==""&&actualFormat.data_format!="")warnings.Add("TABLE_DEFAULT_FORMAT_NORMALIZED: Empty formula format resolved to an observed native default; inspect data_format and rendered value.");
            var expectedFormat=old.format is {} prior?prior with{data_format=request.DataFormat!=null?actualFormat.data_format:prior.data_format}:null;
            Need(Exact(old with{text=cell.text,content_count=1,format=expectedFormat},cell)&&
                Exact(before.CellOverrides[index].Where(bit=>bit!=148).ToArray(),after.CellOverrides[index].Where(bit=>bit!=148).ToArray()),
                "Native formula editing changed merge/protection or unrequested cell/grid appearance.","NATIVE_TABLE_MISMATCH");
            if(oldValue!=null)
            {
                int mask=1|(request.DataFormat!=null?2:0);
                var expected=oldValue with{data_type=actualValue.data_type,unit_type=actualValue.unit_type,native_value=actualValue.native_value,double_bits=actualValue.double_bits,
                    formula=actualFormula,evaluation_unavailable_reason=actualValue.evaluation_unavailable_reason,overrides=actualValue.overrides,
                    native_content_type=actualValue.native_content_type,native_formula_field=actualValue.native_formula_field,
                    scale=oldValue.auto_scale&&actualValue.auto_scale?actualValue.scale:oldValue.scale};
                Need(Exact(expected,actualValue)&&(oldValue.overrides&~mask)==(actualValue.overrides&~mask),"Native formula changed unrequested content placement/scales/override bits.","NATIVE_TABLE_MISMATCH");
            }
        }
        ObserveEvaluation(before,after,cancel,warnings);
        if(!Exact(before.RowHeights,after.RowHeights)||!Exact(before.ColumnWidths,after.ColumnWidths))warnings.Add("TABLE_MINIMUM_SIZE_REFLOWED: Native formula layout changed observed row/column sizes; inspect actual table readability. Stored content, topology and placement remain checked.");
        return BoundedWarnings(warnings,cancel);
    }
}
