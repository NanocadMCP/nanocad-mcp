using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace NanoCADBridge;

// A native large-radius dimension can reopen through the SDK's ImpDimension
// factory. Match its native RX descriptor; never infer subtype from text or a
// managed base class. The transaction retains ownership of the original.
internal sealed class CadNativeDimensionBinding357 : IDisposable
{
    private DisposableWrapper? borrowed;
    private readonly Entity original;
    internal Entity Value { get; }
    internal bool IsBorrowed=>borrowed!=null;
    private CadNativeDimensionBinding357(Entity value,DisposableWrapper? wrapper=null,Entity? source=null){Value=value;borrowed=wrapper;original=source??value;}
    internal static bool Supported(Entity entity)=>entity is Dimension dimension&&
        (DimensionGeometryReader.Supported(entity.GetType())||NativeLargeRadius(dimension));
    private static bool NativeLargeRadius(Dimension source)
    {
        if(source.GetType().FullName!="Teigha.DatabaseServices.ImpDimension"||source.UnmanagedObject==IntPtr.Zero||source.IsDisposed)return false;
        using var actual=source.GetRXClass();using var expected=RXObject.GetClass(typeof(RadialDimensionLarge));
        return actual!=null&&expected!=null&&actual.UnmanagedObject!=IntPtr.Zero&&actual.UnmanagedObject==expected.UnmanagedObject;
    }
    internal static CadNativeDimensionBinding357 Bind(Entity source)
    {
        if(source is not Dimension dimension||!NativeLargeRadius(dimension))return new(source);
        // Public installed Create(Type,IntPtr,false) calls the protected native
        // pointer constructor. AutoDelete=false skips DeleteUnmanagedObject on
        // disposal. Do not dispose or reopen the transaction's source wrapper.
        var wrapper=DisposableWrapper.Create(typeof(RadialDimensionLarge),source.UnmanagedObject,false);
        if(wrapper==null||wrapper.AutoDelete||wrapper.UnmanagedObject!=source.UnmanagedObject)
        {
            // An unproved alias must never delete the transaction's native
            // entity, through Dispose or a later finalizer. Do not change its
            // ownership flag, adopt it, or attempt native cleanup.
            if(wrapper!=null)GC.SuppressFinalize(wrapper);
            throw new DomainException("UNSAFE_NATIVE_BINDING","The typed wrapper did not prove non-owning access to the exact source pointer; it was not adopted or disposed.");
        }
        try
        {
            if(wrapper is not RadialDimensionLarge typed||typed.GetType()!=typeof(RadialDimensionLarge)||typed.AutoDelete||
                typed.UnmanagedObject!=source.UnmanagedObject||typed.ObjectId!=source.ObjectId||typed.OwnerId!=source.OwnerId||
                typed.IsWriteEnabled!=source.IsWriteEnabled||typed.Database?.UnmanagedObject!=source.Database?.UnmanagedObject)
                throw new DomainException("OBJECT_IDENTITY_MISMATCH","Native dimension binding did not retain the exact pointer, resident identity, owner, database or access mode.");
            return new(typed,wrapper,source);
        }
        catch{wrapper?.Dispose();throw;}
    }
    internal static Entity Clone(Entity source)
    {
        var candidate=source.Clone();var clone=candidate as Entity;
        if(clone==null||!clone.AutoDelete||!clone.ObjectId.IsNull||!clone.OwnerId.IsNull||clone.UnmanagedObject==IntPtr.Zero||clone.UnmanagedObject==source.UnmanagedObject)
        {
            if(candidate is DisposableWrapper rejected&&!ReferenceEquals(rejected,source))GC.SuppressFinalize(rejected);
            throw new DomainException("UNSAFE_NATIVE_CLONE","Native dimension clone is not a distinct owned detached entity; a source/resident alias was not adopted or disposed.");
        }
        try
        {
            using var expected=Bind(source);using var actual=Bind(clone);
            using var sourceClass=source.GetRXClass();using var cloneClass=clone.GetRXClass();
            if(sourceClass==null||cloneClass==null||sourceClass.UnmanagedObject==IntPtr.Zero||
                sourceClass.UnmanagedObject!=cloneClass.UnmanagedObject||
                actual.Value.GetType()!=expected.Value.GetType()||!clone.IsNewObject||!clone.IsWriteEnabled)
                throw new DomainException("UNSAFE_NATIVE_CLONE","Detached native dimension clone changed its exact native type or access state.");
            return clone;
        }
        catch{clone.Dispose();throw;}
    }
    internal static object NativeDetails(Dimension source)
    {
        using var native=source.GetRXClass();
        return new{runtime_type=source.GetType().FullName,native_class_name=native?.Name,native_dxf_name=native?.DxfName,
            scope="exact_opened_native_dimension; no_subtype_inferred_from_dimension_text"};
    }
    public void Dispose(){var owned=borrowed;borrowed=null;try{owned?.Dispose();}finally{GC.KeepAlive(original);}}
}
