using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadDrawingDependencies
{
    private static string Handle(ObjectId id) => Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Live(ObjectId id) => !id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased;

    internal static object Read(Entity entity, Transaction tr, Action cancel)
    {
        if (entity is BlockReference block) return Block(block, tr, cancel);
        if (entity is not Curve)
            return new { scope = "not_applicable", reason = "Select a drawing curve or block insertion." };

        var links = new List<object>();
        var budget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel);
        using var reactors = entity.GetPersistentReactorIds();
        foreach (ObjectId id in reactors)
        {
            cancel();
            object link;
            if (!Live(id)) link = new { kind = "unresolved_reactor", reason = "Native reactor identity is unavailable." };
            else
            {
                try
                {
                    var target = tr.GetObject(id, OpenMode.ForRead);
                    link = ReadLink(entity, target, tr, cancel);
                }
                catch (DomainException error) when (error.Error.code.StartsWith("UNSUPPORTED_", StringComparison.Ordinal))
                {
                    link = new { kind = "unresolved_reactor", reactor_handle = Handle(id), reason = error.Message };
                }
                catch (Teigha.Runtime.Exception error) when (!error.ErrorStatus.ToString().Contains("Lock", StringComparison.OrdinalIgnoreCase))
                {
                    link = new { kind = "unresolved_reactor", reactor_handle = Handle(id), reason = error.ErrorStatus.ToString() };
                }
            }
            budget.Add(link);
            links.Add(link);
        }
        return new { scope = "selected_curve_persistent_reactors", source_handle = Handle(entity.ObjectId), links };
    }

    private static object ReadLink(Entity source, DBObject target, Transaction tr, Action cancel)
    {
        string handle = Handle(target.ObjectId);
        if (target is Hatch hatch)
        {
            var loops = new List<object>();
            var loopBudget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel);
            bool found = false;
            for (int i = 0; i < hatch.NumberOfLoops; i++)
            {
                cancel();
                using var ids = hatch.GetAssociatedObjectIdsAt(i);
                var sources = new List<string>();
                var memberBudget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel);
                foreach (ObjectId id in ids)
                {
                    cancel();
                    if (!Live(id)) return new { kind = "unresolved_reactor", reactor_handle = handle, reason = "Hatch loop contains an unavailable boundary." };
                    string name = Handle(id);
                    memberBudget.Add(name);
                    sources.Add(name);
                    found |= id == source.ObjectId;
                }
                var loop = new { index = i, loop_type = hatch.GetLoopAt(i).LoopType.ToString(), source_handles = sources };
                loopBudget.Add(loop);
                loops.Add(loop);
            }
            if (found && hatch.Associative && hatch.OwnerId == source.OwnerId)
                return new { kind = "associative_hatch", hatch_handle = handle, loops };
            return new { kind = "unresolved_reactor", reactor_handle = handle, reason = "The Hatch has no reciprocal local associative loop for this source." };
        }
        using var binding = CadNativeDimAssocBinding434.TryBind(target);
        if (binding != null)
        {
            var dimensionId = binding.Value.DimObjId;
            if (Live(dimensionId) && tr.GetObject(dimensionId, OpenMode.ForRead) is Dimension dimension && dimension.OwnerId == source.OwnerId)
            {
                var state = CadDimensionAssociation385.Capture(dimension, source.Database, tr, cancel);
                if (state.Association == target.ObjectId && state.Active)
                {
                    var matches = state.Points.Where(p => p.Source == source.ObjectId)
                        .Select(p => new { role = p.Role, native_snap = p.Snap.ToString() }).ToArray();
                    if (matches.Length > 0)
                        return new
                        {
                            kind = "associated_dimension",
                            dimension_handle = Handle(dimensionId),
                            association_handle = handle,
                            points = matches,
                            circle_geometry_edit = source is Circle && dimension is DiametricDimension diameter
                                ? CadDimensionAssociation385.CircleLabelCondition(diameter) : null
                        };
                }
            }
            return new { kind = "unresolved_reactor", reactor_handle = handle, reason = "No matching active whole-entity dimension point was verified." };
        }
        if (target is Group && CadDrawingGroups431.OrganizationalReactor(source, target.ObjectId, tr, cancel))
            return new { kind = "selection_group", group_handle = handle };
        return new { kind = "other_reactor", reactor_handle = handle, runtime_type = target.GetType().Name };
    }

    private static object Block(BlockReference insertion, Transaction tr, Action cancel)
    {
        var definition = (BlockTableRecord)tr.GetObject(insertion.BlockTableRecord, OpenMode.ForRead);
        var direct = new List<string>();
        var budget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel);
        bool found = false;
        using var ids = definition.GetBlockReferenceIds(true, false);
        foreach (ObjectId id in ids)
        {
            cancel();
            if (!Live(id) || tr.GetObject(id, OpenMode.ForRead) is not BlockReference related || related.BlockTableRecord != definition.ObjectId)
                throw new DomainException("BLOCK_REFERENCE_UNAVAILABLE", "The definition's direct insertion inventory contains an unresolved reference.");
            found |= id == insertion.ObjectId;
            string name = Handle(id);
            budget.Add(name);
            direct.Add(name);
        }
        return new
        {
            scope = "selected_block_insertion",
            occurrence_handle = Handle(insertion.ObjectId),
            definition_handle = Handle(definition.ObjectId),
            definition_name = definition.Name,
            definition_scope = insertion.IsDynamicBlock ? "evaluated_dynamic_block_table_record" : "block_table_record",
            dynamic_authoring_definition_handle = insertion.IsDynamicBlock && Live(insertion.DynamicBlockTableRecord)
                ? Handle(insertion.DynamicBlockTableRecord) : null,
            external_definition = definition.IsFromExternalReference || definition.IsFromOverlayReference,
            direct_reference_handles = direct,
            selected_occurrence_in_inventory = found,
            direct_reference_inventory = found ? "observed_native_direct_references" : "incomplete_selected_occurrence_absent",
            reference_scope = "direct references reported for this block table record; nested parents and array cells are not expanded",
            inspect_block = new { handle = Handle(insertion.ObjectId) }
        };
    }
}
