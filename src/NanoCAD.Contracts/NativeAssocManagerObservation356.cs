namespace NanoCAD.Contracts;

// This is the exact readable native manager anchor, not a private solver dump.
public sealed record NativeAssocManagerObservationRead356(string subject_scope,string mcs_owner_document_id,
    string manager_handle,string owner_handle,string runtime_type,bool private_state_complete,string observation_scope);
