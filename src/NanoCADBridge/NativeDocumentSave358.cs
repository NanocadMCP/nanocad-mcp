using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// The owning native document dispatches its save lifecycle. A raw database
// serialization cannot stand in for the document's modeling-module callbacks.
internal static class NativeDocumentSave358
{
    internal const string VersionWarning="NATIVE_DOCUMENT_SAVE_VERSION: The owning document saves DWG through the host's current native configuration. This API has no version argument; DWG2018 is not promised. A completed file/path receipt does not itself prove retained-model persistence after reopen.";
    static void Need([DoesNotReturnIf(false)]bool value,string message)
    {if(!value)throw new DomainException("DOCUMENT_CONTEXT_MISMATCH",message);}
    internal static Action<string>? Resolve(DocumentState state,Action check)
    {
        check();var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(value=>value.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Keep the mixed-mode SDK out of the ordinary 2D-only save/JIT path.
        // Never load a modeling module as a side effect of saving a document.
        if(!loaded.Contains("mapimgd"))return null;
        if(!loaded.Contains("MapiBaseTypes"))throw new DomainException("CAPABILITY_UNAVAILABLE","The loaded MultiCAD document save route lacks its base runtime; no raw serialization fallback is used for this document.");
        return ResolveNative(state,check);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Action<string> ResolveNative(DocumentState state,Action check)
    {
        Multicad.DatabaseServices.McDocument Current()
        {
            check();Need(ReferenceEquals(Application.DocumentManager.MdiActiveDocument,state.Document),"The exact host document must remain active for owning-document saving.");
            var active=Multicad.DatabaseServices.McDocument.ActiveDocument;
            Need(active!=null&&!active.ID.IsNull,"The active native document is unavailable.");
            // ActiveDocument can be a Paper/Model sheet. Select its real top
            // document, never a model body's internal MCS owner/network.
            var owner=active.IsSub?active.GetTopDoc():active;
            Need(owner!=null&&!owner.ID.IsNull&&!owner.IsSub,"The active native container has no unambiguous full owning document.");
            if(state.Document.IsNamedDrawing)
                Need(!string.IsNullOrWhiteSpace(owner.Path)&&string.Equals(Path.GetFullPath(owner.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
                    "The native owning document path does not match the selected named host drawing.");
            else
                Need(owner.IsNew&&(string.IsNullOrWhiteSpace(owner.Path)||string.Equals(Path.GetFileName(owner.Path),Path.GetFileName(state.Document.Name),StringComparison.OrdinalIgnoreCase)),
                    "The native owning document is not the selected unnamed host drawing.");
            check();return owner;
        }
        var before=Current();var identity=before.ID;
        return path=>
        {
            var current=Current();Need(current.ID==identity,"The actual owning document identity changed before native save.");
            // Installed PE is bool SaveAs(string), unlike the older CHM's ref
            // string syntax. Native false after this boundary is unresolved.
            check();if(!current.SaveAs(path))throw new DomainException("DOCUMENT_SAVE_FAILED","Owning native document SaveAs returned false after it started; file/memory outcome is unknown.");
            check();Need(ReferenceEquals(Application.DocumentManager.MdiActiveDocument,state.Document),"Owning native save changed the active host document.");
        };
    }
}
