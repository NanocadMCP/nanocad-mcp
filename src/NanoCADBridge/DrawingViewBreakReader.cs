using System.Runtime.CompilerServices;
using Multicad;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class DrawingViewBreakReader
{
    internal sealed record Inventory(object[]? Items,string? UnavailableReason);
    internal static Inventory Read(McObjectId[] ids,McObjectId viewId,Dictionary<McObjectId,List<string>> layoutHandles,
        string instance,string document,Action budget)
    {
        if(ids.Length==0)return new(Array.Empty<object>(),null);
        if(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>string.Equals(a.GetName().Name,"mapinet",StringComparison.OrdinalIgnoreCase)))
            return new(null,"mapinet_not_loaded");
        return ReadKnown(ids,viewId,layoutHandles,instance,document,budget);
    }
    // Keep the optional custom-class assembly out of the JIT path for plain views.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Inventory ReadKnown(McObjectId[] ids,McObjectId viewId,Dictionary<McObjectId,List<string>> layoutHandles,
        string instance,string document,Action budget)
    {
        double Finite(double value)=>double.IsFinite(value)?value:throw new DomainException("INVALID_GEOMETRY","Nonfinite stored break parameter.");
        var items=new List<object>();var seen=new HashSet<McObjectId>();
        foreach(var id in ids)
        {
            budget();
            if(id.IsNull || !seen.Add(id))return new(null,"invalid_or_duplicate_break_identity");
            if(!layoutHandles.TryGetValue(id,out var handles) || handles.Count!=1)return new(null,"break_not_uniquely_resolved_in_layout");
            var value=id.GetObjectOfType<Mapinet.Break2d>();
            if(value==null)return new(null,"unsupported_custom_break_schema");
            if(value.ID!=id || value.TargetDrawingViewId!=viewId)return new(null,"break_target_identity_mismatch");
            var vector=value.VectSkew;
            items.Add(new{@ref=new EntityRef(instance,document,handles[0]),mc_id=id.ToString(),
                adapter_schema="mapinet_break2d_parameters_v1",class_id="{B1667CD2-2245-44F2-A9F5-A861417A0285}",
                parameter_space="native_break_parameters",break_type=value.BreakType.ToString(),break_type_index=value.BreakTypeIndex,
                gap_between_lines=Finite(value.GapBetweenLines),skew_vector=new[]{Finite(vector.X),Finite(vector.Y),Finite(vector.Z)},target_view_id=viewId.ToString()});
        }
        budget();return new(items.ToArray(),null);
    }
}
