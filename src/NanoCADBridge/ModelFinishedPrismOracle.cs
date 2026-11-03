using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Independent mass/moment oracle for bounded convex edge treatments on an exact
// WCS-aligned rectangular extrusion. This deliberately does not generalize a
// sampled B-rep or silently infer edge identity from mutable native markers.
internal static class ModelFinishedPrismOracle
{
    private static void Need(bool valid,string code,string message)
    {if(!valid)throw new DomainException(code,message);}
    private static bool Near(double a,double b)=>Math.Abs(a-b)<=1e-7*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)));
    private static bool Same(Point3 a,Point3 b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.Z,b.Z);
    // Direction 0/1/2 runs along local X/Y/Z. SideA and SideB choose the
    // two perpendicular high/low prism faces, in their natural axis order.
    private sealed record Corner(int Direction,bool SideA,bool SideB);

    private static ModelFeatureOracle.Metrics MultipleVertical(
        ModelFeatureOracle.Metrics before,ModelFeatureOracle.Metrics after,ModelFeatureOracle.Metrics observed,
        Point3 origin,(double X0,double X1,double Y0,double Y1,double Height) old,
        (double X0,double X1,double Y0,double Y1,double Height) future,
        double size,bool rounded,int edgeCount)
    {
        Need(edgeCount is >=2 and <=4,"OUT_OF_SCOPE","Supports two to four distinct vertical corner treatments in one native feature.");
        Need(old.X1-old.X0>old.Height&&old.Y1-old.Y0>old.Height&&
             future.X1-future.X0>future.Height&&future.Y1-future.Y0>future.Height,
             "OUT_OF_SCOPE","Multi-edge orientation requires a flat rectangular prism: both profile spans exceed extrusion depth.");
        double coefficient=rounded?1-Math.PI/4:.5;
        double offset=size*(rounded?(5.0/6-Math.PI/4)/(1-Math.PI/4):1.0/3);
        double oldPerEdge=coefficient*size*size*old.Height;
        Need(Near(before.Volume-observed.Volume,edgeCount*oldPerEdge)&&
             Same(before.Min,observed.Min)&&Same(before.Max,observed.Max),
             "NATIVE_GEOMETRY_MISMATCH","Current mass or bounds do not match nonoverlapping vertical corner treatments.");
        var corners=new[]{(XHigh:false,YHigh:false),(XHigh:true,YHigh:false),
                          (XHigh:false,YHigh:true),(XHigh:true,YHigh:true)};
        Point3 Cut((double X0,double X1,double Y0,double Y1,double Height) box,int index)
        {
            var c=corners[index];
            return new(origin.X+(c.XHigh?box.X1-offset:box.X0+offset),
                       origin.Y+(c.YHigh?box.Y1-offset:box.Y0+offset),
                       origin.Z+box.Height/2);
        }
        Point3 Center(ModelFeatureOracle.Metrics baseModel,
            (double X0,double X1,double Y0,double Y1,double Height) box,int mask,double perEdge)
        {
            double volume=baseModel.Volume-edgeCount*perEdge;
            Need(volume>0,"INVALID_GEOMETRY","Multi-edge treatment would leave no positive body volume.");
            double x=baseModel.Volume*baseModel.Centroid.X;
            double y=baseModel.Volume*baseModel.Centroid.Y;
            double z=baseModel.Volume*baseModel.Centroid.Z;
            for(int i=0;i<4;i++)if((mask&(1<<i))!=0)
            {
                var c=Cut(box,i);x-=perEdge*c.X;y-=perEdge*c.Y;z-=perEdge*c.Z;
            }
            return new(x/volume,y/volume,z/volume);
        }
        var matches=Enumerable.Range(1,15).Where(mask=>
            Enumerable.Range(0,4).Count(i=>(mask&(1<<i))!=0)==edgeCount&&
            Same(Center(before,old,mask,oldPerEdge),observed.Centroid)).ToArray();
        Need(matches.Length==1,"NATIVE_GEOMETRY_MISMATCH",
             "Current mass/centroid do not uniquely identify the selected vertical corners.");
        double nextPerEdge=coefficient*size*size*future.Height;
        double nextVolume=after.Volume-edgeCount*nextPerEdge;
        Need(nextVolume>0,"INVALID_GEOMETRY","Future multi-edge body would have nonpositive volume.");
        return new(nextVolume,Center(after,future,matches[0],nextPerEdge),after.Min,after.Max);
    }

    // Bounded engineering chain: a rectangular plate, separate through-holes
    // strictly inside its central inset, and one terminal outer-corner feature.
    // The holes cannot intersect the removed corner volumes. Detached CSG mass
    // is checked against an independent analytic base-minus-cylinders result.
    internal static ModelFeatureOracle.Metrics PreviewCutChain(
        ModelExtrusionSpec[] old,ModelExtrusionSpec[] future,
        ModelFeatureOracle.Metrics before,ModelFeatureOracle.Metrics after,
        ModelFeatureOracle.Metrics observed,McObjectId finishingId,Action? cancel=null)
    {
        Need(old.Length>=2&&future.Length==old.Length,
             "OUT_OF_SCOPE","Finished sketch batch supports a base and one or more through-hole cuts.");
        var fillet=finishingId.GetObjectOfType<FilletFeature>();
        var chamfer=finishingId.GetObjectOfType<ChamferFeature>();
        Need((fillet!=null)!=(chamfer!=null),"OUT_OF_SCOPE","One terminal native fillet/chamfer is required.");
        var selected=fillet?.GetEdges()?.ToArray()??chamfer?.GetEdges()?.ToArray();
        double size=fillet?.Radius??chamfer!.Distance;
        Need(selected is{Length:>=2 and <=4}&&selected.Distinct().Count()==selected.Length&&size>0,
             "OUT_OF_SCOPE","Finished cut-chain batch requires two to four distinct vertical corners in one native feature.");
        static (double X0,double X1,double Y0,double Y1,double Height) Rectangle(ModelExtrusionSpec spec)
        {
            var p=spec.Profile.Vertices;var plane=spec.Plane;
            Need(spec.Direction=="positive"&&spec.Profile.Kind=="polygon"&&p?.Length==4&&
                 Near(plane.XAxis.X,1)&&Near(plane.XAxis.Y,0)&&Near(plane.XAxis.Z,0)&&
                 Near(plane.YAxis.X,0)&&Near(plane.YAxis.Y,1)&&Near(plane.YAxis.Z,0),
                 "OUT_OF_SCOPE","Finished cut chain requires a positive WCS rectangle.");
            var vertices=p!;
            double x0=vertices.Min(v=>v.X),x1=vertices.Max(v=>v.X),
                   y0=vertices.Min(v=>v.Y),y1=vertices.Max(v=>v.Y);
            Need(x1>x0&&y1>y0&&vertices.All(v=>(Near(v.X,x0)||Near(v.X,x1))&&(Near(v.Y,y0)||Near(v.Y,y1)))&&
                 vertices.Count(v=>Near(v.X,x0)&&Near(v.Y,y0))==1&&
                 vertices.Count(v=>Near(v.X,x1)&&Near(v.Y,y0))==1&&
                 vertices.Count(v=>Near(v.X,x1)&&Near(v.Y,y1))==1&&
                 vertices.Count(v=>Near(v.X,x0)&&Near(v.Y,y1))==1,
                 "OUT_OF_SCOPE","Finished cut-chain base is not a rectangular four-corner contour.");
            return(x0,x1,y0,y1,spec.Distance);
        }
        var oldBox=Rectangle(old[0]);var newBox=Rectangle(future[0]);
        Need(old[0].Plane==future[0].Plane&&Near(oldBox.Height,newBox.Height)&&oldBox.Height>0&&
             oldBox.X1-oldBox.X0>oldBox.Height&&oldBox.Y1-oldBox.Y0>oldBox.Height&&
             newBox.X1-newBox.X0>newBox.Height&&newBox.Y1-newBox.Y0>newBox.Height&&
             size<Math.Min(oldBox.X1-oldBox.X0,Math.Min(oldBox.Y1-oldBox.Y0,oldBox.Height))/2&&
             size<Math.Min(newBox.X1-newBox.X0,Math.Min(newBox.Y1-newBox.Y0,newBox.Height))/2,
             "OUT_OF_SCOPE","Finished cut-chain prism must stay flat, with unchanged depth and nonoverlapping corner treatments.");
        ModelFeatureOracle.Metrics Analytic(ModelExtrusionSpec[] chain,
            (double X0,double X1,double Y0,double Y1,double Height) box)
        {
            var baseMass=ModelExtrusionGeometry.Measure(chain[0]);
            double volume=baseMass.Volume,mx=volume*baseMass.Centroid.X,
                   my=volume*baseMass.Centroid.Y,mz=volume*baseMass.Centroid.Z;
            var circles=new List<(Point2 Center,double Radius)>();
            foreach(var cut in chain.Skip(1))
            {
                cancel?.Invoke();
                var center=cut.Profile.Center;
                Need(cut.Plane==chain[0].Plane&&cut.Direction=="positive"&&
                     cut.Profile.Kind=="circle"&&center!=null&&cut.Profile.Radius>0&&
                     Near(cut.Distance,box.Height),
                     "OUT_OF_SCOPE","Finished cut chain allows only same-plane full-depth circular cuts.");
                double r=cut.Profile.Radius;
                Need(center!.X-r>box.X0+size&&center.X+r<box.X1-size&&
                     center.Y-r>box.Y0+size&&center.Y+r<box.Y1-size,
                     "OUT_OF_SCOPE","Hole must remain strictly inside the inset clear of every outer corner treatment.");
                Need(circles.All(other=>{cancel?.Invoke();return Math.Pow(center.X-other.Center.X,2)+Math.Pow(center.Y-other.Center.Y,2)>
                     Math.Pow(r+other.Radius,2);}),
                     "OUT_OF_SCOPE","Through-holes must be separate.");
                circles.Add((center,r));
                double removed=Math.PI*r*r*box.Height;
                var origin=chain[0].Plane.Origin;
                volume-=removed;mx-=removed*(origin.X+center.X);
                my-=removed*(origin.Y+center.Y);
                mz-=removed*(origin.Z+box.Height/2);
            }
            Need(volume>0,"INVALID_GEOMETRY","Cut chain leaves no positive body volume.");
            return new(volume,new(mx/volume,my/volume,mz/volume),baseMass.Min,baseMass.Max);
        }
        Need(ModelFeatureOracle.Same(before,Analytic(old,oldBox))&&
             ModelFeatureOracle.Same(after,Analytic(future,newBox)),
             "NATIVE_GEOMETRY_MISMATCH","Detached cut chain differs from independent plate-minus-holes mass.");
        return MultipleVertical(before,after,observed,old[0].Plane.Origin,
            oldBox,newBox,size,fillet!=null,selected!.Length);
    }

    internal static ModelFeatureOracle.Metrics Preview(Mc3dSolid body,DocumentState state,Db.Transaction tx,
        Db.Solid3d current,McObjectId extrusionId,double nextDistance,Action cancel)
    {
        var contents=ModelInventoryBudget.History(body,cancel);
        var extrusions=contents!.Select(id=>id.GetObjectOfType<ExtrudeFeature>()).Where(item=>item!=null).ToArray();
        var finishing=contents!.Where(id=>id.GetObjectOfType<FilletFeature>()!=null||id.GetObjectOfType<ChamferFeature>()!=null).ToArray();
        var views=ModelViewFollowers.Resolve(body,contents.ToArray(),cancel);
        Need(extrusions.Length==1&&extrusions[0]!.ID==extrusionId&&finishing.Length==1&&views.LastModelItem==finishing[0],
             "OUT_OF_SCOPE","Upstream finished-model edit requires one rectangular extrusion and one terminal fillet/chamfer.");
        var f=extrusions[0]!;DrawingProfileWriter.StraightExtrusion(f,body);
        var profile=f.ProfileID.GetObjectOfType<SketchProfile>();
        Need(profile!=null&&profile.HostSolidID==body.ID,"OUT_OF_SCOPE","Retained profile binding is unavailable.");
        var spec=DrawingProfileWriter.ReadTail(f,profile!.ParentSketchID,cancel).Definition;
        var plane=spec.Plane;var vertices=spec.Profile.Vertices;
        Need(spec.Direction=="positive"&&spec.Profile.Kind=="polygon"&&vertices?.Length==4&&
             Near(plane.XAxis.X,1)&&Near(plane.XAxis.Y,0)&&Near(plane.XAxis.Z,0)&&
             Near(plane.YAxis.X,0)&&Near(plane.YAxis.Y,1)&&Near(plane.YAxis.Z,0),
             "OUT_OF_SCOPE","The independent finishing oracle currently requires one positive WCS-aligned rectangular prism.");
        var points=vertices!;
        double x0=points.Min(v=>v.X),x1=points.Max(v=>v.X),y0=points.Min(v=>v.Y),y1=points.Max(v=>v.Y);
        double width=x1-x0,depth=y1-y0,height=spec.Distance;
        Need(width>0&&depth>0&&height>0&&nextDistance>0&&
             points.All(v=>(Near(v.X,x0)||Near(v.X,x1))&&(Near(v.Y,y0)||Near(v.Y,y1)))&&
             points.Count(v=>Near(v.X,x0)&&Near(v.Y,y0))==1&&
             points.Count(v=>Near(v.X,x1)&&Near(v.Y,y0))==1&&
             points.Count(v=>Near(v.X,x1)&&Near(v.Y,y1))==1&&
             points.Count(v=>Near(v.X,x0)&&Near(v.Y,y1))==1,
             "OUT_OF_SCOPE","The retained four-vertex profile is not a nondegenerate axis-aligned rectangle.");
        Need(!Near(width,depth)&&!Near(width,height)&&!Near(depth,height),
             "OUT_OF_SCOPE","Three distinct prism edge lengths are required to identify the treated direction from native mass.");
        var fillet=finishing[0].GetObjectOfType<FilletFeature>();
        var chamfer=finishing[0].GetObjectOfType<ChamferFeature>();
        double size=fillet?.Radius??chamfer!.Distance;
        var selected=fillet?.GetEdges()?.ToArray()??chamfer?.GetEdges()?.ToArray();
        Need(selected is{Length:>=1 and <=4}&&selected.Distinct().Count()==selected.Length&&
             size>0&&size<Math.Min(width,Math.Min(depth,Math.Min(height,nextDistance)))/2,
             "OUT_OF_SCOPE","Oracle requires one to four distinct convex treated edges with size below half every prism span.");
        var before=ModelParameterGeometry.Preview(body,state,tx,new Dictionary<McObjectId,double>(),
            new Dictionary<McObjectId,double>(),cancel,ignoreTerminalFinishing:true);
        var after=ModelParameterGeometry.Preview(body,state,tx,new Dictionary<McObjectId,double>{{extrusionId,nextDistance}},
            new Dictionary<McObjectId,double>(),cancel,ignoreTerminalFinishing:true);
        var measured=ModelExtrusionGeometry.Measure(spec);
        var future=ModelExtrusionGeometry.Measure(spec with{Distance=nextDistance});
        Need(ModelFeatureOracle.Same(before,new(measured.Volume,measured.Centroid,measured.Min,measured.Max))&&
             ModelFeatureOracle.Same(after,new(future.Volume,future.Centroid,future.Min,future.Max)),
             "NATIVE_GEOMETRY_MISMATCH","Detached native prism does not match its independently measured rectangle.");
        var observed=ModelFeatureOracle.Read(current);
        Need(Same(before.Min,observed.Min)&&Same(before.Max,observed.Max),
             "NATIVE_GEOMETRY_MISMATCH","Convex edge treatment changed the prism's outer bounds.");
        if(selected!.Length>1)
            return MultipleVertical(before,after,observed,plane.Origin,
                (x0,x1,y0,y1,height),(x0,x1,y0,y1,nextDistance),size,fillet!=null,selected.Length);
        double coefficient=fillet!=null?1-Math.PI/4:.5;
        double removed=before.Volume-observed.Volume;
        double inferredLength=removed/(coefficient*size*size);
        bool alongX=Near(inferredLength,width),alongY=Near(inferredLength,depth),alongZ=Near(inferredLength,height);
        Need(removed>0&&new[]{alongX,alongY,alongZ}.Count(v=>v)==1&&
             Near(removed,(alongX?width:alongY?depth:height)*coefficient*size*size),
             "NATIVE_GEOMETRY_MISMATCH","Finished volume does not identify one convex prism edge direction.");
        int direction=alongX?0:alongY?1:2;
        double centroidOffset=size*(fillet!=null?(5.0/6-Math.PI/4)/(1-Math.PI/4):1.0/3);
        Point3 RemovedCenter(Corner corner,double distance)
        {
            double x=corner.Direction==0?(x0+x1)/2:corner.SideA?x1-centroidOffset:x0+centroidOffset;
            double y=corner.Direction==1?(y0+y1)/2:
                (corner.Direction==0?corner.SideA:corner.SideB)?y1-centroidOffset:y0+centroidOffset;
            double z=corner.Direction==2?distance/2:corner.SideB?distance-centroidOffset:centroidOffset;
            return new(plane.Origin.X+x,plane.Origin.Y+y,plane.Origin.Z+z);
        }
        Point3 FinishCenter(ModelFeatureOracle.Metrics baseModel,Corner corner,double distance,double cutVolume)
        {
            var cut=RemovedCenter(corner,distance);double remaining=baseModel.Volume-cutVolume;
            return new((baseModel.Volume*baseModel.Centroid.X-cutVolume*cut.X)/remaining,
                       (baseModel.Volume*baseModel.Centroid.Y-cutVolume*cut.Y)/remaining,
                       (baseModel.Volume*baseModel.Centroid.Z-cutVolume*cut.Z)/remaining);
        }
        var corners=(from sideA in new[]{false,true} from sideB in new[]{false,true}
                     select new Corner(direction,sideA,sideB)).ToArray();
        var matched=corners.Where(c=>Same(FinishCenter(before,c,height,removed),observed.Centroid)).ToArray();
        Need(matched.Length==1,"NATIVE_GEOMETRY_MISMATCH",
             "Native finished centroid does not uniquely identify the treated rectangular edge.");
        double nextRemoved=(direction==2?nextDistance:(direction==0?width:depth))*coefficient*size*size;
        double remainingVolume=after.Volume-nextRemoved;
        Need(remainingVolume>0,"INVALID_GEOMETRY","Upstream edit would leave no positive finished volume.");
        return new(remainingVolume,FinishCenter(after,matched[0],nextDistance,nextRemoved),after.Min,after.Max);
    }

    internal static ModelFeatureOracle.Metrics PreviewSketch(Mc3dSolid body,McObjectId finishingId,
        ModelExtrusionSpec oldSpec,ModelExtrusionSpec newSpec,ModelFeatureOracle.Metrics observed,Action cancel)
    {
        static (double X0,double X1,double Y0,double Y1) Rectangle(ModelExtrusionSpec spec)
        {
            void Require(bool value){if(!value)throw new DomainException("OUT_OF_SCOPE","Finished sketch edit requires a WCS-aligned four-corner rectangular profile.");}
            var plane=spec.Plane;var points=spec.Profile.Vertices;
            Require(spec.Direction=="positive"&&spec.Profile.Kind=="polygon"&&points?.Length==4&&
                Near(plane.XAxis.X,1)&&Near(plane.XAxis.Y,0)&&Near(plane.XAxis.Z,0)&&
                Near(plane.YAxis.X,0)&&Near(plane.YAxis.Y,1)&&Near(plane.YAxis.Z,0));
            var corners=points!;
            double x0=corners.Min(v=>v.X),x1=corners.Max(v=>v.X),y0=corners.Min(v=>v.Y),y1=corners.Max(v=>v.Y);
            Require(x1>x0&&y1>y0&&corners.All(v=>(Near(v.X,x0)||Near(v.X,x1))&&(Near(v.Y,y0)||Near(v.Y,y1)))&&
                corners.Count(v=>Near(v.X,x0)&&Near(v.Y,y0))==1&&corners.Count(v=>Near(v.X,x1)&&Near(v.Y,y0))==1&&
                corners.Count(v=>Near(v.X,x1)&&Near(v.Y,y1))==1&&corners.Count(v=>Near(v.X,x0)&&Near(v.Y,y1))==1);
            return(x0,x1,y0,y1);
        }
        var old=Rectangle(oldSpec);var future=Rectangle(newSpec);
        Need(oldSpec.Plane==newSpec.Plane&&Near(oldSpec.Distance,newSpec.Distance)&&
             oldSpec.Profile.Kind==newSpec.Profile.Kind,"OUT_OF_SCOPE","The sketch operation may change only the rectangular profile in its retained plane.");
        double width=old.X1-old.X0,depth=old.Y1-old.Y0,height=oldSpec.Distance;
        Need(!Near(width,depth)&&!Near(width,height)&&!Near(depth,height),
             "OUT_OF_SCOPE","Old prism spans must differ to identify the treated edge.");
        var fillet=finishingId.GetObjectOfType<FilletFeature>();var chamfer=finishingId.GetObjectOfType<ChamferFeature>();
        Need((fillet!=null)!=(chamfer!=null),
             "OUT_OF_SCOPE","One terminal native fillet/chamfer is required.");
        double size=fillet?.Radius??chamfer!.Distance;
        var edges=fillet?.GetEdges()?.ToArray()??chamfer?.GetEdges()?.ToArray();
        Need(edges is{Length:>=1 and <=4}&&edges.Distinct().Count()==edges.Length&&size>0&&
             size<Math.Min(width,Math.Min(depth,height))/2&&
             size<Math.Min(future.X1-future.X0,Math.Min(future.Y1-future.Y0,height))/2,
             "INVALID_GEOMETRY","Changed rectangle cannot retain the selected convex edge treatment.");
        using var oldBody=ModelFeatureOracle.Tool(oldSpec,cancel);
        using var futureBody=ModelFeatureOracle.Tool(newSpec,cancel);
        var before=ModelFeatureOracle.Read(oldBody);var after=ModelFeatureOracle.Read(futureBody);
        var original=ModelExtrusionGeometry.Measure(oldSpec);var changed=ModelExtrusionGeometry.Measure(newSpec);
        Need(ModelFeatureOracle.Same(before,new(original.Volume,original.Centroid,original.Min,original.Max))&&
             ModelFeatureOracle.Same(after,new(changed.Volume,changed.Centroid,changed.Min,changed.Max))&&
             Same(before.Min,observed.Min)&&Same(before.Max,observed.Max),
             "NATIVE_GEOMETRY_MISMATCH","Detached rectangular bases or current finished bounds differ from their analytic geometry.");
        if(edges!.Length>1)
            return MultipleVertical(before,after,observed,oldSpec.Plane.Origin,
                (old.X0,old.X1,old.Y0,old.Y1,height),
                (future.X0,future.X1,future.Y0,future.Y1,height),size,fillet!=null,edges.Length);
        double coefficient=fillet!=null?1-Math.PI/4:.5;
        double loss=before.Volume-observed.Volume;
        double inferred=loss/(coefficient*size*size);
        bool alongX=Near(inferred,width),alongY=Near(inferred,depth),alongZ=Near(inferred,height);
        Need(loss>0&&new[]{alongX,alongY,alongZ}.Count(v=>v)==1,
             "NATIVE_GEOMETRY_MISMATCH","Current finished mass does not identify one convex prism edge direction.");
        int direction=alongX?0:alongY?1:2;
        double offset=size*(fillet!=null?(5.0/6-Math.PI/4)/(1-Math.PI/4):1.0/3);
        Point3 Removed((double X0,double X1,double Y0,double Y1) rect,bool sideA,bool sideB)
        {
            double x=direction==0?(rect.X0+rect.X1)/2:(sideA?rect.X1-offset:rect.X0+offset);
            double y=direction==1?(rect.Y0+rect.Y1)/2:
                ((direction==0?sideA:sideB)?rect.Y1-offset:rect.Y0+offset);
            double z=direction==2?height/2:(sideB?height-offset:offset);
            return new(oldSpec.Plane.Origin.X+x,oldSpec.Plane.Origin.Y+y,oldSpec.Plane.Origin.Z+z);
        }
        Point3 Center(ModelFeatureOracle.Metrics baseModel,double removed,(double X0,double X1,double Y0,double Y1) rect,bool sideA,bool sideB)
        {
            var cut=Removed(rect,sideA,sideB);double net=baseModel.Volume-removed;
            return new((baseModel.Volume*baseModel.Centroid.X-removed*cut.X)/net,
                       (baseModel.Volume*baseModel.Centroid.Y-removed*cut.Y)/net,
                       (baseModel.Volume*baseModel.Centroid.Z-removed*cut.Z)/net);
        }
        var candidates=(from sideA in new[]{false,true} from sideB in new[]{false,true}
                        where Same(Center(before,loss,old,sideA,sideB),observed.Centroid)
                        select(sideA,sideB)).ToArray();
        Need(candidates.Length==1,"NATIVE_GEOMETRY_MISMATCH","Current finished centroid does not identify one rectangular edge/corner.");
        double nextLoss=(direction==0?future.X1-future.X0:direction==1?future.Y1-future.Y0:height)*coefficient*size*size;
        Need(after.Volume-nextLoss>0,"INVALID_GEOMETRY","Changed profile leaves no finished solid volume.");
        return new(after.Volume-nextLoss,Center(after,nextLoss,future,candidates[0].sideA,candidates[0].sideB),after.Min,after.Max);
    }
}
