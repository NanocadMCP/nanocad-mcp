namespace NanoCAD.Contracts;

public sealed record PaperDrawingViewCreation356(EntityRef view_ref,EntityRef layout_ref,string native_document_id,
    string? view_token,string? view_token_unavailable_reason,EntityRef[] source_refs,string[] top_source_ids,string? parent_view_id,string root_view_id,
    string[] child_view_ids,string projection_observation);
