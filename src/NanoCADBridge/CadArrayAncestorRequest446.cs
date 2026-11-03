using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Send a new transitive evaluation request only across an observed dirty-child /
// up-to-date-parent boundary. No callback, status reset or network reassignment.
internal static class CadArrayAncestorRequest446
{
    sealed record Node(AssocNetwork Network,ObjectId Child,ObjectId Parent);
    static bool Live(ObjectId id,Database db)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased&&id.Database==db;
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static bool Dirty(AssocStatus status)=>status is AssocStatus.ChangedDirectlyAssocStatus or AssocStatus.ChangedTransitivelyAssocStatus;
    static bool Safe(AssocStatus status)=>status==AssocStatus.IsUpToDateAssocStatus||Dirty(status);
    static bool Contains(AssocNetwork network,ObjectId child,Action cancel)
    {
        using var actions=network.GetActions;
        bool found=false;foreach(ObjectId id in actions){cancel();if(id==child)found=true;}
        return found;
    }
    internal static string[] Apply(Entity root,Transaction tr,Action cancel,Action begin,Action<string> at)
    {
        var warnings=new List<string>();
        string[] Pending(string reason)
        {
            warnings.Add("NATIVE_ARRAY_ANCESTOR_REQUEST_PENDING: "+reason+"; no status is reset or owner reassigned. Ordinary evaluation and actual geometry inspection remain separate.");
            return warnings.ToArray();
        }
        var db=root.Database;AssocAction controller;var nodes=new List<Node>();
        try
        {
            at("native_array_ancestor_request_proof446");
            var observed=CadArrayEvaluationObservation446.Read(root,tr,cancel);
            if(!observed.complete||observed.reaches_database_top_network!=true||
                observed.controller_handle==null||observed.action_body_handle==null||
                observed.root_write_dependency_handle==null||observed.database_top_network_handle==null||
                observed.owner_chain.Length==0||observed.owner_chain.Any(n=>n.contains_observed_child_action!=true||n.status==null))
                return Pending("the current exact controller/reverse-dependency/top-network chain is incomplete");
            var actionId=CadStructureWriter.Find(db,observed.controller_handle);
            if(!Live(actionId,db)||tr.GetObject(actionId,OpenMode.ForRead) is not AssocAction actual||
                actual.IsActionBodyAProxy||actual.IsActionEvaluationInProgress||!Dirty(actual.Status))
                return Pending("the current selected-root controller is not an idle actual dirty action");
            controller=actual;
            var body=controller.ActionBody;
            if(!Live(body,db)||Id(body)!=observed.action_body_handle)
                return Pending("the current controller body identity changed");
            var dependencyId=CadStructureWriter.Find(db,observed.root_write_dependency_handle);
            if(!Live(dependencyId,db)||tr.GetObject(dependencyId,OpenMode.ForRead) is not AssocDependency dependency||
                dependency.OwningAction!=actionId||!dependency.IsWriteDependency||!dependency.IsAttachedToObject||
                dependency.DependentOnObject!=root.ObjectId||
                !(Safe(dependency.Status)||dependency.Status==AssocStatus.ChangedNoDifferenceAssocStatus))
                return Pending("the exact root reverse-write dependency changed");
            using(var identity=dependency.DependentOnCompoundObject)
                if(!identity.IsSimpleObjectId||identity.IsExternal||identity.LeafId!=root.ObjectId||!Live(identity.LeafId,db))
                    return Pending("the root reverse-write dependency no longer retains its actual simple identity");
            var top=AssocNetwork.GetInstanceFromDatabase(db,false,"");
            if(!Live(top,db)||Id(top)!=observed.database_top_network_handle)
                return Pending("the existing database top-network identity changed");
            var current=controller.OwningNetwork;var child=actionId;var seen=new HashSet<ObjectId>();
            foreach(var item in observed.owner_chain)
            {
                cancel();
                if(!Live(current,db)||!seen.Add(current)||Id(current)!=item.network_handle||
                    tr.GetObject(current,OpenMode.ForRead) is not AssocNetwork network||
                    network.Database!=db||network.IsActionEvaluationInProgress||!Safe(network.Status)||!Contains(network,child,cancel))
                    return Pending("the actual owner chain, child membership or admissible current status changed");
                var parent=network.OwningNetwork;
                if((parent.IsNull?null:Id(parent))!=item.parent_network_handle)
                    return Pending("the actual parent-network binding changed");
                nodes.Add(new(network,child,parent));child=current;current=parent;
            }
            if(!current.IsNull||nodes[^1].Network.ObjectId!=top||!nodes[^1].Parent.IsNull)
                return Pending("the complete observed chain does not terminate at the existing database top network");
        }
        catch(Teigha.Runtime.Exception error){cancel();return Pending("public ownership/status observation unavailable: "+error.ErrorStatus);}
        catch(InvalidOperationException){cancel();return Pending("public ownership/status observation has an unavailable native wrapper state");}
        catch(DomainException error)when(error.Error.code!="CANCELLED"){cancel();return Pending("public ownership/status observation unavailable: "+error.Error.code);}

        // Every node/edge is proved before the first request. Recheck the real
        // child and target immediately before each public checked status setter.
        foreach(var node in nodes)
        {
            cancel();at("native_array_ancestor_request_recheck446");
            var child=tr.GetObject(node.Child,OpenMode.ForRead) as AssocAction;
            var network=node.Network;
            if(child==null||!Live(child.ObjectId,db)||child.OwningNetwork!=network.ObjectId||
                child.IsActionEvaluationInProgress||network.IsActionEvaluationInProgress||
                network.OwningNetwork!=node.Parent||!Contains(network,child.ObjectId,cancel))
                return Pending("the actual child/parent identity changed before a request");
            var childStatus=child.Status;var prior=network.Status;
            if(!Safe(childStatus)||!Safe(prior))return Pending("a failed, suppressed, erased, no-difference or unknown status is preserved");
            if(!Dirty(childStatus)||prior!=AssocStatus.IsUpToDateAssocStatus)continue;
            at("native_array_ancestor_request_write_open446");
            if(!network.IsWriteEnabled)network.UpgradeOpen();
            childStatus=child.Status;prior=network.Status;
            if(child.OwningNetwork!=network.ObjectId||network.OwningNetwork!=node.Parent||
                child.IsActionEvaluationInProgress||network.IsActionEvaluationInProgress||
                !Contains(network,child.ObjectId,cancel)||!Safe(childStatus)||!Safe(prior))
                return Pending("the proven request boundary changed while opening its actual network");
            if(!Dirty(childStatus)||prior!=AssocStatus.IsUpToDateAssocStatus)continue;
            at("native_array_ancestor_transitive_request446");begin();
            network.SetStatus(AssocStatus.ChangedTransitivelyAssocStatus,true,false);
            var after=network.Status;
            warnings.Add("NATIVE_ARRAY_ANCESTOR_REQUESTED: Actual network "+Id(network.ObjectId)+" status "+prior+
                " -> "+after+" requested ChangedTransitivelyAssocStatus for its actual dirty child "+Id(child.ObjectId)+
                " ("+childStatus+"); notifyOwningNetwork=true, setInOwnedActions=false. This requests evaluation and does not certify regenerated items.");
            if(!Dirty(after))return Pending("the checked transitive request did not leave an actual dirty target status; it is not repeated");
            // One first actual quiet ancestor is requested. Normal native
            // notification/evaluation handles the remaining hierarchy; this
            // operation does not walk upward issuing further repair variants.
            return warnings.ToArray();
        }
        return warnings.ToArray();
    }
}

