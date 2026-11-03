using NanoCAD.Contracts;
using NanoCAD.Ipc;

internal static class LongOperationTests
{
    private static readonly string Bridge=Guid.NewGuid().ToString("D"),Document=Guid.NewGuid().ToString("D");
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static Request Request(int timeout=40)=>new(Wire.Version,Guid.NewGuid().ToString("D"),"create_entities",timeout,Wire.Element(new{
        bridge_instance_id=Bridge,document_id=Document,expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",
        entities=new[]{new{type="Line",start=new[]{0d,0d},end=new[]{1d,0d}}}}));
    private static Result Success(WriteJob job)=>Result.Ok(job.OriginalRequestId,new CreatedEntities(job.OperationId,"committed",false,
        new[]{new EntityRef(Bridge,Document,"AA")},"single_command"),new(Bridge,Document,"2",DateTime.UtcNow.ToString("O")));
    private static void Cancelled(Action action)
    {try{action();throw new Exception("Expected cancellation");}catch(OperationCanceledException){}}
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
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
