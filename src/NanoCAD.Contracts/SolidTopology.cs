using System.Text.Json;

namespace NanoCAD.Contracts;

public static class SolidTopologyValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if(input.ValueKind!=JsonValueKind.Object)throw new DomainException("INVALID_ARGUMENT","Expected object.");
        var values=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        bool includeNativeFaces=false,seenNativeFaces=false,includeNativeEdges=false,seenNativeEdges=false;
        foreach(var p in input.EnumerateObject())
        {
            if(p.Name=="include_native_model_edges")
            {
                if(p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)||seenNativeEdges)
                    throw new DomainException("INVALID_ARGUMENT","include_native_model_edges must be boolean and unique.");
                includeNativeEdges=p.Value.GetBoolean();seenNativeEdges=true;continue;
            }
            if(p.Name=="include_native_model_faces")
            {
                if(p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)||seenNativeFaces)
                    throw new DomainException("INVALID_ARGUMENT","include_native_model_faces must be boolean and unique.");
                includeNativeFaces=p.Value.GetBoolean();seenNativeFaces=true;
                continue;
            }
            if(p.Name is not ("bridge_instance_id" or "document_id" or "expected_revision" or "handle") || !values.TryAdd(p.Name,p.Value))
                throw new DomainException("INVALID_ARGUMENT","Unknown or duplicate topology argument: "+p.Name);
        }
        if(!values.Remove("handle",out var handle) || handle.ValueKind!=JsonValueKind.String)
            throw new DomainException("INVALID_ARGUMENT","A native Solid3d handle is required.");
        values.Add("handles",Wire.Element(new[]{handle.GetString()}));
        var args=Validation.Parse("inspect_objects",Wire.Element(values));
        if(args.BridgeId==null || args.DocumentId==null || args.ExpectedRevision==null)
            throw new DomainException("INVALID_ARGUMENT","Topology inspection requires explicit bridge/document/revision.");
        return args with{IncludeNativeModelFaces=includeNativeFaces,IncludeNativeModelEdges=includeNativeEdges};
    }
}
