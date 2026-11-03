using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Configure only the newly resident Section's unique native settings. The
// Section processing inventory is distinct from the sliced intersection list.
internal static class SectionSourceSelection357
{
    internal const string Scope="stored_native_SectionSettings_source_selection_by_generation_mode; separate_from_actual_complete_projection_and_sliced_intersection_inventories";
    static readonly Db.SectionType[] Kinds=[Db.SectionType.LiveSection,Db.SectionType.Section2d,Db.SectionType.Section3d];
    internal sealed record Plan(McObjectId SectionId,Db.ObjectId SectionEntity,Db.ObjectId SettingsId,JsonElement ExpectedSettings,string[] SourceHandles,McObjectId[] Sources);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool value,string message,string code="MODEL_UPDATE_FAILED")
    {if(!value)throw new DomainException(code,message);}
    static bool Gone(Db.ObjectId id)=>id.IsNull||!id.IsValid||id.IsErased||id.IsEffectivelyErased;
    static string H(Db.ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    internal static void RequireSupportedScope(CreateSectionViewEdit spec,JsonElement snapshot,Action cancel)
    {
        cancel();var common=snapshot.GetProperty("common");var requested=spec.SourceHandles.ToHashSet(StringComparer.Ordinal);
        var entities=common.GetProperty("entities").EnumerateArray().ToDictionary(row=>row.GetProperty("handle").GetString()!,StringComparer.Ordinal);
        var layers=common.GetProperty("layers").EnumerateArray().ToDictionary(row=>row.GetProperty("handle").GetString()!,StringComparer.Ordinal);
        var bodies=common.GetProperty("bodies").EnumerateArray().ToArray();var unselected=new List<object>();var selected=new List<object>();
        foreach(var body in bodies)
        {
            cancel();var handle=body.GetProperty("handle").GetString()!;var entity=entities[handle];var layer=layers[entity.GetProperty("layer").GetString()!];
            bool visible=entity.GetProperty("visible").GetBoolean()&&!layer.GetProperty("off").GetBoolean()&&!layer.GetProperty("frozen").GetBoolean();
            var row=new{handle,projection_id=body.GetProperty("projection_id"),native_type=entity.GetProperty("type"),visible,
                layer=entity.GetProperty("layer"),layer_off=layer.GetProperty("off"),layer_frozen=layer.GetProperty("frozen")};
            if(requested.Contains(handle))selected.Add(row);
            else if(visible)unselected.Add(row);
        }
        // Hidden/off/frozen observations do not prove exclusion from native
        // Section projection. They only avoid extending the confirmed visible-
        // subset refusal to an unproved case. No visibility-filtered source set
        // is inferred: Verify still requires all requested actual participants
        // exactly, including complete complex IDs, for every admitted request.
        if(unselected.Count==0)return;
        object Detail(List<object> rows)
        {
            cancel();var bytes=JsonSerializer.SerializeToUtf8Bytes(rows,Wire.Json);
            return new{count=rows.Count,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                values=bytes.Length<=2048?rows:null,first=bytes.Length<=2048?null:rows[0],
                values_unavailable_reason=bytes.Length<=2048?(string?)null:"full_inventory_exceeds_diagnostic_byte_slice"};
        }
        // SDK SampleSection/IMc3dSection: section-based root views receive their
        // projection sources from Section processing, not Set3dEntsIds. Actual
        // native361 retained all three selected-generation settings, yet both
        // root view inventories still included seven objects instead of one.
        // No scoped per-object root Section visibility setter is exposed here.
        // Stop this unsupported subset before tracking/registration, rather
        // than rewrite global body visibility or substitute sliced IDs/cropping.
        throw new DomainException("SECTION_SOURCE_SELECTION_UNSUPPORTED",
            "This native root Section path has not retained selected subsets when other visible Model solids remain. Use it only when the complete native Model solid scope is intended; this subset request is stopped before changing objects.",false,
            new{scope="root_Section_view_with_unselected_visible_top_level_Model_solids",
                selected=Detail(selected),unselected_visible=Detail(unselected),
                native_basis="SDK root Section views derive participants from Section; actual361 view top/complex sources expanded despite exact selected settings",
                retention="No source visibility, geometry, layer, ownership or existing view is edited; exact post-regeneration view participants remain mandatory for admitted requests"});
    }
    static SectionNativeBinding357.Binding<Db.Section> Native(Section section,DocumentState state,Db.Transaction tx,Action cancel)
    {
        cancel();var lookup=CadDrawingViewEditState.Context(state);var db=state.Document.Database;Db.ObjectId id=Db.ObjectId.Null;
        Need(section.ID.Handle!=0&&db.TryGetObjectId(new Db.Handle(section.ID.Handle),out id)&&!Gone(id)&&
            McObjectId.FromHandle(id.Handle.Value,lookup.ID)==section.ID,"The Section has no exact resident native identity.","OBJECT_IDENTITY_MISMATCH");
        var binding=SectionNativeBinding357.Section(tx.GetObject(id,Db.OpenMode.ForRead),db,cancel);var raw=binding.Value;
        try
        {
            var owner=tx.GetObject(raw.OwnerId,Db.OpenMode.ForRead) as Db.BlockTableRecord;
            Need(owner!=null&&owner.IsLayout&&owner.OwnerId==db.BlockTableId&&!owner.IsDependent&&!owner.IsFromExternalReference&&!owner.IsFromOverlayReference&&
                owner.Cast<Db.ObjectId>().Count(member=>{cancel();return member==id;})==1&&McObjectId.FromHandle(owner.Handle.Value,lookup.ID)==section.DbEntity.DocumentID,
                "The native Section is not uniquely resident in its exact local layout sheet.","OBJECT_IDENTITY_MISMATCH");
            return binding;
        }
        catch{binding.Dispose();throw;}
    }
    static SectionNativeBinding357.Binding<Db.SectionSettings> Settings(Db.Section raw,Db.Transaction tx,Db.OpenMode mode,Action cancel)
    {
        cancel();var id=raw.Settings;Need(!Gone(id),"Native Section settings are unavailable.","SECTION_SETTINGS_UNAVAILABLE");
        var binding=SectionNativeBinding357.Settings(tx.GetObject(id,mode),raw.Database,cancel);var value=binding.Value;
        try
        {
            Need(value.ObjectId==id&&!Gone(value.OwnerId)&&tx.GetObject(value.OwnerId,Db.OpenMode.ForRead).Database?.UnmanagedObject==raw.Database.UnmanagedObject,
                "Section settings must retain an exact resident local owner.","SECTION_SETTINGS_UNAVAILABLE");
            Need(mode!=Db.OpenMode.ForWrite||value.IsWriteEnabled,"Selected native Section settings are not actually open for writing.","SECTION_SETTINGS_UNAVAILABLE");
            return binding;
        }
        catch{binding.Dispose();throw;}
    }
    // Read existing settings, including all modes/appearance/owned data, before
    // creation. No SectionManager/global setting is rewritten or reset.
    internal static JsonElement Inventory(HostMgd.ApplicationServices.Document document,Db.Transaction tx,Action cancel)
    {
        var db=document.Database;var table=(Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead);
        var rows=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(Db.ObjectId blockId in table)
        {
            cancel();var block=(Db.BlockTableRecord)tx.GetObject(blockId,Db.OpenMode.ForRead);
            foreach(Db.ObjectId id in block)
            {
                cancel();if(Gone(id))continue;
                var entity=tx.GetObject(id,Db.OpenMode.ForRead);if(!SectionNativeBinding357.IsSection(entity,cancel))continue;
                using var binding=SectionNativeBinding357.Section(entity,db,cancel);var raw=binding.Value;
                Need(raw.ObjectId==id&&raw.OwnerId==blockId&&raw.Database?.UnmanagedObject==db.UnmanagedObject,"An existing Section has inconsistent native membership.","OBJECT_IDENTITY_MISMATCH");
                var row=new{section_handle=H(id),owner_handle=H(blockId),settings_handle=raw.Settings.IsNull?null:H(raw.Settings),settings=CadSectionCutState351.Settings(raw,tx,cancel)};
                budget.Add(row);rows.Add(row);
            }
        }
        var result=Wire.Element(rows.OrderBy(row=>JsonSerializer.Serialize(row,Wire.Json),StringComparer.Ordinal).ToArray());
        CadDrawingViewLifecycleState345.Bytes(result,cancel);return result;
    }
    internal static void Retained(JsonElement before,JsonElement after,string addedSection,Action cancel)
    {
        var old=before.EnumerateArray().ToDictionary(row=>row.GetProperty("section_handle").GetString()!,StringComparer.OrdinalIgnoreCase);
        var current=after.EnumerateArray().ToDictionary(row=>row.GetProperty("section_handle").GetString()!,StringComparer.OrdinalIgnoreCase);
        Need(current.Count==old.Count+1&&current.ContainsKey(addedSection),"Section settings membership changed outside the one new Section.");
        foreach(var row in old){cancel();Need(current.TryGetValue(row.Key,out var actual)&&row.Value.GetRawText()==actual.GetRawText(),"An existing Section's native settings or ownership changed.");}
    }
    internal static Plan Configure(Section section,McDocument sheet,McObjectId[] sources,JsonElement before,DocumentState state,Action cancel)
    {
        cancel();using var tx=state.Document.Database.TransactionManager.StartTransaction();using var native=Native(section,state,tx,cancel);var raw=native.Value;
        Need(section.DbEntity.DocumentID==sheet.ID&&sheet.IsModel&&state.Document.Database.CurrentSpaceId==raw.OwnerId,
            "Configure only the fresh Section in the exact active Model sheet.","DOCUMENT_CONTEXT_MISMATCH");
        string handle=H(raw.ObjectId);Need(before.EnumerateArray().All(row=>row.GetProperty("section_handle").GetString()!=handle),"Section source configuration cannot edit an existing Section.","OBJECT_IDENTITY_MISMATCH");
        using var settingsBinding=Settings(raw,tx,Db.OpenMode.ForWrite,cancel);var settings=settingsBinding.Value;var settingsId=settings.ObjectId;
        Need(before.EnumerateArray().All(row=>row.GetProperty("settings_handle").GetString()!=H(settingsId)),"Fresh Section reused existing native settings.","OBJECT_IDENTITY_MISMATCH");
        // A unique fresh record must not be shared by another Section, including
        // Paper or definition members; never configure borrowed defaults.
        var inventory=Inventory(state.Document,tx,cancel);
        Need(inventory.EnumerateArray().Count(row=>row.GetProperty("settings_handle").GetString()==H(settingsId))==1,
            "Fresh Section settings are shared by another resident Section.","OBJECT_IDENTITY_MISMATCH");
        var db=state.Document.Database;var lookup=CadDrawingViewEditState.Context(state);var table=(Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead);var model=table[Db.BlockTableRecord.ModelSpace];
        using var ids=new Db.ObjectIdCollection();var handles=new List<string>();var measured=new ModelInventoryBudget(cancel);
        foreach(var source in sources)
        {
            cancel();Db.ObjectId id=Db.ObjectId.Null;Need(!source.IsNull&&source.Handle!=0&&db.TryGetObjectId(new Db.Handle(source.Handle),out id)&&!Gone(id)&&
                McObjectId.FromHandle(id.Handle.Value,lookup.ID)==source,"Selected source has no exact native drawing identity.","SOURCE_SCOPE_MISMATCH");
            var body=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Solid3d;
            Need(body!=null&&body.GetType()==typeof(Db.Solid3d)&&body.Database==db&&body.OwnerId==model,"Select exact current top-level Model solids.","SOURCE_SCOPE_MISMATCH");
            measured.Add(H(id));ids.Add(id);handles.Add(H(id));
        }
        Need(ids.Count>0&&ids.Cast<Db.ObjectId>().Distinct().Count()==ids.Count,"Selected source identities are empty or repeated.","SOURCE_SCOPE_MISMATCH");
        var prior=Wire.Element(CadSectionCutState351.Settings(raw,tx,cancel));var expected=JsonNode.Parse(prior.GetRawText())!.AsObject();
        expected["CurrentSectionType"]=JsonValue.Create((int)Db.SectionType.Section2d);
        var modes=expected["modes"]!.AsArray();
        foreach(var kind in Kinds)
        {
            cancel();var generation=(settings.GenerationOptions(kind)&~Db.SectionGeneration.SourceAllObjects)|Db.SectionGeneration.SourceSelectedObjects;
            settings.SetSourceObjects(kind,ids);settings.SetGenerationOptions(kind,generation);
            var mode=modes.Single(item=>item!["kind"]!.GetValue<string>()==kind.ToString())!.AsObject();
            mode["sources"]=JsonSerializer.SerializeToNode(handles.ToArray(),Wire.Json);mode["generation"]=generation.ToString();
        }
        settings.CurrentSectionType=Db.SectionType.Section2d;
        var expectedValue=Wire.Element(expected);var actual=Wire.Element(CadSectionCutState351.Settings(raw,tx,cancel));
        ExactSettings(expectedValue,actual,"fresh_configuration",cancel);
        Need(section.State==Section.StateType.Plane&&!section.LiveSectionEnabled,"Source selection must not enable live sectioning or change the cut state.");
        tx.Commit();return new(section.ID,raw.ObjectId,settingsId,expectedValue,handles.OrderBy(value=>value,StringComparer.Ordinal).ToArray(),sources.ToArray());
    }
    internal static void Verify(Plan plan,Section section,DrawingView view,DocumentState state,Action cancel)
    {
        cancel();using var tx=state.Document.Database.TransactionManager.StartTransaction();using var native=Native(section,state,tx,cancel);var raw=native.Value;
        Need(section.ID==plan.SectionId&&raw.ObjectId==plan.SectionEntity&&raw.Settings==plan.SettingsId,
            "New native Section/settings identity changed.","OBJECT_IDENTITY_MISMATCH");
        var actual=Wire.Element(CadSectionCutState351.Settings(raw,tx,cancel));
        ExactSettings(plan.ExpectedSettings,actual,"regenerated_configuration",cancel);
        var db=state.Document.Database;var lookup=CadDrawingViewEditState.Context(state);Db.ObjectId viewId=Db.ObjectId.Null;
        Need(!view.ID.IsNull&&view.ID!=plan.SectionId&&view.ID.Handle!=0&&
            db.TryGetObjectId(new Db.Handle(view.ID.Handle),out viewId)&&!Gone(viewId)&&
            McObjectId.FromHandle(viewId.Handle.Value,lookup.ID)==view.ID,
            "The selected Section view has no exact resident native identity.","OBJECT_IDENTITY_MISMATCH");
        var representation=tx.GetObject(viewId,Db.OpenMode.ForRead);
        Need(representation.ObjectId==viewId&&representation.Database?.UnmanagedObject==db.UnmanagedObject&&
            representation.OwnerId==raw.OwnerId&&view.DbEntity.DocumentID==section.DbEntity.DocumentID&&
            view.SectionID==plan.SectionId&&view.IsBasedOnSection&&view.IsRootSectionView&&!view.IsSlice,
            "The exact resident view does not belong to its selected Section and local sheet.","OBJECT_IDENTITY_MISMATCH");
        // SDK get3dEntsForProcessing is configuration-derived Section processing
        // scope. It is not the DrawingView's selected projection/cutting list.
        // In native360 the settings retained source196 in every mode while this
        // Section inventory contained seven Model objects. Keep that observation
        // distinct; exact view participation remains a required ownership proof.
        var processing=DrawingViewSources356.Capture(section,cancel);
        var sources=DrawingViewSources356.Capture(view,cancel);
        Need(processing.native_owner_document_id==sources.native_owner_document_id&&
            sources.native_owner_document_id==section.DbEntity.DocumentID.ToString(),
            "Section processing and selected view observations belong to different native sheets.","OBJECT_IDENTITY_MISMATCH");
        try
        {
            Need(sources.complex_sources!=null,"Selected Section view has no complete native projection participation inventory.","VIEW_SOURCE_SELECTION_UNVERIFIED");
            DrawingViewSources356.Exact(sources,plan.Sources,cancel);
        }
        catch(DomainException error)
        {
            throw new DomainException(error.Error.code,"Native Section view did not retain the configured exact selected projection participants.",false,
                new{section_source_selection=ConfigurationDiagnostic(actual,cancel),
                    section_processing_inventory=new{role="configuration_derived_processing_observation_not_selected_view_comparison",
                        inventory=DrawingViewSources356.Diagnostic(CadDrawingViewEditState.Ids(plan.Sources,"Requested Section sources"),processing,cancel)},
                    selected_view_sources=DrawingViewSources356.Diagnostic(CadDrawingViewEditState.Ids(plan.Sources,"Requested Section view sources"),sources,cancel)});
        }
    }
    internal static SectionSourceSelectionRead357 Read(Section section,DocumentState state,Db.Transaction tx,Action cancel)
    {
        using var native=Native(section,state,tx,cancel);var raw=native.Value;using var settingsBinding=Settings(raw,tx,Db.OpenMode.ForRead,cancel);var settings=settingsBinding.Value;var rows=new List<SectionSourceModeRead357>();var budget=new ModelInventoryBudget(cancel);
        foreach(var kind in Kinds)
        {
            cancel();using var ids=new Db.ObjectIdCollection();settings.GetSourceObjects(kind,ids);var handles=new List<string>();
            foreach(Db.ObjectId id in ids){cancel();Need(!Gone(id)&&tx.GetObject(id,Db.OpenMode.ForRead).Database==raw.Database,"Stored Section source belongs to an unavailable/foreign drawing.","SECTION_SETTINGS_UNAVAILABLE");budget.Add(H(id));handles.Add(H(id));}
            var generation=settings.GenerationOptions(kind);var row=new SectionSourceModeRead357(kind.ToString(),(int)generation,generation.ToString(),handles.ToArray());budget.Add(row);rows.Add(row);
        }
        var result=new SectionSourceSelectionRead357(section.ID.ToString(),H(raw.ObjectId),H(settings.ObjectId),H(settings.OwnerId),settings.CurrentSectionType.ToString(),rows.ToArray(),Scope);budget.Add(result);return result;
    }
    static bool SameSettings(JsonElement expected,JsonElement actual,Action cancel)
    {
        JsonElement Normalize(JsonElement value)
        {
            var root=JsonNode.Parse(value.GetRawText())!.AsObject();
            foreach(var mode in root["modes"]!.AsArray())
            {
                cancel();var sources=mode!["sources"]!.AsArray().Select(item=>item!.GetValue<string>()).ToArray();
                Need(sources.Distinct(StringComparer.OrdinalIgnoreCase).Count()==sources.Length,"Native Section generation sources contain repeated identities.");
                mode["sources"]=JsonSerializer.SerializeToNode(sources.OrderBy(id=>id,StringComparer.Ordinal).ToArray(),Wire.Json);
            }
            return Wire.Element(root);
        }
        return Normalize(expected).GetRawText()==Normalize(actual).GetRawText();
    }
    static void ExactSettings(JsonElement expected,JsonElement actual,string stage,Action cancel)
    {
        if(!SameSettings(expected,actual,cancel))throw new DomainException("MODEL_UPDATE_FAILED","Fresh native Section source configuration or retained destination/appearance/settings differs.",false,
            new{stage,expected=ConfigurationDiagnostic(expected,cancel),actual=ConfigurationDiagnostic(actual,cancel)});
    }
    static object ConfigurationDiagnostic(JsonElement value,Action cancel)
    {
        var modes=new List<object>();
        foreach(var mode in value.GetProperty("modes").EnumerateArray())
        {
            cancel();var ids=mode.GetProperty("sources");var bytes=JsonSerializer.SerializeToUtf8Bytes(ids,Wire.Json);
            modes.Add(new{kind=mode.GetProperty("kind").GetString(),generation=mode.GetProperty("generation").GetString(),source_count=ids.GetArrayLength(),
                source_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),source_handles=bytes.Length<=512?(object)ids:null,
                source_handles_unavailable_reason=bytes.Length<=512?(string?)null:"full_source_array_exceeds_diagnostic_byte_slice"});
        }
        // Fixed three modes and byte slices keep failure details bounded. The
        // whole settings fingerprint also covers retained destinations/appearance.
        return new{settings_handle=value.GetProperty("handle").GetString(),owner=value.GetProperty("owner").GetString(),current_section_type=value.GetProperty("CurrentSectionType"),
            settings_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json))),modes};
    }
}
