using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Read the actual bound Field's existing cache. Table.value/textString are
// expression/display paths; an observation must not demand their evaluation.
// No Field evaluator, formatting setter or external expression is called here.
internal static class CadTableFieldCache365
{
    internal const string Source="actual_native_Field_existing_cache; no_Table_value_or_text_expression_getter; no_evaluation_requested";
    internal sealed record Sample(object? Value,string Text,string? ValueReason,string? TextReason);
    internal static Sample Read(Table table,ObjectId fieldId,Transaction transaction,Action cancel)
    {
        cancel();var item=transaction.GetObject(fieldId,OpenMode.ForRead);
        if(item.GetType()!=typeof(Field)||item.Database?.UnmanagedObject!=table.Database?.UnmanagedObject||item.OwnerId.IsNull)
            throw new DomainException("OUT_OF_SCOPE","Cached Table Field observation needs the actual bound local exact Field.");
        var field=(Field)item;var state=field.State;
        object? value=null;string text="";string? valueReason=null,textReason=null;
        if((state&FieldState.HasCache)==0)valueReason="Native Field has no existing value cache; observation did not evaluate its expression.";
        else
        {
            try{cancel();value=field.Value;}
            catch(OperationCanceledException){throw;}
            catch(Exception error){valueReason="Existing native Field cache unavailable: "+error.GetType().Name+"; no Table evaluation getter was used.";}
            if((state&FieldState.Modified)!=0&&valueReason==null)
                valueReason="Existing native Field cache is readable but Modified state does not prove current expression evaluation.";
        }
        if((state&FieldState.HasFormattedString)==0)
            textReason="Native Field has no existing formatted-text cache; an empty placeholder is not observed literal/display text and no formatting was requested.";
        else
        {
            try{cancel();text=field.GetStringValue();}
            catch(OperationCanceledException){throw;}
            catch(Exception error){textReason="Existing native Field formatted-text cache unavailable: "+error.GetType().Name+"; no Table display getter was used.";}
        }
        return new(value,text,valueReason,textReason);
    }
}
