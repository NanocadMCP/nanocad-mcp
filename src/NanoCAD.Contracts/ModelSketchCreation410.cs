namespace NanoCAD.Contracts;

public sealed record ModelSketchCreation410(EntityRef body_ref,string body_mc_id,string sketch_id,string profile_id,
    string feature_id,string last_item_id,NativeSketchCreated385[] native_objects,SketchConstraintChange[] constraint_changes,
    string? distance_variable_id);
