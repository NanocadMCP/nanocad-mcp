using System.Text.Json;

namespace NanoCAD.Contracts;

// Internal IPC only. The public MCP tool is capture_view, whose server checks
// document/revision/camera before and after the host-side desktop capture.
public sealed record VisualCaptureBridgeArguments(string BridgeId,string CaptureId,int MaxEdge,bool InlineImage = false)
{
    public bool ViewportOnly { get; init; }
    public CaptureWorldBounds? Bounds { get; init; }
    public string? CameraToken { get; init; }
    public bool RequiresViewport => ViewportOnly || Bounds != null;

    public static VisualCaptureBridgeArguments Parse(JsonElement input)
    {
        if(input.ValueKind!=JsonValueKind.Object)throw new DomainException("INVALID_ARGUMENT","Capture request must be an object.");
        string? bridge=null,capture=null,cameraToken=null;int edge=0;bool inline=false,viewportOnly=false;
        CaptureWorldBounds? bounds = null;
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in input.EnumerateObject())
        {
            if(!names.Add(property.Name))throw new DomainException("INVALID_ARGUMENT","Duplicate capture argument.");
            switch(property.Name)
            {
                case "bridge_instance_id":
                    if(property.Value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(property.Value.GetString(),"D",out var id)||id==Guid.Empty)
                        throw new DomainException("INVALID_ARGUMENT","Capture requires an exact bridge instance ID.");
                    bridge=id.ToString("D");break;
                case "capture_id":
                    if(property.Value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(property.Value.GetString(),"N",out var token)||token==Guid.Empty)
                        throw new DomainException("INVALID_ARGUMENT","Capture requires a new opaque file token.");
                    capture=token.ToString("N");break;
                case "inline_image":
                    if(property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new DomainException("INVALID_ARGUMENT","inline_image must be boolean.");
                    inline=property.Value.GetBoolean();break;
                case "viewport_only":
                    if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new DomainException("INVALID_ARGUMENT", "viewport_only must be boolean.");
                    viewportOnly = property.Value.GetBoolean();
                    break;
                case "bounds":
                    bounds = CaptureWorldBounds.Parse(property.Value);
                    break;
                case "expected_camera_token":
                    if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() is not { Length: 64 } camera ||
                        camera.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
                        throw new DomainException("INVALID_ARGUMENT", "expected_camera_token must be an uppercase SHA-256 token.");
                    cameraToken = camera;
                    break;
                case "max_edge":
                    if(property.Value.ValueKind!=JsonValueKind.Number||!property.Value.TryGetInt32(out edge)||edge is <256 or >2048)
                        throw new DomainException("INVALID_ARGUMENT","Capture max_edge must be256–2048.");
                    break;
                default:throw new DomainException("INVALID_ARGUMENT","Unknown capture argument.");
            }
        }
        if(bridge==null||capture==null||edge==0)throw new DomainException("INVALID_ARGUMENT","Capture bridge, token and max_edge are required.");
        if ((bounds == null) != (cameraToken == null))
            throw new DomainException("INVALID_ARGUMENT", "A region capture must include bounds and its observed camera token.");
        return new(bridge,capture,edge,inline) { ViewportOnly = viewportOnly, Bounds = bounds, CameraToken = cameraToken };
    }
}
