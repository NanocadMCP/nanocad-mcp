using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Multicad;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Only public, read-only SDK role lookup crosses this ABI. Formula evaluation
// and mutation remain in the existing owning MCS graph on the CAD thread.
internal static class NativeFeatureParameters412
{
    internal sealed record ShellThickness(McObjectId Variable,double Value);
    internal sealed record HolePlacement(string Definition,McObjectId FirstVariable,McObjectId SecondVariable,double FirstValue,double SecondValue);
    private const int Capacity=39;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AbiVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    private delegate int ShellRead([MarshalAs(UnmanagedType.LPWStr)]string document,[MarshalAs(UnmanagedType.LPWStr)]string feature,int channel,
        [Out]StringBuilder variable,int capacity,out double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    private delegate int HoleRead([MarshalAs(UnmanagedType.LPWStr)]string document,[MarshalAs(UnmanagedType.LPWStr)]string feature,out int definition,
        [Out]StringBuilder first,int firstCapacity,out double firstValue,[Out]StringBuilder second,int secondCapacity,out double secondValue);
    private sealed record Api(IntPtr Library,ShellRead Shell,HoleRead Hole);
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
                string directory=Path.GetDirectoryName(typeof(NativeFeatureParameters412).Assembly.Location)
                    ??throw Unavailable("The native parameter reader's bridge directory is unavailable.");
                string path=Path.Combine(directory,"NanoCAD.NativeParameters412.dll");
                if(!File.Exists(path))throw Unavailable("The native parameter reader is missing beside the bridge; other parameter families remain available.");
                // Absolute trusted bridge directory; do not resolve our helper
                // from the working directory or a user drawing search path.
                library=NativeLibrary.Load(path,typeof(NativeFeatureParameters412).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32|DllImportSearchPath.ApplicationDirectory);
                var version=Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(library,"NanoCAD_NativeParametersAbiVersion"));
                if(version()!=1)throw Unavailable("The native parameter reader has an incompatible ABI.");
                var api=new Api(library,Marshal.GetDelegateForFunctionPointer<ShellRead>(NativeLibrary.GetExport(library,"NanoCAD_GetShellThicknessVariable412")),
                    Marshal.GetDelegateForFunctionPointer<HoleRead>(NativeLibrary.GetExport(library,"NanoCAD_GetHolePlacementVariables412")));
                loaded=api;return api; // Loaded only on success; no cached failure disables a family.
            }
            catch(Exception error)when(error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {throw Unavailable("The native parameter reader or its installed CAD dependency could not be loaded.");}
            finally
            {
                // Keep successful typed delegates and their module resident for
                // this bridge lifetime. Failed loads own no retained SDK objects.
                if(loaded==null&&library!=IntPtr.Zero)NativeLibrary.Free(library);
            }
        }
    }
    private static void Input(McObjectId document,McObjectId feature)
    {if(document.IsNull||feature.IsNull)throw new DomainException("INVALID_ARGUMENT","Read a current native owner document and feature identity.");}
    private static McObjectId Id(StringBuilder text)
    {
        if(text.Length==0)return McObjectId.Null;
        if(!Guid.TryParseExact(text.ToString(),"B",out var id)||id==Guid.Empty)
            throw new DomainException("MODEL_STATE_UNAVAILABLE","The native parameter reader returned an invalid existing variable identity.");
        return new McObjectId(id); // Public SDK McObjectId(Guid), not a native pointer cast.
    }
    private static void Result(int status)
    {if(status<0)throw new DomainException("VARIABLE_BINDING_UNAVAILABLE","The public native SDK could not read this exact feature parameter binding.",false,new{native_hresult="0x"+unchecked((uint)status).ToString("X8")});}
    internal static ShellThickness ReadShell(McObjectId document,McObjectId feature,int channel=0)
    {
        Input(document,feature);if(channel is not (0 or 1))throw new DomainException("INVALID_ARGUMENT","Unknown native thickness slot.");
        var text=new StringBuilder(Capacity);int status=Load().Shell(document.ToString(),feature.ToString(),channel,text,Capacity,out double value);Result(status);
        if(!double.IsFinite(value))throw new DomainException("MODEL_STATE_UNAVAILABLE","The native thickness value is unavailable.");
        return new(Id(text),value);
    }
    internal static HolePlacement ReadHolePlacement(McObjectId document,McObjectId feature)
    {
        Input(document,feature);var first=new StringBuilder(Capacity);var second=new StringBuilder(Capacity);
        int status=Load().Hole(document.ToString(),feature.ToString(),out int definition,first,Capacity,out double a,second,Capacity,out double b);Result(status);
        // Some placement modes have no active second scalar. Its raw NaN is
        // not an error for an unrelated role; the typed adapter validates only
        // the actual roles it admits, against the managed getter and owner MCS.
        string kind=definition switch{0=>"Unknown",1=>"TwoEdges",2=>"TwoVertices",3=>"Concentric",4=>"Radial",5=>"Fixed",
            100=>"AlongEdges",101=>"CircularArray",102=>"RectangularArray",200=>"Dependent",201=>"PartCS",_=>"UnknownNative:"+definition};
        return new(kind,Id(first),Id(second),a,b);
    }
}
