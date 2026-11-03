using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Read the complete native complex inventory, not a guessed union of section
// processing IDs. GetTop3dEntitiesInView includes only simple top-level IDs.
internal static class DrawingViewSources356
{
    internal sealed record Complex(string[] object_ids,string top_id,string leaf_id,string target_id,
        bool simple,bool xref,bool subentity);
    internal sealed record Observed(string[] top_source_ids,Complex[]? complex_sources,string? complex_unavailable_reason,
        string[]? sliced_source_ids,string native_owner_document_id);
    internal static Observed Capture(DrawingView view,Action cancel)
    {
        cancel();var top=view.GetTop3dEntitiesInView();var sliced=view.IsBasedOnSection?CadDrawingViewEditState.Ids(view.GetSliced3dEntsIds(),"Native sliced projection sources"):null;
        return Capture(top,sliced,view.DbEntity.DocumentID.ToString(),view.GetAll3dEntitiesInView,cancel);
    }
    internal static Observed Capture(Section section,Action cancel)
    {
        cancel();return Capture(section.GetTop3dEntitiesForSectioning(),null,section.DbEntity.DocumentID.ToString(),section.GetAll3dEntitiesForSectioning,cancel);
    }
    static Observed Capture(IEnumerable<McObjectId>? topIds,string[]? sliced,string owner,Func<List<McObjectComplexId>?> getter,Action cancel)
    {
        cancel();var budget=new ModelInventoryBudget(cancel);
        var top=CadDrawingViewEditState.Ids(topIds,"Native simple projection sources");budget.Add(top);
        if(sliced!=null)budget.Add(sliced);
        var rows=new List<Complex>();string? unavailable=null;
        List<McObjectComplexId>? native=null;
        try{cancel();native=getter();}
        catch(NotImplementedException){cancel();unavailable="native_complex_projection_getter_not_implemented";}
        catch(NotSupportedException){cancel();unavailable="native_complex_projection_getter_not_supported";}
        if(native==null&&unavailable==null)unavailable="native_complex_projection_inventory_null";
        Exception? captureError=null;var cleanup=new List<Exception>();
        try
        {
            if(native!=null)foreach(var id in native)
            {
                cancel();CadDrawingViewEditState.Need(id!=null&&!id.IsNull,"A native complex projection source is null.");
                var objects=id!.Objects;
                CadDrawingViewEditState.Need(objects!=null&&objects.Count>0&&objects.All(value=>!value.IsNull),"A native projection source chain is unavailable/invalid.");
                var row=new Complex(objects!.Select(value=>value.ToString()).ToArray(),id.TopId.ToString(),id.LeafId.ToString(),id.TargetId.ToString(),id.IsSimple,id.IsXref,id.IsSubent);
                budget.Add(row);rows.Add(row);
            }
        }
        catch(Exception error){captureError=error;}
        // Getter unavailability is an observation. Disposing the native owned
        // wrappers is outside that catch and cannot be turned into success.
        if(native!=null)foreach(var item in native.Distinct(ReferenceEqualityComparer.Instance))
            if(item is IDisposable owned)try{owned.Dispose();}catch(Exception error){cleanup.Add(error);}
        if(captureError!=null&&cleanup.Count==0)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(captureError).Throw();
        if(captureError!=null)cleanup.Insert(0,captureError);
        if(cleanup.Count>0)throw new AggregateException("Native projection-source capture/cleanup failed.",cleanup);
        var ordered=rows.OrderBy(row=>JsonSerializer.Serialize(row,Wire.Json),StringComparer.Ordinal).ToArray();
        var result=new Observed(top,native==null?null:ordered,unavailable,sliced,owner);budget.Add(result);return result;
    }
    internal static void Exact(Observed actual,IEnumerable<McObjectId> requested,Action cancel)
    {
        var wanted=CadDrawingViewEditState.Ids(requested,"Requested top-level Model sources");
        bool same=wanted.SequenceEqual(actual.top_source_ids,StringComparer.Ordinal);
        if(actual.complex_sources is {} complex)
            same&=complex.Length==wanted.Length&&complex.All(row=>row.simple&&!row.xref&&!row.subentity&&row.object_ids.Length==1&&
                row.object_ids[0]==row.top_id&&row.top_id==row.leaf_id&&row.top_id==row.target_id&&wanted.Contains(row.top_id,StringComparer.Ordinal))&&
                complex.Select(row=>row.top_id).Distinct(StringComparer.Ordinal).Count()==wanted.Length;
        if(actual.sliced_source_ids is {} sliced)same&=sliced.All(id=>wanted.Contains(id,StringComparer.Ordinal));
        if(!same)throw new DomainException("MODEL_UPDATE_FAILED","Resident native view did not retain the exact requested top-level Model source identities.",false,
            Diagnostic(wanted,actual,cancel));
    }
    internal static object Diagnostic(string[] requested,Observed actual,Action cancel)
    {
        object Detail(object value,int count)
        {
            cancel();var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json);
            return new{count,sha256=Convert.ToHexString(SHA256.HashData(bytes)),value=bytes.Length<=2048?value:null,
                value_unavailable_reason=bytes.Length<=2048?(string?)null:"full_inventory_exceeds_diagnostic_byte_slice"};
        }
        return new{comparison="exact_top_level_Model_source_set; complex insertion/subentity chains are not silently flattened",
            requested=Detail(requested,requested.Length),top=Detail(actual.top_source_ids,actual.top_source_ids.Length),
            complex=actual.complex_sources==null?null:Detail(actual.complex_sources,actual.complex_sources.Length),
            actual.complex_unavailable_reason,sliced=actual.sliced_source_ids==null?null:Detail(actual.sliced_source_ids,actual.sliced_source_ids.Length),
            actual.native_owner_document_id};
    }
}
