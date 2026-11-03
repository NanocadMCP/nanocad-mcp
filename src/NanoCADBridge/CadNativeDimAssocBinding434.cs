using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using Rx=Teigha.Runtime;

namespace NanoCADBridge;

// The managed factory may expose a native object through a generic wrapper.
// Bind only the exact observed DimAssoc RX descriptor, retaining the original
// transaction-owned wrapper and borrowing its pointer without native ownership.
internal sealed class CadNativeDimAssocBinding434 : IDisposable
{
    readonly Db.DBObject original;
    readonly IntPtr pointer;
    bool borrowed;
    internal Db.DimAssoc Value { get; }

    CadNativeDimAssocBinding434(Db.DBObject original,Db.DimAssoc value,bool borrowed)
    {this.original=original;Value=value;this.borrowed=borrowed;pointer=original.UnmanagedObject;}

    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool condition,string message)
    {if(!condition)throw new DomainException("DIMENSION_ASSOCIATION_NATIVE_BINDING_UNAVAILABLE",message);}

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    static void Reject(Rx.DisposableWrapper? value,string message)
    {
        // An unproved alias must never delete the resident object, including
        // through a later owning finalizer. No ownership flags are changed.
        if(value!=null)GC.SuppressFinalize(value);
        throw new DomainException("DIMENSION_ASSOCIATION_NATIVE_BINDING_UNAVAILABLE",message);
    }

    sealed record NativeClass(bool Exact,IntPtr Pointer,object Observation);
    static NativeClass ClassIdentity(Db.DBObject source)
    {
        Rx.RXClass? actual=null,expected=null;
        try
        {
            expected=Rx.RXObject.GetClass(typeof(Db.DimAssoc));actual=source.GetRXClass();
            if(expected!=null&&expected.AutoDelete)Reject(expected,"The expected DimAssoc descriptor is not a proved borrowed wrapper.");
            if(actual!=null&&actual.AutoDelete)Reject(actual,"The actual native descriptor is not a proved borrowed wrapper.");
            Need(expected!=null&&expected.UnmanagedObject!=IntPtr.Zero&&actual!=null&&actual.UnmanagedObject!=IntPtr.Zero,
                "The actual or expected native DimAssoc descriptor is unavailable.");
            bool exact=actual!.UnmanagedObject==expected!.UnmanagedObject;
            return new(exact,actual.UnmanagedObject,new{exact_descriptor=exact,
                actual_name=actual.Name,actual_dxf_name=actual.DxfName,expected_name=expected.Name,expected_dxf_name=expected.DxfName,
                scope="Exact native descriptor equality; names do not admit a typed association wrapper."});
        }
        finally
        {
            // Native descriptors are borrowed, but managed wrapper identity is
            // not assumed distinct. Release each proved borrowed wrapper once.
            foreach(var descriptor in new[]{expected,actual}.OfType<Rx.RXClass>().Distinct(ReferenceEqualityComparer.Instance))
                if(descriptor is Rx.RXClass item){if(!item.AutoDelete)item.Dispose();else GC.SuppressFinalize(item);}
            GC.KeepAlive(source);
        }
    }

    internal static CadNativeDimAssocBinding434? TryBind(Db.DBObject source)
    {
        Need(!source.IsDisposed&&source.UnmanagedObject!=IntPtr.Zero,"The association candidate has no live native wrapper pointer.");
        var native=ClassIdentity(source);
        if(!native.Exact)return null;
        var id=source.ObjectId;var owner=source.OwnerId;var database=source.Database;
        Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased&&database!=null&&database.UnmanagedObject!=IntPtr.Zero&&
            id.Database?.UnmanagedObject==database.UnmanagedObject,
            "The native DimAssoc candidate is not a live resident object in its observed database.");
        if(source is Db.DimAssoc typed&&source.GetType()==typeof(Db.DimAssoc))return new(source,typed,false);

        var made=Rx.DisposableWrapper.Create(typeof(Db.DimAssoc),source.UnmanagedObject,false);
        if(made is not Db.DimAssoc||made.GetType()!=typeof(Db.DimAssoc)||made.AutoDelete||made.UnmanagedObject!=source.UnmanagedObject)
            Reject(made,"The SDK did not return a proved nonowning exact DimAssoc wrapper; the unproved alias was quarantined.");
        var value=(Db.DimAssoc)made;
        var binding=new CadNativeDimAssocBinding434(source,value,true);
        try
        {
            Need(!value.IsDisposed&&value.ObjectId==id&&value.OwnerId==owner&&value.Database?.UnmanagedObject==database!.UnmanagedObject&&
                value.IsWriteEnabled==source.IsWriteEnabled,
                "The borrowed DimAssoc wrapper changed resident identity, owner, database or write access.");
            var borrowedClass=ClassIdentity(value);
            Need(borrowedClass.Exact&&borrowedClass.Pointer==native.Pointer,"The borrowed DimAssoc wrapper changed the exact native descriptor.");
            return binding;
        }
        catch{binding.Dispose();throw;}
    }

    // Diagnostic observation never constructs a typed alias or mutates source.
    // A failed observation remains unavailable rather than an inferred mismatch.
    internal static object Observe(Db.DBObject source)
    {
        try
        {
            bool pointerPresent=source.UnmanagedObject!=IntPtr.Zero;
            return new{runtime_type=source.GetType().FullName,is_disposed=source.IsDisposed,resident_pointer_present=pointerPresent,
                class_observation=!source.IsDisposed&&pointerPresent?ClassIdentity(source).Observation:null,observation_error=(string?)null};
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            string reason=error is DomainException domain?domain.Error.code:error.GetType().Name;
            return new{runtime_type=source.GetType().FullName,class_observation=(object?)null,observation_error=reason};
        }
    }

    public void Dispose()
    {
        bool release=borrowed;borrowed=false;
        try
        {
            if(release)
            {
                if(Value.AutoDelete||Value.UnmanagedObject!=pointer)
                    Reject(Value,"Borrowed DimAssoc wrapper ownership or pointer changed; the unsafe alias was quarantined without disposal.");
                Value.Dispose();
            }
        }
        finally{GC.KeepAlive(original);}
    }
}
