using System.Runtime.InteropServices;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Called only from the existing CAD-thread native write queue. This adapter
// exposes the public immediate rebuild mode, not a new recovery or retry loop.
internal static class NativeModelHistory421
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AbiVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    private delegate int TargetedRebuild([MarshalAs(UnmanagedType.LPWStr)]string document,
        [MarshalAs(UnmanagedType.LPWStr)]string body,out int accepted);
    private sealed record Api(IntPtr Library,TargetedRebuild Rebuild);
    private static readonly object Sync=new();
    private static Api? loaded;
    private static DomainException Unavailable(string message)=>new("CAPABILITY_UNAVAILABLE",message);
    private static Api Load()
    {
        lock(Sync)
        {
            if(loaded!=null)return loaded;
            IntPtr library=IntPtr.Zero;
            try
            {
                string directory=Path.GetDirectoryName(typeof(NativeModelHistory421).Assembly.Location)
                    ??throw Unavailable("The native model-history adapter's bridge directory is unavailable.");
                string path=Path.Combine(directory,"NanoCAD.NativeModelHistory421.dll");
                if(!File.Exists(path))throw Unavailable("The native model-history adapter is missing beside the bridge.");
                library=NativeLibrary.Load(path,typeof(NativeModelHistory421).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32|DllImportSearchPath.ApplicationDirectory);
                var version=Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(library,"NanoCAD_NativeModelHistoryAbiVersion"));
                if(version()!=1)throw Unavailable("The native model-history adapter has an incompatible ABI.");
                var api=new Api(library,Marshal.GetDelegateForFunctionPointer<TargetedRebuild>(NativeLibrary.GetExport(library,"NanoCAD_TargetedRebuild421")));
                loaded=api;return api;
            }
            catch(Exception error)when(error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {throw Unavailable("The native model-history adapter or its installed CAD dependency could not be loaded.");}
            finally
            {
                if(loaded==null&&library!=IntPtr.Zero)NativeLibrary.Free(library);
            }
        }
    }
    internal static void EnsureAvailable()=>_ = Load();
    internal static bool Rebuild(Mc3dSolid body)
    {
        if(body==null||body.ID.IsNull||body.ID.QueryObjectType()!=typeof(Mc3dSolid))
            throw new DomainException("OBJECT_IDENTITY_MISMATCH","Immediate rebuild requires the exact current native part object.");
        var owner=body.DbEntity.Document;
        if(owner==null||owner.ID.IsNull)
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","Immediate rebuild requires the part's actual native owner document.");
        string document=owner.ID.ToString(),identity=body.ID.ToString();
        int status=Load().Rebuild(document,identity,out int accepted);
        if(status<0||accepted is not (0 or 1))
            throw new DomainException("MODEL_UPDATE_FAILED","The public native immediate rebuild could not complete its exact identity/owner/policy observations.",false,
                new{native_hresult="0x"+unchecked((uint)status).ToString("X8"),native_rebuild_accepted=accepted is 0 or 1?(bool?)(accepted==1):null,
                    owner_document_id=document,body_mc_id=identity});
        return accepted==1;
    }
}
