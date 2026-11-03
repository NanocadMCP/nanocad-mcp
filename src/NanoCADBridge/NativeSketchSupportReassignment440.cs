using System.Runtime.InteropServices;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// One internal native support/frame operation on the existing CAD write queue.
// Native McGeometryParam copies never cross the ABI or get reconstructed here.
internal static class NativeSketchSupportReassignment440
{
    internal sealed record UpdateObservation(string sketch_update_hresult,string update_all_hresult);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    private delegate int Reassign(string document,string body,string sketch,string expectedSupport,string targetSupport,
        out int phase,out int supportResult,out int frameResult,out int sketchUpdateResult,out int updateAllResult);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    private delegate int UseTarget(string document,string body,string sketch,string expectedSupport,string targetSupport,
        out int phase,out int supportResult,out int sketchUpdateResult,out int updateAllResult);
    private static readonly object Sync=new();
    private static Reassign? reassign;
    private static UseTarget? useTarget;
    private static Reassign Load()
    {
        lock(Sync)
        {
            if(reassign!=null)return reassign;
            NativeModelHistory421.EnsureAvailable();
            try
            {
                var directory=Path.GetDirectoryName(typeof(NativeSketchSupportReassignment440).Assembly.Location)!;
                var library=NativeLibrary.Load(Path.Combine(directory,"NanoCAD.NativeModelHistory421.dll"),typeof(NativeSketchSupportReassignment440).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32|DllImportSearchPath.ApplicationDirectory);
                try{reassign=Marshal.GetDelegateForFunctionPointer<Reassign>(NativeLibrary.GetExport(library,"NanoCAD_ReassignSketchSupportPreservingFrameWithUpdate440"));}
                finally{if(reassign==null)NativeLibrary.Free(library);}
                return reassign;
            }
            catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {throw new DomainException("CAPABILITY_UNAVAILABLE","The matching native sketch support/frame preservation adapter is unavailable beside this bridge.");}
        }
    }
    private static UseTarget LoadTarget()
    {
        lock(Sync)
        {
            if(useTarget!=null)return useTarget;NativeModelHistory421.EnsureAvailable();
            try
            {
                var directory=Path.GetDirectoryName(typeof(NativeSketchSupportReassignment440).Assembly.Location)!;
                var library=NativeLibrary.Load(Path.Combine(directory,"NanoCAD.NativeModelHistory421.dll"),typeof(NativeSketchSupportReassignment440).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32|DllImportSearchPath.ApplicationDirectory);
                try{useTarget=Marshal.GetDelegateForFunctionPointer<UseTarget>(NativeLibrary.GetExport(library,"NanoCAD_ReassignSketchSupportUsingTargetFrame440"));}
                finally{if(useTarget==null)NativeLibrary.Free(library);}return useTarget;
            }
            catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {throw new DomainException("CAPABILITY_UNAVAILABLE","The matching explicit target-frame native adapter is unavailable beside this bridge.");}
        }
    }
    internal static void EnsureAvailable(bool targetFrame=false){if(targetFrame)_ = LoadTarget();else _ = Load();}
    internal static UpdateObservation Apply(PlanarSketch sketch,Mc3dSolid body,McObjectId expectedSupport,McObjectId targetSupport,bool targetFrame=false)
    {
        var owner=body.DbEntity.Document;
        if(owner==null||owner.ID.IsNull||body.ID.IsNull||sketch.ID.IsNull||sketch.HostSolidID!=body.ID||
            sketch.DbEntity.Document?.ID!=owner.ID||expectedSupport.IsNull||targetSupport.IsNull)
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","Support/frame preservation requires the exact current sketch, native part, owner and observed support IDs.");
        int phase,supportResult,sketchUpdateResult,updateAllResult,frameResult=0;
        int status=targetFrame?LoadTarget()(owner.ID.ToString(),body.ID.ToString(),sketch.ID.ToString(),expectedSupport.ToString(),targetSupport.ToString(),
            out phase,out supportResult,out sketchUpdateResult,out updateAllResult):
            Load()(owner.ID.ToString(),body.ID.ToString(),sketch.ID.ToString(),expectedSupport.ToString(),targetSupport.ToString(),
            out phase,out supportResult,out frameResult,out sketchUpdateResult,out updateAllResult);
        static string Hex(int value)=>"0x"+unchecked((uint)value).ToString("X8");
        if(status!=0||phase!=7||supportResult!=0||frameResult!=0||sketchUpdateResult<0||updateAllResult<0)
            throw new DomainException("MODEL_SKETCH_SUPPORT_REASSIGNMENT_FAILED",
                "The native support/frame operation did not verify the exact retained associative references; no fallback, rollback or replay was performed.",false,
                new{native_hresult=Hex(status),native_phase=phase,
                    native_stage=phase switch{0=>"identity_or_original_frame_capture",1=>"original_frame_captured",2=>"support_setter_entered",3=>"support_setter_returned",
                        4=>"frame_setter_entered",5=>"frame_setter_returned",6=>"frame_readback_returned",7=>"exact_frame_support_verified",
                        8=>"sketch_update_entered",9=>"sketch_update_returned",10=>"manager_update_all_entered",11=>"manager_update_all_returned",
                        12=>"current_support_owner_after_update_verified",_=>"invalid_native_phase"},
                    support_hresult=Hex(supportResult),frame_hresult=targetFrame?null:Hex(frameResult),frame_reference_policy=targetFrame?"use_target_datum":"preserve",sketch_update_hresult=Hex(sketchUpdateResult),update_all_hresult=Hex(updateAllResult),
                    sketch_id=sketch.ID.ToString(),expected_support_id=expectedSupport.ToString(),target_support_id=targetSupport.ToString()});
        return new(Hex(sketchUpdateResult),Hex(updateAllResult));
    }
}
