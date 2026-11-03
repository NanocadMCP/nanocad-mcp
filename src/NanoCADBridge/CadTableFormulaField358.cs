using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Native formula bindings only. This never creates/sets/evaluates a Field,
// enumerates unknown private data keys, or treats any external Field as a value.
internal static class CadTableFormulaField358
{
    internal const string Warning="TABLE_FORMULA_FIELD_OBSERVATION: A native formula may use an actual local Field. Its cell formula flag/expression, identity, actual owner path and readable code/evaluator/format/options are observed and guarded. TableContent requires actual Table ownership and matching cell Field bindings. A layout MText carrier requires the Table's actual exclusive anonymous block, member/owner identity and matching MText.GetField()/cell FieldId; that block is not Table-owned. Its generated text cache may reflow; stored Table cells and unrelated carrier metadata remain guarded. Private Field data and internal child-field state have no complete public inventory and are not claimed complete; no external evaluator/value is substituted. Inspect the native schedule.";
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool value,string message,string code="UNSUPPORTED_TABLE_CONTENT")
    {if(!value)throw new DomainException(code,message);}
    static string Id(ObjectId id)
    {Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,"Native formula Field identity is unavailable.");return Validation.NormalizeHandle(id.Handle.ToString());}
    internal sealed class Reader:IDisposable
    {
        readonly Table table;readonly Action cancel;Transaction? transaction;readonly bool ownsTransaction;
        readonly Dictionary<ObjectId,NativeTableFormulaFieldRead358> fields=[];
        readonly ModelInventoryBudget budget;
        internal Reader(Table table,Action cancel,Transaction? transaction=null){this.table=table;this.cancel=cancel;this.transaction=transaction;ownsTransaction=transaction==null;budget=new(cancel);}
        internal NativeTableFormulaFieldRead358? Read(ObjectId id,bool nativeFormula,string expression,CellContentTypes contentType,int row,int column)
        {
            cancel();
            bool plain=contentType==CellContentTypes.Value&&id.IsNull;
            bool formula=nativeFormula&&!string.IsNullOrWhiteSpace(expression)&&contentType is CellContentTypes.Value or CellContentTypes.Field;
            Need(plain||formula,"Field/block cell content is not a proved native same-table formula; its owning content adapter is required.");
            if(id.IsNull){Need(contentType!=CellContentTypes.Field,"Native Field cell has no readable native Field identity.","OBJECT_READ_FAILED");return null;}
            Need(formula,"An external/non-formula Field cannot be admitted as a table formula.");
            Need(!table.ObjectId.IsNull&&table.Database!=null,"Native formula Field observation requires its resident Table.","OUT_OF_SCOPE");
            _=Id(id);
            if(fields.TryGetValue(id,out var prior))return prior;
            transaction??=table.Database.TransactionManager.StartTransaction();
            var value=transaction.GetObject(id,OpenMode.ForRead);
            Need(value.GetType()==typeof(Field)&&value is Field,"Native formula reference is not an exact local Field.");
            var field=(Field)value;
            Need(field.Database==table.Database&&!field.OwnerId.IsNull,"Native formula Field has no actual local ownership.","OUT_OF_SCOPE");
            // No Field.GetChildren ownership guess: its returned native wrappers
            // are outside this readable-definition adapter.
            var observed=new NativeTableFormulaFieldRead358(Id(field.ObjectId),Id(field.OwnerId),field.GetType().FullName!,
                field.GetFieldCode(),field.GetFieldCode(FieldCodeFlags.FieldCode|FieldCodeFlags.ObjectReference|FieldCodeFlags.PreserveFields),
                field.EvaluatorId,field.Format,field.EvaluationOption.ToString(),field.FilingOption.ToString(),
                field.IsTextField,field.HasFields,false,"actual_native_cell_formula_binding_and_readable_field_definition; private_child_state_unobserved");
            budget.Add(observed);fields.Add(id,observed);return observed;
        }
        internal CadTableFieldCache365.Sample Cache(ObjectId id)
        {
            Need(transaction!=null&&fields.ContainsKey(id),"Observe the actual formula Field binding/definition before its existing cache.","OBJECT_READ_FAILED");
            return CadTableFieldCache365.Read(table,id,transaction!,cancel);
        }
        internal void ValidateRoot()
        {
            cancel();if(fields.Count==0){Need(!table.HasFields,"Field-bearing native tables without proved cell formulas require their owning content adapter.");return;}
            Need(transaction!=null,"Native formula observation has no actual transaction.","OBJECT_READ_FAILED");
            _=Owned(table,transaction,cancel,fields.ToDictionary(pair=>pair.Key,pair=>Wire.Element(pair.Value)));
        }
        public void Dispose()
        {
            // Cleanup is outside all observation catches. It cannot become a
            // successful nullable observation or a safe rollback receipt.
            try{if(ownsTransaction)transaction?.Dispose();}catch(Exception error){throw new CleanupFailure(error);}
        }
    }
    internal sealed class CleanupFailure(Exception error):InvalidOperationException("Native formula Field read transaction cleanup failed.",error);
    internal static Dictionary<ObjectId,JsonElement> Bindings(Table table,Transaction tr,Action cancel)
    {
        using var reader=new Reader(table,cancel,tr);var result=new Dictionary<ObjectId,JsonElement>();
        for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
        {
            cancel();if(table.Cells[row,column].Contents.Count!=1)continue;
            var content=table.Cells[row,column].Contents[0];var id=table.GetFieldId(row,column,0);if(id.IsNull)continue;
            var value=reader.Read(id,content.HasFormula,table.GetFormula(row,column,0),table.GetContentTypes(row,column,0),row,column);
            Need(value!=null,"Formula Field observation is unavailable.","OBJECT_READ_FAILED");result[id]=Wire.Element(value);
        }
        return result;
    }
    internal static JsonElement Owned(Table table,Transaction tr,Action cancel,IReadOnlyDictionary<ObjectId,JsonElement> fields)
        =>CadTableFormulaOwnership360.Capture(table,tr,cancel,fields);
    internal static string[] Selected(CadTableEditingReader.Snapshot snapshot,IEnumerable<(int Row,int Column)> cells)
        =>cells.Select(cell=>snapshot.Values[checked(cell.Row*snapshot.Columns+cell.Column)]?.native_formula_field?.handle).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
    internal static void Verify(JsonElement before,JsonElement after,CadTableEditingReader.Snapshot old,CadTableEditingReader.Snapshot current,
        IEnumerable<(int Row,int Column)> selectedBefore,IEnumerable<(int Row,int Column)> selectedAfter,Action cancel)
    {
        var oldFields=Selected(old,selectedBefore);var newFields=Selected(current,selectedAfter);
        CadTableFormulaMTextMetadata362.Verify(before,after,old,current,oldFields,newFields,cancel);
        NativeTableFormulaMetadata358.Verify(CadTableFormulaMTextMetadata362.WithoutCarriers(CadTableFormulaMetadata360.Comparable(before,old,cancel)),
            CadTableFormulaMTextMetadata362.WithoutCarriers(CadTableFormulaMetadata360.Comparable(after,current,cancel)),oldFields,newFields,cancel);
    }
}
