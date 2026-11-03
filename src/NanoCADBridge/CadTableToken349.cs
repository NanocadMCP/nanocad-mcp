using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// One exact admission path for all Table token routes. Diagnostics never admit,
// refresh or replay a mismatching request, and retain the complete field ledger.
internal static class CadTableToken349
{
    internal static void Require(Table table,CadTableEditingReader.Snapshot current,string expected,
        string operation,Action cancel)
    {
        cancel();if(current.Token==expected)return;
        throw new DomainException("TABLE_STATE_CHANGED",
            "Native table dimensions, contents, formatting or inheritance observation changed; refresh inspect_objects geometry.editing.",false,
            new{operation,comparison=CadTableSnapshotDiagnostics346.Compare(table,current,expected,cancel)});
    }
}
