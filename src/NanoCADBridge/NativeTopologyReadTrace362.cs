using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Internal diagnostics for SDK cache invalidation, not a CAD operation or
// permission to discard revisions. Records only actual changed read stages.
internal sealed class NativeTopologyReadTrace362 : IDisposable
{
    [ThreadStatic] static NativeTopologyReadTrace362? _current;
    readonly NativeTopologyReadTrace362? _prior;
    readonly DocumentState _state;
    readonly string _start;
    string _last;
    readonly List<object> _changes=[];
    int _bytes;
    int _observed;
    int _omitted;
    internal NativeTopologyReadTrace362(DocumentState state)
    {
        _state=state;_start=_last=state.Revision;_prior=_current;_current=this;
    }
    internal static void Observe(string stage,long? marker=null)
    {
        var value=_current;if(value==null)return;
        var revision=value._state.Revision;if(revision==value._last)return;
        var row=new{stage,marker,from_revision=value._last,to_revision=revision};
        value._last=revision;value._observed++;
        int size=JsonSerializer.SerializeToUtf8Bytes(row,Wire.Json).Length;
        if(value._bytes+size<=6144){value._changes.Add(row);value._bytes+=size;}
        else value._omitted++;
    }
    internal object Snapshot()
    {
        Observe("native_topology_return");
        return new{scope="actual_native_topology_read_revision_transitions; no revision waiver or rollback proof",
            start_revision=_start,end_revision=_state.Revision,observed_changes=_observed,
            omitted_changes=_omitted,complete=_omitted==0,changes=_changes};
    }
    public void Dispose(){if(ReferenceEquals(_current,this))_current=_prior;}
}
