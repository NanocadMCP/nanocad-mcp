using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using NativeColor=Teigha.Colors.Color;
using static NanoCADBridge.CadHatchFillReader377;

namespace NanoCADBridge;

// Resident native fill setters only. Boundary IDs/curves are never replaced,
// sampled or detached. A detached clone cannot certify native name resolution.
internal static class CadHatchFillWriter377
{
    const string Warning="NATIVE_HATCH_FILL_EDIT: The existing Hatch changes active fill through its native document context. Stored loops, plane, island style and local forward/reverse boundary links remain. Native gradient names/settings may depend on the owning installation. Recovery/Undo is not guaranteed; inspect actual fill and refresh its token. Pending/unknown never means success or rollback.";
    sealed record Binding(int Index,SetHatchFill377 Edit,ObjectId Id,Snapshot Before);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is SetHatchFill377);
    static Binding[] Resolve(ModifyArguments args,Database db,Transaction tr,Action cancel)
    {
        HatchFillValidation377.Batch(args.Operations);WriteSpace.Check(args);var space=CadWriteSpace.Resolve(db,tr,args.TargetSpace);
        var result=new List<Binding>();var budget=new ModelInventoryBudget(cancel);
        for(int index=0;index<args.Operations.Length;index++)
        {
            cancel();var edit=(SetHatchFill377)args.Operations[index];var id=CadStructureWriter.Find(db,edit.Handles.Single());
            var hatch=tr.GetObject(id,OpenMode.ForRead) as Hatch;
            Need(hatch!=null&&hatch.GetType()==typeof(Hatch)&&hatch.Database==db&&hatch.OwnerId==space,
                "Select an exact resident native Hatch in requested Model or active ordinary Paper space.","OUT_OF_SCOPE");
            var layer=(LayerTableRecord)tr.GetObject(hatch.LayerId,OpenMode.ForRead);
            Need(!layer.IsLocked&&!layer.IsDependent,"The Hatch layer must be local and unlocked.","LAYER_UNAVAILABLE");
            Need(hatch.Annotative!=AnnotativeStates.True,"Annotative fill changes require their owning context adapter.");
            var before=Capture(hatch,db,tr,cancel);
            Need(before.Boundary.HatchStyle is "normal" or "outer" or "ignore","The retained native island style is not understood by this fill adapter.");
            Need(before.Token==edit.FillToken,"Native fill/boundary/source state changed; refresh geometry.fill_editing.fill_token.","HATCH_STATE_CHANGED");
            budget.Add(new{edit,before});result.Add(new(index,edit,id,before));
        }
        return result.ToArray();
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action? cancel=null)=>Resolve(args,state.Document.Database,tr,cancel??(()=>{}));
    static bool Close(double expected,double actual)=>double.IsFinite(expected)&&double.IsFinite(actual)&&Math.Abs(expected-actual)<=1e-12+Math.Abs(expected)*1e-12;
    static object Details(string path,object? expected,object? actual)
    {
        var first=JsonSerializer.SerializeToUtf8Bytes(expected,Wire.Json);var second=JsonSerializer.SerializeToUtf8Bytes(actual,Wire.Json);
        var result=new{path,expected,current=actual};
        if(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=4096)return result;
        return new{path,expected_bytes=first.Length,current_bytes=second.Length,
            expected_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(first)),current_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(second)),
            values_omitted_reason="Captured component exceeded actual4096-byte diagnostic budget."};
    }
    static void Match(bool value,string path,object? expected,object? actual)
    {if(!value)throw new DomainException("NATIVE_HATCH_FILL_MISMATCH","Native fill differs at "+path,false,Details(path,expected,actual));}
    static void Requested(SetHatchFill377 edit,Snapshot current)
    {
        Need(current.Kind==edit.Kind,"Native Hatch did not retain the requested active fill kind.","NATIVE_HATCH_FILL_MISMATCH");var active=current.Active;
        if(edit.Gradient is {} gradient)
        {
            Match(active.GetProperty("gradient_type").GetString()==gradient.Type,"/gradient_type",gradient.Type,active.GetProperty("gradient_type"));
            Match(string.Equals(active.GetProperty("gradient_name").GetString(),gradient.Name,StringComparison.OrdinalIgnoreCase),"/gradient_name",gradient.Name,active.GetProperty("gradient_name"));
            Match(Close(gradient.Angle,active.GetProperty("angle").GetDouble()),"/angle",gradient.Angle,active.GetProperty("angle"));
            Match(gradient.Shift==active.GetProperty("shift").GetSingle(),"/shift",gradient.Shift,active.GetProperty("shift"));
            Match(gradient.OneColorMode==active.GetProperty("one_color_mode").GetBoolean(),"/one_color_mode",gradient.OneColorMode,active.GetProperty("one_color_mode"));
            Match(gradient.ShadeTint==active.GetProperty("shade_tint").GetSingle(),"/shade_tint",gradient.ShadeTint,active.GetProperty("shade_tint"));
            var colors=active.GetProperty("colors");Need(colors.GetArrayLength()==gradient.Colors.Length,
                "Native gradient did not retain the complete requested color/value array.","NATIVE_HATCH_FILL_MISMATCH");
            for(int index=0;index<gradient.Colors.Length;index++)
            {
                var expected=gradient.Colors[index];var actual=colors[index];var color=actual.GetProperty("color");
                Match(actual.GetProperty("value").GetSingle()==expected.Value&&color.GetProperty("method").GetString()=="true_color"&&
                    color.GetProperty("rgb").EnumerateArray().Select(value=>value.GetInt32()).SequenceEqual(new[]{(int)expected.Color.Red,expected.Color.Green,expected.Color.Blue}),
                    "/colors/"+index,new{expected.Value,color_rgb=expected.Color},actual);
            }
        }
        else if(edit.Pattern is {} pattern)
        {
            Need(active.GetProperty("pattern_type").GetString()==pattern.Type&&string.Equals(active.GetProperty("pattern_name").GetString(),pattern.Name,StringComparison.OrdinalIgnoreCase)&&
                Close(pattern.Angle,active.GetProperty("angle").GetDouble())&&Close(pattern.Scale,active.GetProperty("scale").GetDouble())&&
                pattern.Double==active.GetProperty("pattern_double").GetBoolean()&&(!pattern.Space.HasValue||Close(pattern.Space.Value,active.GetProperty("spacing").GetDouble()))&&
                Close(pattern.Origin.X,current.Boundary.Origin[0])&&Close(pattern.Origin.Y,current.Boundary.Origin[1]),
                "Resident line pattern differs from requested name/type/active settings.","NATIVE_HATCH_FILL_MISMATCH");
        }
        else Need(string.Equals(active.GetProperty("pattern_name").GetString(),"SOLID",StringComparison.OrdinalIgnoreCase),
            "Native Hatch did not retain the requested solid fill.","NATIVE_HATCH_FILL_MISMATCH");
    }
    static void Retained(Snapshot before,Snapshot current,bool originSetter,Action cancel)
    {
        var expected=before.Boundary;var actual=current.Boundary;var first=expected.Geometry.Preserved;var second=actual.Geometry.Preserved;
        Need(expected.Identity==actual.Identity&&expected.HatchStyle==actual.HatchStyle,
            "Native fill changed Hatch identity/owner or retained island handling.","NATIVE_HATCH_FILL_MISMATCH");
        foreach(string field in new[]{"normal","elevation","loops"})
        {cancel();Need(BlockMoveGeometry.SameProperties(first.GetProperty(field),second.GetProperty(field)),"Native fill changed the retained Hatch plane/loops.","NATIVE_HATCH_FILL_MISMATCH");}
        Need(first.GetProperty("associative").GetRawText()==second.GetProperty("associative").GetRawText()&&
            expected.Geometry.SourceHandles.SequenceEqual(actual.Geometry.SourceHandles)&&
            CadHatchOriginXData348.MetadataPreserved(first.GetProperty("metadata"),second.GetProperty("metadata"),expected.Origin,actual.Origin,originSetter,cancel),
            "Native fill changed Hatch associations, display properties or readable owned metadata.","NATIVE_HATCH_FILL_MISMATCH");
        Need(BlockMoveGeometry.SameProperties(expected.Sources,actual.Sources)&&expected.Sources.GetArrayLength()==actual.Sources.GetArrayLength(),
            "Native fill changed retained source geometry or source inventory.","NATIVE_HATCH_FILL_MISMATCH");
        foreach(var pair in expected.Sources.EnumerateArray().Zip(actual.Sources.EnumerateArray()))
        {
            cancel();foreach(string field in new[]{"handle","owner_handle","runtime_type","metadata"})
                Need(pair.First.GetProperty(field).GetRawText()==pair.Second.GetProperty(field).GetRawText(),
                    "Native fill changed a boundary source identity/owner/metadata/reactor.","NATIVE_HATCH_FILL_MISMATCH");
        }
    }
    static bool Apply(Hatch hatch,Binding binding,Action cancel)
    {
        cancel();var edit=binding.Edit;var before=binding.Before.Boundary;
        if(edit.Gradient is {} gradient)
        {
            if(hatch.HatchObjectType!=HatchObjectType.GradientObject)hatch.HatchObjectType=HatchObjectType.GradientObject;
            hatch.SetGradient(gradient.Type=="predefined"?GradientPatternType.PreDefinedGradient:GradientPatternType.UserDefinedGradient,gradient.Name);cancel();
            hatch.GradientAngle=gradient.Angle;hatch.GradientShift=gradient.Shift;
            hatch.GradientOneColorMode=gradient.OneColorMode;hatch.ShadeTintValue=gradient.ShadeTint;
            var owned=new List<NativeColor>();var values=new List<GradientColor>();
            try
            {
                foreach(var stop in gradient.Colors){cancel();var color=NativeColor.FromRgb(stop.Color.Red,stop.Color.Green,stop.Color.Blue);owned.Add(color);values.Add(new(color,stop.Value));}
                hatch.SetGradientColors(values.ToArray());cancel();
            }
            finally{foreach(var color in owned)color.Dispose();}
        }
        else
        {
            if(hatch.HatchObjectType!=HatchObjectType.HatchObject)hatch.HatchObjectType=HatchObjectType.HatchObject;
            if(edit.Pattern is {} pattern)
            {
                var type=pattern.Type switch{"predefined"=>HatchPatternType.PreDefined,"user_defined"=>HatchPatternType.UserDefined,"custom_defined"=>HatchPatternType.CustomDefined,_=>throw new DomainException("INVALID_ARGUMENT","Unknown pattern type.")};
                hatch.SetHatchPattern(type,pattern.Name);cancel();hatch.PatternScale=pattern.Scale;hatch.PatternAngle=pattern.Angle;hatch.PatternDouble=pattern.Double;
                if(pattern.Space.HasValue)hatch.PatternSpace=pattern.Space.Value;
            }
            else hatch.SetHatchPattern(HatchPatternType.PreDefined,"SOLID");
        }
        bool originSetter=false;double x=edit.Pattern?.Origin.X??before.Origin[0],y=edit.Pattern?.Origin.Y??before.Origin[1];
        if(edit.Pattern!=null||!Close(hatch.Origin.X,x)||!Close(hatch.Origin.Y,y))
        {cancel();hatch.Origin=new Point2d(x,y);originSetter=true;}
        var style=before.HatchStyle switch{"normal"=>HatchStyle.Normal,"outer"=>HatchStyle.Outer,"ignore"=>HatchStyle.Ignore,_=>throw new DomainException("UNSUPPORTED_HATCH_CONTENT","Unknown retained island style.")};
        if(hatch.HatchStyle!=style)hatch.HatchStyle=style;
        cancel();hatch.EvaluateHatch(true);cancel();hatch.RecordGraphicsModified(true);return originSetter;
    }
    static Result Outcome(ModifyArguments args,Binding[] bindings,Snapshot[] after,WriteJob job,ReadContext context)
    {
        var refs=bindings.Select(value=>new EntityRef(args.BridgeId,args.DocumentId,value.Edit.Handles.Single())).ToArray();
        var changes=bindings.Select((value,index)=>new HatchFillChange377(value.Index,refs[index],after[index].Token,after[index].Kind,
            after[index].Boundary.Geometry.SourceHandles,[Warning])).ToArray();
        return Result.Ok(job.OriginalRequestId,new HatchFillOutput377(args.OperationId,"committed",false,refs,[],[],[],"not_guaranteed",changes),context,[WriteRecovery.Warning,Warning]);
    }
    static void Bytes(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget-4096,"Complete fill receipt exceeds actual response bytes; reduce the group before writing.","RESPONSE_LIMIT_EXCEEDED");}
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;bool started=false;string stage="preparation";Exception? failure=null;
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();var bindings=Resolve(args,db,tr,job.CheckCancellation);
            Bytes(Outcome(args,bindings,bindings.Select(value=>value.Before).ToArray(),job,context),job.CheckCancellation);var after=new List<Snapshot>();
            foreach(var binding in bindings)
            {
                job.CheckCancellation();var hatch=(Hatch)tr.GetObject(binding.Id,OpenMode.ForWrite);
                Need(Capture(hatch,db,tr,job.CheckCancellation).Token==binding.Before.Token,"Native Hatch changed before fill setters.","HATCH_STATE_CHANGED");
                stage="resident_native_fill_setters";started=true;bool originSetter=Apply(hatch,binding,job.CheckCancellation);
                stage="complete_native_readback";var current=Capture(hatch,db,tr,job.CheckCancellation);
                Requested(binding.Edit,current);Retained(binding.Before,current,originSetter,job.CheckCancellation);after.Add(current);
            }
            var result=Outcome(args,bindings,after.ToArray(),job,context);Bytes(result,job.CheckCancellation);
            stage="native_commit";job.BeginCommit();tr.Commit();job.RecordCommit(result);
        }
        catch(Exception error){failure=error;}
        Exception? cleanup=null;try{tr?.Dispose();}catch(Exception error){cleanup=error;}
        if(job.CommittedResult is {} committed)
        {
            if(cleanup!=null){committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Fill commit is recorded; inspect the current Hatch before another mutation.").ToArray()};job.RecordCommit(committed);}
            return committed;
        }
        string code=failure is DomainException domain?domain.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_HATCH_FILL_FAILED";
        bool unknown=started||cleanup!=null;
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?
            "Native fill setters started or cleanup failed; the outcome remains unverified. Preserve the exact request and inspect current state; do not retry with a new operation_id.":failure?.Message??"Native fill did not complete.",
            unknown?"unknown":"not_started",context,new{cause=code,stage,native_details=failure is DomainException failed?failed.Error.details:null,cleanup_error=cleanup?.GetType().Name}) with{warnings=[WriteRecovery.Warning,Warning]};
    }
}
