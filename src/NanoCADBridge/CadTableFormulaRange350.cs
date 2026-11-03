using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Translate one bounded range into the existing native formula writer, after
// actual Table topology admission. Never expand rectangles in request parsing.
internal static class CadTableFormulaRange350
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is TableFormulaRangeSpec350);
    internal static ModifyArguments Expand(ModifyArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        if(args.Operations.Length!=1||args.Operations[0] is not TableFormulaRangeSpec350 edit)
            throw new DomainException("INVALID_OPERATION_ORDER","Use one native formula range operation.");
        var db=state.Document.Database;var id=CadStructureWriter.Find(db,edit.Handles.Single());
        if(tr.GetObject(id,OpenMode.ForRead) is not Table table||table.GetType()!=typeof(Table)||table.OwnerId!=CadWriteSpace.Resolve(db,tr,args.TargetSpace))
            throw new DomainException("OUT_OF_SCOPE","Select an exact existing Table in the requested ordinary space.");
        var before=CadTableFormulaEditor343.Read(table,cancel);
        CadTableToken349.Require(table,before,edit.TableToken,edit.Operation,cancel);
        if(edit.LastRow>=before.Rows||edit.LastColumn>=before.Columns)
            throw new DomainException("CELL_OUT_OF_RANGE","Formula destination rectangle is outside this exact Table.");
        var cells=new List<TableFormulaCellEdit343>();var budget=new ModelInventoryBudget(cancel);
        for(long row=edit.FirstRow;row<=edit.LastRow;row++)for(long column=edit.FirstColumn;column<=edit.LastColumn;column++)
        {
            cancel();string formula=edit.RelativeReferences?NativeTableFormula350.Shift(edit.Formula,(int)row-edit.FirstRow,(int)column-edit.FirstColumn,cancel):edit.Formula;
            var cell=new TableFormulaCellEdit343((int)row,(int)column,formula,edit.UnitType,edit.DataFormat);budget.Add(cell);cells.Add(cell);
        }
        return args with{Operations=[new TableFormulaEditingSpec343(edit.Handles,edit.TableToken,cells.ToArray(),edit.Operation)]};
    }
}
