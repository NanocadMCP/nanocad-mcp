namespace NanoCAD.Contracts;

public sealed record ModelSequenceLink(string Id,string? Previous);
public static class ModelFeatureSequence
{
    public static string[] FromHistory(ModelSequenceLink[] history,string[] featureIds,string root,string last,Action? cancel=null)
    {
        void Need(bool ok){if(!ok)throw new DomainException("MODEL_ORDER_UNAVAILABLE","Native predecessor links must identify one complete feature chain through this body's history.");}
        Need(history.Length>=1&&featureIds.Length>=1);
        var nodes=new Dictionary<string,ModelSequenceLink>(StringComparer.OrdinalIgnoreCase);
        foreach(var n in history){cancel?.Invoke();Need(nodes.TryAdd(n.Id,n));}
        var features=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in featureIds){cancel?.Invoke();Need(features.Add(id)&&nodes.ContainsKey(id));}
        var predecessors=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        string? Previous(string id)
        {
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){id};var before=nodes[id].Previous;
            var path=new List<string>();string? result=null;
            while(before!=null)
            {
                cancel?.Invoke();
                Need(seen.Add(before)&&nodes.ContainsKey(before));
                if(features.Contains(before)){result=before;break;}
                if(predecessors.TryGetValue(before,out result))break;
                path.Add(before);
                before=nodes[before].Previous;
            }
            foreach(var node in path){cancel?.Invoke();predecessors[node]=result;}
            return result;
        }
        var links=new List<ModelSequenceLink>();
        foreach(var id in featureIds){cancel?.Invoke();links.Add(new(id,Previous(id)));}
        // LastPartItemID can be trailing construction. Derive one forming tip
        // from the actual collapsed links, never from inventory enumeration.
        // The caller retains the actual terminal and every assigned edge.
        Need(nodes.ContainsKey(last));
        var followed=links.Where(link=>link.Previous!=null).Select(link=>link.Previous!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tips=featureIds.Where(id=>!followed.Contains(id)).ToArray();Need(tips.Length==1);
        if(features.Contains(last))Need(last.Equals(tips[0],StringComparison.OrdinalIgnoreCase));
        else
        {
            // SDK exposes assigned ordering only; null is not a manufactured
            // predecessor. A present trailing link must reach the same tip.
            string? assignedTip=Previous(last);Need(assignedTip==null||assignedTip.Equals(tips[0],StringComparison.OrdinalIgnoreCase));
        }
        return Order(links.ToArray(),root,tips[0],cancel);
    }
    public static string[] Order(ModelSequenceLink[] features,string root,string last,Action? cancel=null)
    {
        void Need(bool ok){if(!ok)throw new DomainException("MODEL_ORDER_UNAVAILABLE","Expected one explicitly ordered feature chain from projected base to last feature.");}
        Need(features.Length>=1);
        var byId=new Dictionary<string,ModelSequenceLink>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in features){cancel?.Invoke();Need(byId.TryAdd(f.Id,f));}Need(byId.ContainsKey(root)&&byId.ContainsKey(last));
        Need(byId[root].Previous==null||!byId.ContainsKey(byId[root].Previous!));
        var next=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in features.Where(f=>!f.Id.Equals(root,StringComparison.OrdinalIgnoreCase)))
        {cancel?.Invoke();Need(f.Previous!=null&&byId.ContainsKey(f.Previous)&&next.TryAdd(f.Previous,f.Id));}
        var result=new List<string>();var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string current=root;
        while(true){cancel?.Invoke();Need(visited.Add(current));result.Add(current);if(!next.TryGetValue(current,out var following))break;current=following;}
        Need(result.Count==features.Length&&current.Equals(last,StringComparison.OrdinalIgnoreCase));return result.ToArray();
    }
}
