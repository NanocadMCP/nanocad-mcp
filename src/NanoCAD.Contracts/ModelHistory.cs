using System.Text.Json;

namespace NanoCAD.Contracts;

public static class ModelHistoryValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new DomainException("INVALID_ARGUMENT", "Expected object.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in input.EnumerateObject())
            if (!values.TryAdd(property.Name, property.Value)) throw new DomainException("INVALID_ARGUMENT", "Duplicate argument: " + property.Name);
        string[]? nativeSketchIds=null;bool includeSketches=false;string? sketchCursor=null;
        if(values.Remove("sketch_ids",out var nativeSketches))nativeSketchIds=ModelSketchLifecycleValidation362.Ids(nativeSketches,"sketch_ids");
        if(values.Remove("include_document_sketches",out var sketchInventory))
        {
            if(sketchInventory.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new DomainException("INVALID_ARGUMENT","include_document_sketches must be boolean.");
            includeSketches=sketchInventory.GetBoolean();
        }
        if(values.Remove("document_sketch_cursor",out var sketchPage))
        {
            if(!includeSketches||sketchPage.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(sketchPage.GetString(),"B",out var sketchGuid)||sketchGuid==Guid.Empty)
                throw new DomainException("INVALID_ARGUMENT","document_sketch_cursor requires include_document_sketches=true and the preceding native identity page's next_cursor.");
            sketchCursor=sketchGuid.ToString("B").ToUpperInvariant();
        }
        string[]? parameterIds=values.Remove("sketch_constraint_parameter_ids",out var parameterSelection)?ModelSketchConstraintParameterValidation379.Ids(parameterSelection,"sketch_constraint_parameter_ids"):null;
        string[]? deletionIds=null;
        if(values.Remove("feature_deletion_ids",out var deletion))deletionIds=ModelFeatureDeletionValidation378.Ids(deletion,"feature_deletion_ids");
        string[]? elementIds=null;
        if(values.Remove("profile_element_ids",out var elements))elementIds=ModelProfileElementsValidation358.Ids(elements,"profile_element_ids");
        bool includeProfiles=false;string? profileCursor=null;string[]? profileIds=null;
        if(values.Remove("include_document_profiles",out var include))
        {
            if(include.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new DomainException("INVALID_ARGUMENT","include_document_profiles must be boolean.");
            includeProfiles=include.GetBoolean();
        }
        if(values.Remove("document_profile_cursor",out var cursor))
        {
            if(!includeProfiles||cursor.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(cursor.GetString(),"B",out var id)||id==Guid.Empty)
                throw new DomainException("INVALID_ARGUMENT","document_profile_cursor requires include_document_profiles=true and the preceding identity page's next_cursor.");
            profileCursor=id.ToString("B").ToUpperInvariant();
        }
        if(values.Remove("profile_ids",out var profiles))
        {
            if(profiles.ValueKind!=JsonValueKind.Array||profiles.GetArrayLength()<1)throw new DomainException("INVALID_ARGUMENT","Choose one or more exact native profile IDs.");
            var unique=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in profiles.EnumerateArray())
            {
                if(item.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(item.GetString(),"B",out var id)||id==Guid.Empty||!unique.Add(id.ToString("B").ToUpperInvariant()))
                    throw new DomainException("INVALID_ARGUMENT","profile_ids must be distinct current nonempty native braced GUIDs.");
            }
            profileIds=unique.ToArray();
        }
        bool includeDatums=false;string? datumCursor=null;string[]? datumIds=null;
        if(values.Remove("include_document_datum_planes",out var datumFlag))
        {
            if(datumFlag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new DomainException("INVALID_ARGUMENT","include_document_datum_planes must be boolean.");
            includeDatums=datumFlag.GetBoolean();
        }
        if(values.Remove("document_datum_cursor",out var datumPage))
        {
            if(!includeDatums||datumPage.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(datumPage.GetString(),"B",out var datum)||datum==Guid.Empty)
                throw new DomainException("INVALID_ARGUMENT","document_datum_cursor requires include_document_datum_planes=true and the preceding identity page's next_cursor.");
            datumCursor=datum.ToString("B").ToUpperInvariant();
        }
        if(values.Remove("datum_plane_ids",out var datums))
        {
            if(datums.ValueKind!=JsonValueKind.Array||datums.GetArrayLength()<1)throw new DomainException("INVALID_ARGUMENT","Choose exact native WorkPlane IDs.");
            var unique=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in datums.EnumerateArray())
                if(item.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(item.GetString(),"B",out var datum)||datum==Guid.Empty||!unique.Add(datum.ToString("B").ToUpperInvariant()))
                    throw new DomainException("INVALID_ARGUMENT","datum_plane_ids must be distinct nonempty current braced GUIDs.");
            datumIds=unique.ToArray();
        }
        string[]? datumRevisionIds=null;
        if(values.Remove("datum_plane_revision_ids",out var revisions))
        {
            if(revisions.ValueKind!=JsonValueKind.Array||revisions.GetArrayLength()<1)throw new DomainException("INVALID_ARGUMENT","Choose exact fixed native datum IDs consumed by mirrors of one current part.");
            var unique=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in revisions.EnumerateArray())
                if(item.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(item.GetString(),"B",out var datum)||datum==Guid.Empty||!unique.Add(datum.ToString("B").ToUpperInvariant()))
                    throw new DomainException("INVALID_ARGUMENT","datum_plane_revision_ids must be distinct nonempty current braced GUIDs.");
            datumRevisionIds=unique.ToArray();
        }
        string[]? regionIds=null;
        if(values.Remove("profile_region_ids",out var regions))
        {
            if(regions.ValueKind!=JsonValueKind.Array||regions.GetArrayLength()<1)
                throw new DomainException("INVALID_ARGUMENT","Choose exact consumed native profile IDs for retained Circle-region metadata.");
            var unique=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in regions.EnumerateArray())
                if(item.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(item.GetString(),"B",out var id)||id==Guid.Empty||!unique.Add(id.ToString("B").ToUpperInvariant()))
                    throw new DomainException("INVALID_ARGUMENT","profile_region_ids must be distinct nonempty current braced GUIDs.");
            regionIds=unique.ToArray();
        }
        string[]? extentIds=null;
        if(values.Remove("extrusion_extent_ids",out var extents))
        {
            if(extents.ValueKind!=JsonValueKind.Array||extents.GetArrayLength()<1)
                throw new DomainException("INVALID_ARGUMENT","Choose exact existing extrusion native IDs for retained extent metadata.");
            var unique=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in extents.EnumerateArray())
                if(item.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(item.GetString(),"B",out var id)||id==Guid.Empty||!unique.Add(id.ToString("B").ToUpperInvariant()))
                    throw new DomainException("INVALID_ARGUMENT","extrusion_extent_ids must be distinct nonempty current braced GUIDs.");
            extentIds=unique.ToArray();
        }
        string[]? sketchIds = null;
        if (values.Remove("sketch_geometry_ids", out var selection))
        {
            if (selection.ValueKind != JsonValueKind.Array || selection.GetArrayLength() < 1)
                throw new DomainException("INVALID_ARGUMENT", "Choose one or more sketch IDs from current model history.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in selection.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !Guid.TryParseExact(item.GetString(), "B", out var id) || id == Guid.Empty ||
                    !unique.Add(id.ToString("B").ToUpperInvariant()))
                    throw new DomainException("INVALID_ARGUMENT", "Sketch IDs must be distinct nonempty braced GUIDs from the current history.");
            }
            sketchIds = unique.ToArray();
        }
        var args = Validation.Parse("inspect_objects", Wire.Element(values));
        if (args.BridgeId == null || args.ExpectedRevision == null)
            throw new DomainException("INVALID_ARGUMENT", "Model history requires explicit bridge/document/revision and distinct handles.");
        if(parameterIds!=null&&(args.Handles!.Length!=1||nativeSketchIds?.Length!=1))
            throw new DomainException("INVALID_ARGUMENT","sketch_constraint_parameter_ids requires one current part handle and one sketch_ids ID.");
        if(deletionIds!=null&&args.Handles!.Length!=1)
            throw new DomainException("INVALID_ARGUMENT","feature_deletion_ids observes the explicit native terminal erase scope of one current part; ordinary multi-body history remains available without this field.");
        if(datumRevisionIds!=null&&args.Handles!.Length!=1)
            throw new DomainException("INVALID_ARGUMENT","datum_plane_revision_ids reads the retained mirror consumers of one current native part; ordinary multi-body history remains available without this field.");
        if(extentIds!=null&&args.Handles!.Length!=1)
            throw new DomainException("INVALID_ARGUMENT","extrusion_extent_ids selects exact features of one current native part; ordinary multi-body history remains available without this field.");
        if(elementIds!=null&&args.Handles!.Length!=1)
            throw new DomainException("INVALID_ARGUMENT","profile_element_ids reads exact native profiles in one selected part's actual owner document; ordinary multi-body history remains unchanged without this field.");
        if(regionIds!=null&&args.Handles!.Length!=1)
            throw new DomainException("INVALID_ARGUMENT","profile_region_ids describes exact profiles of one selected target part; ordinary multi-body history remains available without this field.");
        return args with { SketchConstraintParameterIds=parameterIds,ModelFeatureDeletionIds=deletionIds,SketchGeometryIds = sketchIds,IncludeDocumentProfiles=includeProfiles,DocumentProfileCursor=profileCursor,ModelProfileIds=profileIds,ModelProfileRegionIds=regionIds,ModelExtrusionExtentIds=extentIds,IncludeDocumentDatumPlanes=includeDatums,DocumentDatumCursor=datumCursor,ModelDatumPlaneIds=datumIds,ModelDatumPlaneRevisionIds=datumRevisionIds,ModelProfileElementIds=elementIds,IncludeDocumentSketches=includeSketches,DocumentSketchCursor=sketchCursor,ModelSketchIds=nativeSketchIds };
    }
}
