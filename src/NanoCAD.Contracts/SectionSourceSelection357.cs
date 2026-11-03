namespace NanoCAD.Contracts;

public sealed record SectionSourceModeRead357(string kind,int generation_flags,string generation_options,string[] source_handles);
public sealed record SectionSourceSelectionRead357(string section_id,string section_handle,string settings_handle,
    string? settings_owner_handle,string current_section_type,SectionSourceModeRead357[] modes,string scope);
