using System.Runtime.InteropServices;
using System.Text;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Reads only the SDK's explicit point/axis/second-axis bindings. It does not
// derive the old WCS frame, resolve failed source geometry or change a frame.
internal static class NativeSketchFrameObservation440
{
    internal sealed record Reference(bool is_empty,string? native_id,string complex_form,
        double? parameter,string parameter_bits,int gs_marker,long data);
    internal sealed record Observation(Reference point_ref,Reference axis_ref,Reference axis_ref2,
        bool axis_is_x,bool invert_axis);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    private delegate int ReadReferences(string document,string body,string sketch,
        StringBuilder point,StringBuilder axis,StringBuilder axis2,int capacity,
        [Out,MarshalAs(UnmanagedType.LPArray,SizeConst=3)]double[] parameters,
        [Out,MarshalAs(UnmanagedType.LPArray,SizeConst=3)]int[] markers,
        [Out,MarshalAs(UnmanagedType.LPArray,SizeConst=3)]long[] data,
        [Out,MarshalAs(UnmanagedType.LPArray,SizeConst=3)]int[] empty,
        [Out,MarshalAs(UnmanagedType.LPArray,SizeConst=3)]int[] complexPresent,
        out int axisIsX,out int invertAxis,out int unsupportedIndex);
    private static readonly object Sync=new();
    private static ReadReferences? read;
    private static ReadReferences Load()
    {
        lock(Sync)
        {
            if(read!=null)return read;
            NativeModelHistory421.EnsureAvailable();
            try
            {
                var directory=Path.GetDirectoryName(typeof(NativeSketchFrameObservation440).Assembly.Location)!;
                var library=NativeLibrary.Load(Path.Combine(directory,"NanoCAD.NativeModelHistory421.dll"),typeof(NativeSketchFrameObservation440).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32|DllImportSearchPath.ApplicationDirectory);
                try{read=Marshal.GetDelegateForFunctionPointer<ReadReferences>(NativeLibrary.GetExport(library,"NanoCAD_ReadSketchFrameReferences440"));}
                finally{if(read==null)NativeLibrary.Free(library);}
                return read;
            }
            catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {throw new DomainException("CAPABILITY_UNAVAILABLE","The matching native sketch-frame reference reader is unavailable beside this bridge.");}
        }
    }
    internal static Observation Read(PlanarSketch sketch)
    {
        var owner=sketch.DbEntity.Document;
        if(owner==null||owner.ID.IsNull||sketch.ID.IsNull||sketch.HostSolidID.IsNull)
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","Sketch frame references need the exact actual native sketch, document and part.");
        var ids=new[]{new StringBuilder(39),new StringBuilder(39),new StringBuilder(39)};
        var parameters=new double[3];var markers=new int[3];var data=new long[3];var empty=new int[3];var complex=new int[3];
        int status=Load()(owner.ID.ToString(),sketch.HostSolidID.ToString(),sketch.ID.ToString(),ids[0],ids[1],ids[2],39,
            parameters,markers,data,empty,complex,out int isX,out int inverted,out int unsupported);
        DomainException Unavailable()=>new("SKETCH_FRAME_REFERENCES_UNAVAILABLE",
            "The exact native sketch frame references could not be proved; viewport/block/complex paths are outside this part-only adapter.",
            details:new{native_hresult="0x"+unchecked((uint)status).ToString("X8"),sketch_id=sketch.ID.ToString(),unsupported_reference_index=unsupported is >=0 and <=2?(int?)unsupported:null});
        if(status!=0||isX is not (0 or 1)||inverted is not (0 or 1)||unsupported!=-1)throw Unavailable();
        var refs=new Reference[3];
        for(int index=0;index<refs.Length;index++)
        {
            if(empty[index] is not (0 or 1)||complex[index] is not (0 or 1))throw Unavailable();
            string? id=null;
            if(empty[index]==1)
            {if(ids[index].Length!=0||complex[index]!=0)throw Unavailable();}
            else
            {
                if(!Guid.TryParseExact(ids[index].ToString(),"B",out var guid)||guid==Guid.Empty)throw Unavailable();
                id=guid.ToString("B").ToUpperInvariant();
            }
            refs[index]=new(empty[index]==1,id,complex[index]==1?"canonical_simple_complex_id":empty[index]==1?"empty":"simple_id_only",
                double.IsFinite(parameters[index])?parameters[index]:null,
                unchecked((ulong)BitConverter.DoubleToInt64Bits(parameters[index])).ToString("X16"),markers[index],data[index]);
        }
        return new(refs[0],refs[1],refs[2],isX==1,inverted==1);
    }
}
