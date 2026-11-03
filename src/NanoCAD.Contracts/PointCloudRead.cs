using System.Text.Json;

namespace NanoCAD.Contracts;

public static class PointCloudReadValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if(input.ValueKind!=JsonValueKind.Object)throw new DomainException("INVALID_ARGUMENT","Expected a point cloud context object.");
        var fields=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var p in input.EnumerateObject())
            if(p.Name is not("bridge_instance_id" or "document_id" or "expected_revision" or "handle") || !fields.TryAdd(p.Name,p.Value))
                throw new DomainException("INVALID_ARGUMENT","Unknown or duplicate cloud argument: "+p.Name);
        if(!fields.Remove("handle",out var handle) || handle.ValueKind!=JsonValueKind.String)
            throw new DomainException("INVALID_ARGUMENT","One existing point cloud insertion handle is required.");
        fields.Add("handles",Wire.Element(new[]{handle.GetString()}));
        var args=Validation.Parse("inspect_objects",Wire.Element(fields));
        if(args.BridgeId==null || args.DocumentId==null || args.ExpectedRevision==null)
            throw new DomainException("INVALID_ARGUMENT","Point cloud read requires explicit bridge/document/revision.");
        return args;
    }
}
