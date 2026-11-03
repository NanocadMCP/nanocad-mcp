using System;

namespace NanoCADBridge;

// Pure managed provenance. Neither token nor the captured job/document identity
// can be rebound after submission; CAD entry/completion must prove this attempt.
internal sealed class NativeCommandAttempt409<TJob,TDocument>
    where TJob : class where TDocument : class
{
    internal string Token { get; } = "NCMCP" + Guid.NewGuid().ToString("N");
    internal TJob Job { get; }
    internal TDocument Document { get; }
    internal bool Entered { get; private set; }
    internal bool WriteStarted { get; private set; }
    internal NativeCommandAttempt409(TJob job,TDocument document)
    { Job=job;Document=document; }

    internal bool TryEnter(string? token,TJob? currentJob,TDocument? document,Func<bool> tryStart)
    {
        if(Entered || !string.Equals(Token,token,StringComparison.Ordinal) ||
            !ReferenceEquals(Job,currentJob) || !ReferenceEquals(Document,document)) return false;
        Entered=true;
        WriteStarted=tryStart();
        return true;
    }

    internal bool CanComplete(NativeCommandAttempt409<TJob,TDocument>? current,
        NativeCommandAttempt409<TJob,TDocument>? entered,TJob? currentJob,TDocument? document) =>
        Entered && ReferenceEquals(this,current) && ReferenceEquals(this,entered) &&
        ReferenceEquals(Job,currentJob) && ReferenceEquals(Document,document);

    internal bool CanRetireNotStarted(TJob? currentJob,bool finished) =>
        finished && !WriteStarted && ReferenceEquals(Job,currentJob);

    internal (TOutcome Outcome,bool Isolate) ResolveCompletion<TOutcome>(TOutcome? outcome,TOutcome? committed,
        Func<bool,TOutcome> missingOutcome) where TOutcome : class
    {
        var known=outcome ?? committed;
        return (known ?? missingOutcome(WriteStarted),known == null && WriteStarted);
    }
}
