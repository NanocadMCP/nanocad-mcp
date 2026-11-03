using System.Text.Json;

namespace NanoCAD.Contracts;

// A null native inventory is unavailable information, never an empty network.
public sealed record ModelSketchConstraintObservation353(string document_id,string sketch_id,string scope_id,
    string network,string solver_status,bool constraints_complete,JsonElement[]? constraints,
    string? unavailable_reason,string? warning);
