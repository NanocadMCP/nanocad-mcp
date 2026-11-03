using System.Text.Json;

namespace NanoCAD.Contracts;

public static class DocumentConstraintsValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if(input.ValueKind!=JsonValueKind.Object)throw new DomainException("INVALID_ARGUMENT","Expected an object.");
        bool include=false;var fields=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var property in input.EnumerateObject())
        {
            if(!fields.TryAdd(property.Name,property.Value))throw new DomainException("INVALID_ARGUMENT","Duplicate argument.");
            if(property.Name=="include_variables")
            {if(property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)throw new DomainException("INVALID_ARGUMENT","include_variables must be boolean.");include=property.Value.GetBoolean();}
        }
        fields.Remove("include_variables");SketchPlaneSpec? plane=null;if(fields.Remove("workplane",out var wp))plane=DrawingWorkplane.Parse(wp);
        var args=Validation.Parse("inspect_objects",Wire.Element(fields)) with{IncludeVariables=include,Workplane=plane};
        if(args.BridgeId==null||args.ExpectedRevision==null)
            throw new DomainException("INVALID_ARGUMENT","Constraint inspection requires explicit bridge/document/revision and a nonempty current Model Space handle set.");
        return args;
    }
}
