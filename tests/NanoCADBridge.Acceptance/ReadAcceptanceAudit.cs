using System.Reflection;
using System.Text.Json;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using NanoCAD.Contracts;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Compiled only into the explicit acceptance package. It observes actual MCP
// reads, without adding commands or writes to the production tool surface.
internal sealed class ReadAcceptanceAudit : IDisposable
{
    private static bool _loftOptionsObserved;
    public static void ObserveVariableScalar(object value)
    {
        var path=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,"variable-scalar-audit.jsonl");
        using var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read);
        if(stream.Length>64*1024)return;
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value);stream.Write(bytes);stream.WriteByte((byte)'\n');
    }
    public static void ObserveLoftSection(object value)
    {
        var path=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,"loft-sections-audit.jsonl");
        using var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read);
        if(stream.Length>64*1024)throw new InvalidOperationException("Loft section audit size limit.");
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value);stream.Write(bytes);stream.WriteByte((byte)'\n');stream.Flush(true);
    }
    public static void ObserveLoftOptions()
    {
        if(_loftOptionsObserved)return;
        // Detached option objects only: no drawing, command or system-variable edits.
        var builder=new Teigha.DatabaseServices.LoftOptionsBuilder{Ruled=true,Closed=false,NoTwist=true,AlignDirection=true};
        using var initial=builder.ToLoftOptions();
        builder.NormalOption=Teigha.DatabaseServices.LoftOptionsNormalOption.NoNormal;
        builder.Simplify=true;
        using var former=builder.ToLoftOptions();
        builder.Ruled=true;
        using var corrected=builder.ToLoftOptions();
        var value=new{utc=DateTime.UtcNow.ToString("O"),pid=Environment.ProcessId,
            before_normal=initial.Ruled,after_normal=former.Ruled,after_ruled_last=corrected.Ruled,
            former_normal=former.NormalOption.ToString(),corrected_normal=corrected.NormalOption.ToString(),
            scope="Detached options observations in the actual host; no CAD state changes"};
        var path=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,"loft-options-audit.json");
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        JsonSerializer.Serialize(stream,value);stream.Flush(true);_loftOptionsObserved=true;
    }
    private static readonly string[] Fixtures = { @"C:\NanoCAD MCP\tests\fixtures\phase1-readonly.dxf", @"C:\NanoCAD MCP\tests\fixtures\phase4-structure.dxf" };
    private static readonly string Run = Guid.NewGuid().ToString("D");
    private readonly Document _document;
    private readonly DocumentState _state;
    private readonly Request _request;
    private readonly Observation _before;
    private readonly string _path;
    private readonly List<object> _events = new();
    private void Event(string kind,string detail)
    {
        if(_events.Count>=32)return;
        var callers=new System.Diagnostics.StackTrace(false).GetFrames()?.Select(f=>f.GetMethod()).Where(m=>m!=null)
            .Select(m=>m!.DeclaringType?.FullName+"."+m.Name).Take(14).ToArray();
        _events.Add(new{kind,detail,callers});
    }
    private void SystemChanged(object? sender,SystemVariableChangedEventArgs args){if(args.Changed)Event("system_variable",args.Name);}
    private void ObjectChanged(object? sender,Teigha.DatabaseServices.ObjectEventArgs args)=>Event("object_event",args.DBObject.GetType().FullName??"unknown");
    private void CommandChanged(object? sender,CommandEventArgs args)=>Event("command",args.GlobalCommandName);

    private sealed record Observation(int dbmod, int pickfirst, string revision, string selection_revision,
        string selection_status, string[] selection_handles);

    public static ReadAcceptanceAudit? Begin(Document document, DocumentState state, Request request)
    {
        // Fixed disposable fixture only; user drawings are never audited.
        var fullPath = Path.GetFullPath(document.Name);
        if (!Fixtures.Contains(fullPath, StringComparer.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\", StringComparison.OrdinalIgnoreCase)) return null;
        return new(document, state, request);
    }

    private ReadAcceptanceAudit(Document document, DocumentState state, Request request)
    {
        _document = document; _state = state; _request = request;
        _path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "read-audit.jsonl");
        _before = Capture();
        Application.SystemVariableChanged+=SystemChanged;
        document.Database.ObjectModified+=ObjectChanged;document.Database.ObjectAppended+=ObjectChanged;
        document.CommandEnded+=CommandChanged;document.CommandCancelled+=CommandChanged;document.CommandFailed+=CommandChanged;
    }

    private Observation Capture()
    {
        int dbmod = Convert.ToInt32(Application.GetSystemVariable("DBMOD"));
        int pickfirst = Convert.ToInt32(Application.GetSystemVariable("PICKFIRST"));
        string status = "disabled";
        string[] handles = Array.Empty<string>();
        if (pickfirst == 1)
        {
            var selected = _document.Editor.SelectImplied();
            status = selected.Status.ToString();
            if (selected.Status == PromptStatus.OK)
            {
                if (selected.Value == null) throw new InvalidOperationException("Implied selection is OK without a value.");
                handles = selected.Value.GetObjectIds().Select(id => id.Handle.ToString())
                    .OrderBy(handle => handle, StringComparer.Ordinal).ToArray();
            }
            else if (selected.Status != PromptStatus.Error)
                throw new InvalidOperationException("Unexpected implied-selection status during acceptance audit.");
        }
        return new(dbmod, pickfirst, _state.Revision, _state.SelectionRevision, status, handles);
    }

    public void Dispose()
    {
        Application.SystemVariableChanged-=SystemChanged;
        _document.Database.ObjectModified-=ObjectChanged;_document.Database.ObjectAppended-=ObjectChanged;
        _document.CommandEnded-=CommandChanged;_document.CommandCancelled-=CommandChanged;_document.CommandFailed-=CommandChanged;
        // The using scope ends after the read transaction has been
        // disposed, including failed reads. No mutation is used for restoration.
        var after = Capture();
        var value = new
        {
            utc = DateTime.UtcNow.ToString("O"), run = Run, pid = Environment.ProcessId,
            thread = Environment.CurrentManagedThreadId, document_id = _state.Id,
            operation = _request.operation, request_id = _request.requestId,
            before = _before, after,
            observed_events = _events,
            unchanged = _before.dbmod == after.dbmod && _before.pickfirst == after.pickfirst &&
                _before.revision == after.revision && _before.selection_revision == after.selection_revision &&
                _before.selection_status == after.selection_status && _before.selection_handles.SequenceEqual(after.selection_handles),
            scope = "DBMOD, revision, PICKFIRST and implied selection across actual UI read and transaction disposal; Undo requires separate native test"
        };
        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidOperationException("Acceptance audit reached its 1 MiB limit.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        stream.Write(bytes); stream.WriteByte((byte)'\n'); stream.Flush(true);
        if (!value.unchanged) throw new InvalidOperationException("Read acceptance detected changed document or selection state. See read-audit.jsonl.");
    }
}
