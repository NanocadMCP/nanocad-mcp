using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// SDK database.h DBText setters/adjustAlignment; native glyph layout is authoritative.
internal static class CadTextFormat442
{
    sealed record Values(ObjectId Style,double Height,double Width,double Rotation,double Oblique,TextHorizontalMode H,TextVerticalMode V,
        Point3d Position,Point3d Alignment,Vector3d Normal);
    sealed record Snapshot(Values Values,JsonElement Preserved,string Token);
    sealed record Plan(DBText Source,SetTextFormat442 Edit,Snapshot Before,Values Target,string[] Warnings);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool yes,string message,string code="UNSUPPORTED_TEXT_FORMAT") {if(!yes)throw new DomainException(code,message);}
    static double[] P(Point3d p)=>[p.X,p.Y,p.Z];
    static double[] V(Vector3d p)=>[p.X,p.Y,p.Z];
    static Point3d P(Point3 p)=>new(p.X,p.Y,p.Z);
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-9+1e-11*Math.Max(Math.Abs(a),Math.Abs(b));
    static bool Same(Point3d a,Point3d b)=>a.DistanceTo(b)<=Tolerance.Global.EqualPoint;
    static bool Two(TextHorizontalMode h)=>h is TextHorizontalMode.TextAlign or TextHorizontalMode.TextFit;
    static bool Baseline(Values v)=>v.H==TextHorizontalMode.TextLeft&&v.V==TextVerticalMode.TextBase;
    static Point3d Anchor(Values v)=>Baseline(v)||Two(v.H)?v.Position:v.Alignment;
    static TextHorizontalMode H(string s)=>s switch{"left"=>TextHorizontalMode.TextLeft,"center"=>TextHorizontalMode.TextCenter,"right"=>TextHorizontalMode.TextRight,"aligned"=>TextHorizontalMode.TextAlign,"middle"=>TextHorizontalMode.TextMid,"fit"=>TextHorizontalMode.TextFit,_=>throw new DomainException("INVALID_ARGUMENT","Unknown horizontal mode.")};
    static TextVerticalMode Vertical(string s)=>s switch{"baseline"=>TextVerticalMode.TextBase,"bottom"=>TextVerticalMode.TextBottom,"middle"=>TextVerticalMode.TextVerticalMid,"top"=>TextVerticalMode.TextTop,_=>throw new DomainException("INVALID_ARGUMENT","Unknown vertical mode.")};
    static string Name(TextHorizontalMode h)=>h switch{TextHorizontalMode.TextLeft=>"left",TextHorizontalMode.TextCenter=>"center",TextHorizontalMode.TextRight=>"right",TextHorizontalMode.TextAlign=>"aligned",TextHorizontalMode.TextMid=>"middle",TextHorizontalMode.TextFit=>"fit",_=>h.ToString()};
    static string Name(TextVerticalMode v)=>v switch{TextVerticalMode.TextBase=>"baseline",TextVerticalMode.TextBottom=>"bottom",TextVerticalMode.TextVerticalMid=>"middle",TextVerticalMode.TextTop=>"top",_=>v.ToString()};
    static Values Stored(DBText t)=>new(t.TextStyleId,t.Height,t.WidthFactor,t.Rotation,t.Oblique,t.HorizontalMode,t.VerticalMode,t.Position,t.AlignmentPoint,t.Normal);
    static object Observed(Values v)=>new{style=Id(v.Style),v.Height,v.Width,v.Rotation,v.Oblique,horizontal=Name(v.H),vertical=Name(v.V),position=P(v.Position),alignment=P(v.Alignment),normal=V(v.Normal)};
    static Snapshot Capture(DBText text,Transaction tr,Action cancel)
    {
        cancel();Need(text.GetType()==typeof(DBText)&&!text.ObjectId.IsNull&&!text.HasFields&&text.Annotative!=AnnotativeStates.True,
            "Use resident ordinary native TEXT, without Field ownership or alternate annotation contexts.");
        var owner=tr.GetObject(text.OwnerId,OpenMode.ForRead) as BlockTableRecord;Need(owner!=null&&owner.IsLayout&&!owner.IsFromExternalReference,"Use ordinary local Model/Paper TEXT.");
        Need(!CadDrawingGroups431.HasNonOrganizationalReactors(text,tr,cancel),"Persistent TEXT relations need their owning format adapter.");
        var v=Stored(text);Need(Enum.IsDefined(typeof(TextHorizontalMode),v.H)&&Enum.IsDefined(typeof(TextVerticalMode),v.V)&&v.Height>0&&v.Width>0&&
            v.Normal.Length>0&&new[]{v.Height,v.Width,v.Rotation,v.Oblique}.Concat(P(v.Position)).Concat(P(v.Alignment)).Concat(V(v.Normal)).All(double.IsFinite),"Stored TEXT format is incomplete or nonfinite.");
        var style=tr.GetObject(v.Style,OpenMode.ForRead) as TextStyleTableRecord;Need(style!=null&&style.OwnerId==text.Database.TextStyleTableId,"Native TEXT style is unavailable.");
        using var xdata=text.XData;var budget=new ModelInventoryBudget(cancel);
        var retained=Wire.Element(new{root=Id(text.ObjectId),owner=Id(text.OwnerId),text=text.TextString,properties=CadBlockMover.PreservedProperties(text),
            normal=V(v.Normal),text.Thickness,text.IsMirroredInX,text.IsMirroredInY,owned=NativeOwnedMetadata.Capture(text,tr,cancel),
            xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,item=>budget.Add(item))});budget.Add(retained);
        var token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{values=Observed(v),retained,style=TextStyleMetadataReader.Read(style!,cancel)},Wire.Json)));
        return new(v,retained,token);
    }
    internal static TextEditing442 TryRead(DBText text,Action cancel)
    {
        try{var tr=text.Database?.TransactionManager.TopTransaction;return tr==null?new(null,false,"Resident read transaction unavailable."):new(Capture(text,tr,cancel).Token,true);}
        catch(OperationCanceledException){throw;}
        catch(Exception e){return new(null,false,e is DomainException d?d.Error.code+": "+d.Message:"Native format snapshot unavailable: "+e.GetType().Name);}
    }
    static Values Desired(DBText source,Snapshot before,SetTextFormat442 edit,Transaction tr,Action cancel,List<string> warnings)
    {
        Need(before.Token==edit.TextToken,"TEXT state changed; read a fresh text_token.","TEXT_STATE_CHANGED");var p=edit.Properties;var v=before.Values;var style=v.Style;
        if(p.TextStyleHandle is {} handle)
        {style=CadStructureWriter.Find(source.Database,handle);Need(tr.GetObject(style,OpenMode.ForRead) is TextStyleTableRecord r&&r.GetType()==typeof(TextStyleTableRecord)&&!r.IsDependent&&r.OwnerId==source.Database.TextStyleTableId,"Choose an existing local native text style.","TEXT_STYLE_UNAVAILABLE");
            try{FontCoverage.Require(tr,style,source.TextString);}catch(DomainException e)when(e.Error.code=="FONT_CHECK_UNAVAILABLE"){warnings.Add("FONT_CHECK_UNAVAILABLE: Review actual required symbols in the changed native font.");}}
        var h=p.HorizontalMode==null?v.H:H(p.HorizontalMode);var vertical=p.VerticalMode==null?v.V:Vertical(p.VerticalMode);
        Need(!Two(h)||vertical==TextVerticalMode.TextBase,"Aligned/Fit use native baseline vertical mode.","INVALID_ARGUMENT");Need(h!=TextHorizontalMode.TextMid||vertical==TextVerticalMode.TextBase,"Native middle mode uses baseline vertical mode.","INVALID_ARGUMENT");
        Need(h!=TextHorizontalMode.TextAlign||p.TextHeight==null,"Aligned derives height from two anchors; use Fit for fixed-height fitting.","INVALID_ARGUMENT");
        Need(!Two(h)||p.WidthFactor==null,"Aligned/Fit derive width from two anchors.","INVALID_ARGUMENT");
        Need(!Two(h)||p.Rotation==null,"Aligned/Fit derive rotation from two anchors; rotate their anchors instead.","INVALID_ARGUMENT");
        var anchor=p.AnchorWcs is {} a?P(a):Anchor(v);Point3d second;
        if(Two(h))
        {Need(Two(v.H)||p.SecondAnchorWcs!=null,"Entering Aligned/Fit requires an explicit second_anchor_wcs.","INVALID_ARGUMENT");second=p.SecondAnchorWcs is {} b?P(b):v.Alignment;Need(anchor.DistanceTo(second)>Tolerance.Global.EqualPoint,"The two TEXT anchors must be distinct.","INVALID_GEOMETRY");}
        else{Need(p.SecondAnchorWcs==null,"second_anchor_wcs belongs only to Aligned/Fit.","INVALID_ARGUMENT");second=anchor;}
        Need(Math.Abs((anchor-v.Position).DotProduct(v.Normal))<=Tolerance.Global.EqualPoint&&Math.Abs((second-v.Position).DotProduct(v.Normal))<=Tolerance.Global.EqualPoint,
            "TEXT anchors must remain in its actual retained plane; no projection is performed.","INVALID_GEOMETRY");
        cancel();return v with{Style=style,Height=p.TextHeight??v.Height,Width=p.WidthFactor??v.Width,Rotation=p.Rotation??v.Rotation,Oblique=p.Oblique??v.Oblique,H=h,V=vertical,Position=anchor,Alignment=second};
    }
    static void Assign(DBText text,Values target,Database db)
    {
        text.TextStyleId=target.Style;text.Height=target.Height;text.WidthFactor=target.Width;text.Rotation=target.Rotation;text.Oblique=target.Oblique;
        text.HorizontalMode=target.H;text.VerticalMode=target.V;
        if(Two(target.H)){text.Position=target.Position;text.AlignmentPoint=target.Alignment;}
        else if(Baseline(target)){text.Position=target.Position;}
        else{text.AlignmentPoint=target.Alignment;}
        if(!Baseline(target))text.AdjustAlignment(db);
    }
    static Plan[] Resolve(ModifyArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        TextFormatValidation442.Batch(args.Operations);WriteSpace.Check(args);var db=state.Document.Database;var space=CadWriteSpace.Resolve(db,tr,args.TargetSpace);var plans=new List<Plan>();
        foreach(var edit in args.Operations.Cast<SetTextFormat442>())
        {
            cancel();var id=CadStructureWriter.Find(db,edit.Handles.Single());Need(tr.GetObject(id,OpenMode.ForRead) is DBText text&&text.GetType()==typeof(DBText)&&text.OwnerId==space,"Select exact native TEXT in the requested active space.");
            var source=(DBText)tr.GetObject(id,OpenMode.ForRead);var layer=(LayerTableRecord)tr.GetObject(source.LayerId,OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsDependent,"TEXT needs an unlocked local layer.","LAYER_UNAVAILABLE");
            var before=Capture(source,tr,cancel);var notices=new List<string>();var desired=Desired(source,before,edit,tr,cancel,notices);plans.Add(new(source,edit,before,desired,notices.ToArray()));
        }
        return plans.ToArray();
    }
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is SetTextFormat442);
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action? cancel)=>_=Resolve(args,state,tr,cancel??(()=>{}));
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context)
    {
        Transaction? tr=null;Exception? failure=null;bool started=false;string stage="preparation";
        try
        {
            tr=state.Document.Database.TransactionManager.StartTransaction();var plans=Resolve(args,state,tr,job.CheckCancellation);var changes=new List<TextFormatChange442>();var warnings=new List<string>{WriteRecovery.Warning};
            foreach(var plan in plans)
            {
                stage="native_text_format";job.CheckCancellation();plan.Source.UpgradeOpen();started=true;Assign(plan.Source,plan.Target,state.Document.Database);
                stage="native_text_readback";var after=Capture(plan.Source,tr,job.CheckCancellation);var v=after.Values;var t=plan.Target;bool aligned=t.H==TextHorizontalMode.TextAlign;
                Need(v.Style==t.Style&&v.H==t.H&&v.V==t.V&&Near(v.Oblique,t.Oblique)&&(!aligned?Near(v.Height,t.Height):v.Height>0)&&
                    (Two(t.H)?v.Width>0:Near(v.Width,t.Width))&&(Two(t.H)||Near(Math.IEEERemainder(v.Rotation-t.Rotation,2*Math.PI),0))&&Same(Anchor(v),t.Position)&&
                    (!Two(t.H)||Same(v.Alignment,t.Alignment)),"Native TEXT setters did not retain requested modes, size or authoritative anchors.","NATIVE_TEXT_FORMAT_MISMATCH");
                Need(after.Preserved.GetRawText()==plan.Before.Preserved.GetRawText(),"Native formatting changed TEXT contents, appearance, owner or unrequested owned data.","NATIVE_TEXT_FORMAT_MISMATCH");
                var notices=plan.Warnings.ToList();if(Two(t.H))notices.Add("NATIVE_TEXT_FITTED: Native two-anchor fitting determines rotation/width and, for Aligned, height; actual values are returned.");
                var reference=new EntityRef(args.BridgeId,args.DocumentId,Id(plan.Source.ObjectId));changes.Add(new(reference,after.Token,new(args.BridgeId,args.DocumentId,Id(v.Style)),v.Height,v.Width,v.Rotation,v.Oblique,Name(v.H),Name(v.V),P(Anchor(v)),Two(v.H)?P(v.Alignment):null,notices.ToArray()));warnings.AddRange(notices);
            }
            var data=new ModifiedEntities(args.OperationId,"committed",false,changes.Select(c=>c.source_ref).ToArray(),[],[],[],"not_guaranteed",text_format_changes:changes.ToArray());
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.ToArray());NativeCurveRead342.CheckSerializedBudget(result,Wire.PayloadBudget-4096,job.CheckCancellation);
            stage="native_commit";job.BeginCommit();tr.Commit();job.RecordCommit(result);
        }
        catch(Exception e){failure=e;}
        Exception? cleanup=null;try{tr?.Dispose();}catch(Exception e){cleanup=e;}
        if(job.CommittedResult is {} committed){if(cleanup!=null)committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: TEXT formatting committed; inspect current state before further changes.").ToArray()};job.RecordCommit(committed);return committed;}
        string code=failure is DomainException d?d.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_TEXT_FORMAT_FAILED";bool unknown=started||cleanup!=null;
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native TEXT formatting started; inspect actual state and do not duplicate with a new operation_id.":failure?.Message??"TEXT formatting did not start.",unknown?"unknown":"not_started",context,
            new{cause=code,stage,native_error_status=failure is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():null}) with{warnings=[WriteRecovery.Warning]};
    }
    internal static void Create(DBText text,TextSpec spec,Database db)
    {
        if(spec.Format442 is not {} f)return;var position=new Point3d(spec.Position.X,spec.Position.Y,0);var second=f.SecondAnchor is {} a?new Point3d(a.X,a.Y,0):position;
        Need(!Two(H(f.HorizontalMode))||position.DistanceTo(second)>Tolerance.Global.EqualPoint,"Two-anchor TEXT needs distinct anchors.","INVALID_GEOMETRY");
        Assign(text,new(text.TextStyleId,text.Height,f.WidthFactor,f.Rotation,f.Oblique,H(f.HorizontalMode),Vertical(f.VerticalMode),position,second,Vector3d.ZAxis),db);
    }
}
