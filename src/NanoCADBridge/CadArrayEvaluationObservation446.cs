using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Public managed getters only. This is optional current diagnostic information,
// never an editing token, a repair instruction or a scheduling/evaluation call.
internal static class CadArrayEvaluationObservation446
{
    const string Scope="Read-only current selected-root controller/reverse-write dependency and actual owning-network membership chain. Statuses are transient. No dirty marking, evaluation, creation, network ownership changes or inferred evaluation-queue contents.";
    static bool Live(ObjectId id,Database db)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased&&id.Database==db;
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    internal static AssociativeArrayEvaluationObservation446 Read(Entity root,Transaction tr,Action cancel)
    {
        string? controllerHandle=null,controllerStatus=null,bodyHandle=null,dependencyHandle=null,dependencyStatus=null,ownerHandle=null,topHandle=null;
        bool? reachesTop=null;var chain=new List<AssociativeArrayNetworkObservation446>();
        var db=root.Database;var budget=new CadContourBudget344(cancel);
        AssociativeArrayEvaluationObservation446 Result(bool complete,string? reason=null)=>new(
            complete?"observed":controllerHandle==null?"unavailable":"partial",complete,reason,
            controllerHandle,controllerStatus,bodyHandle,dependencyHandle,dependencyStatus,ownerHandle,topHandle,reachesTop,chain.ToArray(),Scope);
        try
        {
            cancel();
            // false is essential: ordinary inspection never creates a network.
            var top=AssocNetwork.GetInstanceFromDatabase(db,false,"");
            if(!top.IsNull)
            {
                if(!Live(top,db))return Result(false,"database_top_network_identity_not_live");
                topHandle=Id(top);reachesTop=false;
            }
            using var ids=AssocAction.GetActionsDependentOnObject(root,false,true);
            var candidates=new HashSet<ObjectId>();
            foreach(ObjectId id in ids)
            {
                cancel();if(!Live(id,db))return Result(false,"selected_root_write_controller_identity_not_live");
                candidates.Add(id);
            }
            if(candidates.Count!=1)return Result(false,"selected_root_write_controller_count="+candidates.Count);
            var selected=candidates.Single();controllerHandle=Id(selected);
            if(tr.GetObject(selected,OpenMode.ForRead) is not AssocAction controller)
                return Result(false,"selected_root_controller_not_readable_as_native_action");
            controllerStatus=controller.Status.ToString();
            var body=controller.ActionBody;if(Live(body,db))bodyHandle=Id(body);
            if(controller.IsActionBodyAProxy)return Result(false,"selected_root_controller_has_proxy_action_body");
            using var dependencies=controller.GetDependencies(false,true);
            ObjectId reverse=ObjectId.Null;
            foreach(ObjectId id in dependencies)
            {
                cancel();
                if(!Live(id,db)||tr.GetObject(id,OpenMode.ForRead) is not AssocDependency dependency||
                    dependency.OwningAction!=selected||!dependency.IsWriteDependency)
                    return Result(false,"reverse_write_dependency_inventory_unavailable_or_inconsistent");
                if(dependency.DependentOnObject!=root.ObjectId)continue;
                using var identity=dependency.DependentOnCompoundObject;
                if(!dependency.IsAttachedToObject||!identity.IsSimpleObjectId||identity.IsExternal||
                    identity.LeafId!=root.ObjectId||!Live(identity.LeafId,db))
                    return Result(false,"selected_root_reverse_write_dependency_not_exact_live_simple_identity");
                if(!reverse.IsNull&&reverse!=id)return Result(false,"multiple_selected_root_reverse_write_dependencies");
                reverse=id;dependencyHandle=Id(id);dependencyStatus=dependency.Status.ToString();
            }
            if(reverse.IsNull)return Result(false,"selected_root_reverse_write_dependency_not_observed");
            var current=controller.OwningNetwork;
            if(!current.IsNull)ownerHandle=Id(current);
            ObjectId child=selected;var seen=new HashSet<ObjectId>();
            while(!current.IsNull)
            {
                cancel();
                if(!Live(current,db))return Result(false,"owning_network_identity_not_live");
                if(!seen.Add(current))return Result(false,"owning_network_cycle_at="+Id(current));
                if(tr.GetObject(current,OpenMode.ForRead) is not AssocNetwork network)
                    return Result(false,"owner_chain_member_not_readable_as_native_network");
                string? status=null,parentHandle=null,reason=null;bool? contains=null;
                ObjectId parent;
                try{status=network.Status.ToString();}
                catch(Teigha.Runtime.Exception error){reason="status_unavailable:"+error.ErrorStatus;}
                catch(InvalidOperationException){reason="status_unavailable:invalid_native_wrapper_state";}
                try
                {
                    using var actions=network.GetActions;bool found=false;
                    foreach(ObjectId id in actions){cancel();if(id==child)found=true;}
                    contains=found;
                }
                catch(Teigha.Runtime.Exception error){reason=(reason==null?"":reason+";")+"membership_unavailable:"+error.ErrorStatus;}
                catch(InvalidOperationException){reason=(reason==null?"":reason+";")+"membership_unavailable:invalid_native_wrapper_state";}
                parent=network.OwningNetwork;
                if(!parent.IsNull)
                {
                    if(!Live(parent,db))return Result(false,"parent_network_identity_not_live");
                    parentHandle=Id(parent);
                }
                bool? isTop=top.IsNull?null:current==top;
                var node=new AssociativeArrayNetworkObservation446(Id(current),status,parentHandle,contains,isTop,reason);
                budget.Add(node);chain.Add(node);
                if(isTop==true)reachesTop=true;
                if(reason!=null)return Result(false,"owner_chain_contains_unavailable_status_or_membership");
                child=current;current=parent;
            }
            return Result(true);
        }
        catch(Teigha.Runtime.Exception error){cancel();return Result(false,"public_native_read_unavailable:"+error.ErrorStatus);}
        catch(InvalidOperationException){cancel();return Result(false,"public_native_read_unavailable:invalid_native_wrapper_state");}
        catch(DomainException error)when(error.Error.code!="CANCELLED"){cancel();return Result(false,"optional_observation_unavailable:"+error.Error.code);}
    }
}

