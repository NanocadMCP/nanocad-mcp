using System.Globalization;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Brep=Teigha.BoundaryRepresentation.Brep;

namespace NanoCADBridge;

// A single body/point or one solid pair, never an implicit all-body campaign.
// Native SDK signatures are established; current-host acceptance is pending.
internal static class CadSolidAnalysis
{
    internal static object Read(GeometryAnalysisArguments args,DocumentState state,Transaction transaction,
        ObjectId modelId,string instance,CancellationToken cancellation)
    {
        void Check()=>cancellation.ThrowIfCancellationRequested();
        var bodies=new List<Solid3d>();
        foreach(var handle in args.Read.Handles!)
        {
            Check();var bits=ulong.Parse(handle,NumberStyles.HexNumber,CultureInfo.InvariantCulture);
            if(!state.Document.Database.TryGetObjectId(new Handle(unchecked((long)bits)),out var id)||id.IsNull||!id.IsValid)
                throw new DomainException("OBJECT_NOT_FOUND","Solid analysis handle was not found.",false,new{handle});
            if(id.IsErased||id.IsEffectivelyErased)
                throw new DomainException("OBJECT_ERASED","Solid analysis source has been erased.",false,new{handle});
            if(transaction.GetObject(id,OpenMode.ForRead) is not Solid3d body||body.GetType()!=typeof(Solid3d)||body.OwnerId!=modelId)
                throw new DomainException("OUT_OF_SCOPE","Solid analysis requires exact top-level native Model Space Solid3d shapes.",false,new{handle});
            CadSolidEditor.CheckGeometry(body,Check);bodies.Add(body);
        }
        EntityRef Ref(Solid3d body)=>new(instance,state.Id,Validation.NormalizeHandle(body.Handle.ToString()));
        var units=new{name=state.Document.Database.Insunits.ToString(),value=(int)state.Document.Database.Insunits};
        if(args.Mode=="solid_interference")
        {
            using var first=(Solid3d)bodies[0].Clone();using var second=(Solid3d)bodies[1].Clone();
            if(!first.ObjectId.IsNull||!second.ObjectId.IsNull)
                throw new DomainException("OBJECT_IDENTITY_MISMATCH","Solid interference requires detached native shape copies.");
            Check();bool interference=first.CheckInterference(second);Check();
            return new{mode=args.Mode,coordinate_system="WCS",units,solver="native_solid_check_interference",
                first=Ref(bodies[0]),second=Ref(bodies[1]),interference,
                contact_evaluated=false,distance_evaluated=false,
                warning="This result detects solid interference. A false result does not establish positive clearance or classify tangential contact."};
        }
        if(args.Mode!="solid_containment")throw new DomainException("INVALID_ARGUMENT","Unknown solid analysis mode.");
        var point=args.Point!;var bounds=bodies[0].GeometricExtents;
        string classification,method;bool? containerAvailable=null;string? nativeClassification=null;
        if(NanoCAD.Contracts.PointContainment.OutsideNativeBounds(point,
            [bounds.MinPoint.X,bounds.MinPoint.Y,bounds.MinPoint.Z],
            [bounds.MaxPoint.X,bounds.MaxPoint.Y,bounds.MaxPoint.Z]))
        {
            classification="outside";method="conservative_native_bounds_exclusion";
        }
        else
        {
            // Inside the enclosing box is not evidence of being in material.
            // Let the live body B-rep classify cavities and boundary points.
            try
            {
                using var brep=new Brep(bodies[0]);
                if(brep.IsNull)throw new DomainException("OBJECT_READ_FAILED","Native body B-rep is unavailable.");
                Check();using var container=brep.GetPointContainment(new Point3d(point[0],point[1],point[2]),out var native);Check();
                nativeClassification=native.ToString();containerAvailable=container!=null&&!container.IsNull;
                classification=nativeClassification switch{
                    "Inside"=>"inside","Outside"=>"outside","OnBoundary"=>"boundary",
                    _=>throw new DomainException("NATIVE_CONTAINMENT_MISMATCH","Native solid containment returned an unclassified point.",false,
                        new{native_classification=nativeClassification})};
                // Volume containment may return a different topology container than
                // planar-face containment. Do not transplant the face-only oracle.
                method="native_body_brep_point_containment";
            }
            catch(Teigha.Runtime.Exception error)
            {
                // This native route failed even on an exact temporary sphere in110.
                // Report this query's SDK failure, without inferring a classification
                // or disabling other bodies, bounds exclusion or Boolean editing.
                throw new DomainException("NATIVE_SOLID_CONTAINMENT_UNAVAILABLE",
                    "Native whole-body point containment could not classify this body and point; no classification is inferred from its bounds.",false,
                    new{body=Ref(bodies[0]),point,api_status=error.ErrorStatus.ToString(),stage="whole_body_brep_classification"});
            }
        }
        return new{mode=args.Mode,coordinate_system="WCS",units,solver="native_bounds_and_body_brep",
            body=Ref(bodies[0]),point,classification,method,native_classification=nativeClassification,
            container_available=containerAvailable,tolerance_policy="native_kernel_with_conservative_bounds_exclusion"};
    }
}
