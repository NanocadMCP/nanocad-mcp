using NanoCAD.Contracts;
using NanoCAD.Ipc;

internal static class LongOperationTests
{
    private static readonly string Bridge=Guid.NewGuid().ToString("D"),Document=Guid.NewGuid().ToString("D");
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static Request Request(int timeout=40,string? bridgeId=null)=>new(Wire.Version,Guid.NewGuid().ToString("D"),"create_entities",timeout,Wire.Element(new{
        bridge_instance_id=bridgeId??Bridge,document_id=Document,expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",
        entities=new[]{new{type="Line",start=new[]{0d,0d},end=new[]{1d,0d}}}}));
    private static Result Success(WriteJob job)=>Result.Ok(job.OriginalRequestId,new CreatedEntities(job.OperationId,"committed",false,
        new[]{new EntityRef(Bridge,Document,"AA")},"single_command"),new(Bridge,Document,"2",DateTime.UtcNow.ToString("O")));
    private static void Cancelled(Action action)
    {try{action();throw new Exception("Expected cancellation");}catch(OperationCanceledException){}}
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("write transport: oversized request is not started and never needs a pipe", async () =>
        {
            var operationId = Guid.NewGuid().ToString("D");
            var request = Request(10000) with { arguments = Wire.Element(new {
                operation_id = operationId, text = new string('a', Wire.MaxRequestBytes)
            }) };
            var descriptor = Discovery.Describe(Bridge, "request-size-only");
            try
            {
                await new BridgeClient().CallAsync(descriptor, request, default);
                throw new Exception("Oversized request accepted.");
            }
            catch (DomainException error)
            {
                Check(error.Error.code == "REQUEST_TOO_LARGE" &&
                    error.Error.details is WriteErrorDetails { outcome: "not_started", undo_history: "unchanged" } details &&
                    details.operation_id == operationId, "Local size rejection was classified as possible execution.");
            }
        });
        await test("write transport: cancellation after dispatch preserves original operation identity", async () =>
        {
            var descriptor = Discovery.Describe(Guid.NewGuid().ToString("D"), "write-owner-cancel-no-CAD");
            var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var listener = new BridgeListener(descriptor, async (request, stop) =>
            {
                received.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, stop);
                return Result.Ok(request.requestId, new { });
            }, new JsonLog("write-owner-cancel-test"));
            var request = Request(10000, descriptor.bridge_instance_id);
            using var cancel = new CancellationTokenSource();
            var response = new BridgeClient().CallAsync(descriptor, request, cancel.Token);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancel.Cancel();
            try { await response; throw new Exception("Expected interrupted write response."); }
            catch (DomainException error)
            {
                Check(error.Error.code == "WRITE_OUTCOME_UNKNOWN" &&
                    error.Error.details is WriteErrorDetails { outcome: "unknown" } details &&
                    details.operation_id == request.arguments.GetProperty("operation_id").GetString() &&
                    Wire.Element(details.cause!).GetProperty("bridge_instance_id").GetString() == descriptor.bridge_instance_id,
                    "Cancellation lost the original operation identity or claimed a known outcome.");
            }
        });
        await test("transport: inconsistent result envelope preserves a sent write's uncertain outcome", async () =>
        {
            var descriptor = Discovery.Describe(Guid.NewGuid().ToString("D"), "envelope-contract-no-CAD");
            using var listener = new BridgeListener(descriptor, (request, _) =>
                Task.FromResult(new Result(Wire.Version, request.requestId, false, null, null, null, Array.Empty<string>())),
                new JsonLog("envelope-contract-test"));
            var write = Request(10000, descriptor.bridge_instance_id);
            var read = write with { operation = "get_status", arguments = Wire.Element(new { descriptor.bridge_instance_id }) };
            foreach (var request in new[] { read, write })
            {
                try { await new BridgeClient().CallAsync(descriptor, request, default); throw new Exception("Invalid outcome accepted."); }
                catch (DomainException error)
                {
                    Check(error.Error.code == (request == read ? "PROTOCOL_MISMATCH" : "WRITE_OUTCOME_UNKNOWN"),
                        "Response classification ignored the possible write.");
                }
            }
        });
        await test("write isolation: waiting and new writes stop while original receipts remain readable", async () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var original = Request(10000);
            var cause = queue.Enqueue(original).Job;
            Check(queue.Take() == cause && cause.TryStart(), "Could not start the original write.");
            var waiting = Request(10000);
            var pending = queue.SubmitAsync(waiting, default);
            queue.Isolate(cause);
            cause.Complete(cause.Error("WRITE_OUTCOME_UNKNOWN", "Unknown native outcome.", "unknown"));
            var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(1));
            Check(cancelled.error is { code: "WRITES_ISOLATED_UNTIL_RESTART", details: WriteErrorDetails { outcome: "not_started" } } &&
                queue.Count == 0 && queue.IsolatedByOperationId == cause.OperationId, "Waiting write or isolation identity lost.");
            try { await queue.SubmitAsync(Request(), default); throw new Exception("Write accepted during isolation."); }
            catch (DomainException error)
            {
                Check(error.Error.code == "WRITES_ISOLATED_UNTIL_RESTART" &&
                    error.Error.details is WriteErrorDetails { outcome: "not_started" } blocked &&
                    Wire.Element(blocked.cause!).GetProperty("isolating_operation_id").GetString() == cause.OperationId,
                    "Fresh write did not identify the isolating operation.");
            }
            var history = await queue.ReadOperationAsync(new Request(Wire.Version, Guid.NewGuid().ToString("D"), "get_operation_result",
                1000, Wire.Element(new { bridge_instance_id = Bridge, operation_id = cause.OperationId })), default);
            var replay = await queue.SubmitAsync(original, default);
            Check(history.data is OperationResultLookup432 { found: true, outcome: "unknown" } &&
                replay.error?.details is WriteErrorDetails { outcome: "unknown", replayed: true } && queue.LedgerCount == 2,
                "Isolation blocked historical lookup or changed the original write outcome.");
        });
        await test("long write: response expiry and later disconnect retain one running job",()=>{
            using var queue=new WriteQueue(Environment.CurrentManagedThreadId);
            using var owner=new CancellationTokenSource();var request=Request();
            var waiting=queue.SubmitAsync(request,owner.Token);var job=queue.Take()!;Check(job.TryStart(),"start");
            var pending=waiting.GetAwaiter().GetResult();Check(pending.error?.code=="WRITE_OUTCOME_PENDING","pending");
            owner.Cancel();job.CheckCancellation();Check(queue.ActiveExecutions.Length==1,"active execution missing");
            var replay=queue.SubmitAsync(request with{requestId=Guid.NewGuid().ToString("D")},default).GetAwaiter().GetResult();
            Check(replay.error?.details is WriteErrorDetails{replayed:true,outcome:"unknown"},"replay state");
            Check(queue.LedgerCount==1&&queue.Count==0,"duplicate reservation");job.CheckCancellation();
            job.BeginCommit();job.RecordCommit(Success(job));job.Complete(Success(job));
            var done=queue.SubmitAsync(request,default).GetAwaiter().GetResult();
            Check(done.success&&done.data is CreatedEntities{replayed:true}&&queue.ActiveExecutions.Length==0,"final replay");
            return Task.CompletedTask;
        });
        await test("long write: response expiry before native start prevents late execution",()=>{
            foreach(bool scheduled in new[]{false,true}){
                using var queue=new WriteQueue(Environment.CurrentManagedThreadId);var request=Request();
                var waiting=queue.SubmitAsync(request,default);WriteJob? job=scheduled?queue.Take():null;
                var result=waiting.GetAwaiter().GetResult();
                Check(result.error?.details is WriteErrorDetails{outcome:"not_started"},"queued timeout outcome");
                Check(scheduled?!job!.TryStart():queue.Take()==null,"late execution");
            }return Task.CompletedTask;
        });
        await test("long write: execution budget is independent and cannot be renewed by replay",()=>{
            using var queue=new WriteQueue(Environment.CurrentManagedThreadId,executionBudget:TimeSpan.FromMilliseconds(180));
            var request=Request();var waiting=queue.SubmitAsync(request,default);var job=queue.Take()!;Check(job.TryStart(),"start");
            Check(waiting.GetAwaiter().GetResult().error?.code=="WRITE_OUTCOME_PENDING","pending");
            Check(queue.SubmitAsync(request,default).GetAwaiter().GetResult().error?.code=="WRITE_OUTCOME_PENDING","replay pending");
            Thread.Sleep(200);Cancelled(job.CheckCancellation);Cancelled(job.BeginCommit);
            job.Complete(job.Error("CANCELLED","Native adapter verified cleanup after execution deadline.","rolled_back"));
            Check(queue.SubmitAsync(request,default).GetAwaiter().GetResult().error?.details is WriteErrorDetails{outcome:"rolled_back",replayed:true},"deadline result lost");
            return Task.CompletedTask;
        });
        await test("long write: bridge stop preserves unknown running outcome and never resumes it",()=>{
            using var queue=new WriteQueue(Environment.CurrentManagedThreadId);var request=Request();
            var waiting=queue.SubmitAsync(request,default);var job=queue.Take()!;Check(job.TryStart(),"start");
            waiting.GetAwaiter().GetResult();queue.Dispose();
            Check(job.Completion.GetAwaiter().GetResult().error?.details is WriteErrorDetails{outcome:"unknown"},"stop fabricated outcome");
            Cancelled(job.CheckCancellation);Check(!job.TryStart(),"stopped operation restarted");return Task.CompletedTask;
        });
        await test("long write: recorded commit survives expiry of execution budget",()=>{
            using var queue=new WriteQueue(Environment.CurrentManagedThreadId,executionBudget:TimeSpan.FromMilliseconds(120));
            var request=Request();var waiting=queue.SubmitAsync(request,default);var job=queue.Take()!;Check(job.TryStart(),"start");
            waiting.GetAwaiter().GetResult();job.BeginCommit();job.RecordCommit(Success(job));Thread.Sleep(150);
            job.Complete(job.Error("CANCELLED","late cleanup","unknown"));
            Check(queue.SubmitAsync(request,default).GetAwaiter().GetResult().success,"lost committed result");return Task.CompletedTask;
        });
        await test("long write: actual pipe response timeout disconnect and replay preserve execution",async()=>{
            using var queue=new WriteQueue(Environment.CurrentManagedThreadId);var descriptor=Discovery.Describe(Bridge,"long-write-test-no-CAD");
            using var listener=new BridgeListener(descriptor,queue.SubmitAsync,new JsonLog("long-write-test"));
            var request=Request(10000);var first=new BridgeClient().CallAsync(descriptor,request,default);
            Check(SpinWait.SpinUntil(()=>queue.Count==1,3000),"request was not queued");
            var job=queue.Take()!;Check(job.TryStart(),"start");var pending=await first;
            Check(pending.error?.code=="WRITE_OUTCOME_PENDING","pipe did not return pending before deadline");
            await Task.Delay(150);job.CheckCancellation();job.BeginCommit();job.RecordCommit(Success(job));job.Complete(Success(job));
            var done=await new BridgeClient().CallAsync(descriptor,request with{requestId=Guid.NewGuid().ToString("D")},default);
            Check(done.success&&queue.LedgerCount==1,"pipe replay failed or ran twice");
        });
    }
}
