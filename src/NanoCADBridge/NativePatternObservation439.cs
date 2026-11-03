using System.Runtime.InteropServices;
using System.Text;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class NativePatternObservation439
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    private delegate int ReadDirection(string document,string body,string feature,int dimension,
        StringBuilder source,int capacity,out double parameter,out int marker,out long data,out int mode,out int reversed);
    private static readonly object Sync=new();
    private static ReadDirection? read;
    private static ReadDirection Load()
    {
        lock(Sync)
        {
            if(read!=null)return read;
            NativeModelHistory421.EnsureAvailable();
            try
            {
                var directory=Path.GetDirectoryName(typeof(NativePatternObservation439).Assembly.Location)!;
                var library=NativeLibrary.Load(Path.Combine(directory,"NanoCAD.NativeModelHistory421.dll"),typeof(NativePatternObservation439).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32|DllImportSearchPath.ApplicationDirectory);
                try{read=Marshal.GetDelegateForFunctionPointer<ReadDirection>(NativeLibrary.GetExport(library,"NanoCAD_ReadPatternDirection439"));}
                finally{if(read==null)NativeLibrary.Free(library);}
                return read;
            }
            catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {throw new DomainException("CAPABILITY_UNAVAILABLE","The matching native pattern-binding reader is unavailable beside this bridge.");}
        }
    }
    internal static ModelPatternDirectionBinding439 Read(ArrayFeature pattern,int dimension)
    {
        var owner=pattern.DbEntity.Document;
        if(owner==null||owner.ID.IsNull||pattern.HostSolidID.IsNull)throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","A pattern binding needs its actual native document and part.");
        var id=new StringBuilder(39);
        int status=Load()(owner.ID.ToString(),pattern.HostSolidID.ToString(),pattern.ID.ToString(),dimension,id,39,
            out double parameter,out int marker,out long data,out int mode,out int reversed);
        if(status<0||!Guid.TryParseExact(id.ToString(),"B",out var guid)||guid==Guid.Empty||!double.IsFinite(parameter)||mode is not (50 or 51)||reversed is not (0 or 1))
            throw new DomainException("PATTERN_BINDING_UNAVAILABLE","The exact native pattern direction/mode could not be observed.",details:new{native_hresult="0x"+unchecked((uint)status).ToString("X8"),feature_id=pattern.ID.ToString(),dimension});
        return new(guid.ToString("B").ToUpperInvariant(),parameter,marker,data,mode==51?"step":"range",reversed==1);
    }
    // A missing adapter cannot make previously readable history throw. The
    // caller publishes completeness explicitly; writers require Read instead.
    internal static ModelPatternDirectionBinding439? TryRead(ArrayFeature pattern,int dimension)
    {try{return Read(pattern,dimension);}catch(DomainException){return null;}}
}
