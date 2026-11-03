using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Mutates the existing target body, never replaces its database identity.
// All operands are transient copies; committed tool entities stay ForRead.
internal static class CadSolidBooleanEditor
{
    internal static void CheckTool(Entity entity,Action cancel)
    {
        cancel();
        if(entity is not Solid3d solid||entity.GetType()!=typeof(Solid3d))
            throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Boolean tools must be exact top-level native Solid3d shapes.");
        // This is the same native-copy boundary as the existing copy boolean.
        // Do not edit the tool or try to strip its native history to admit it.
        if(solid.RecordHistory||solid.ShowHistory)
            throw new DomainException("UNSUPPORTED_SOLID_HISTORY","Boolean tool shapes with enabled native history flags require their owning adapter.");
        CadSolidEditor.CheckGeometry(solid,cancel);
    }

    internal static (double BeforeVolume,double AfterVolume) Apply(SolidBooleanEditSpec spec,
        Solid3d target,Func<string,Solid3d> readTool,Action cancel)
    {
        cancel();CadSolidEditor.CheckGeometry(target,cancel);
        var nativeOperation=spec.Operator switch{
            "union"=>BooleanOperationType.BoolUnite,
            "intersection"=>BooleanOperationType.BoolIntersect,
            "subtract"=>BooleanOperationType.BoolSubtract,
            _=>throw new DomainException("INVALID_ARGUMENT","Unknown solid boolean operator.")};
        var targetProperties=Appearance(target);
        double before=target.MassProperties.Volume;
        var tools=new List<(string Handle,Solid3d Source,JsonElement State)>();
        var operands=new List<Solid3d>();
        try
        {
            // Finish all source reads/clones before changing target geometry.
            // The sole-operation contract guarantees that every tool is the
            // committed input shape rather than an earlier edit in this batch.
            foreach(var handle in spec.ToolHandles)
            {
                cancel();var source=readTool(handle);CheckTool(source,cancel);
                if(source.ObjectId.IsNull)
                    throw new DomainException("OUT_OF_SCOPE","Read committed tool solids from the current document, not transient geometry.");
                tools.Add((handle,source,State(source)));
                var operand=(Solid3d)source.Clone();operands.Add(operand);
                if(!operand.ObjectId.IsNull)
                    throw new DomainException("OBJECT_IDENTITY_MISMATCH","The native tool clone was not detached from its source.");
                CadSolidEditor.CheckGeometry(operand,cancel);
            }
            foreach(var operand in operands)
            {
                cancel();target.BooleanOperation(nativeOperation,operand);
                // An empty intersection or a subtraction removing the complete
                // part is refused on the detached preview before native target
                // writing. Never append or silently erase an empty result.
                CadSolidEditor.CheckGeometry(target,cancel);
            }
            if(!ModelParameterSnapshot.Equivalent(targetProperties,Appearance(target)))
                throw new DomainException("OBJECT_IDENTITY_MISMATCH","Boolean editing changed the target identity or display properties.");
            foreach(var tool in tools)
            {
                cancel();
                if(!ModelParameterSnapshot.Equivalent(tool.State,State(tool.Source)))
                    throw new DomainException("BOOLEAN_SOURCE_CHANGED","A committed tool shape or its properties changed during boolean editing.",false,new{handle=tool.Handle});
            }
            return(before,target.MassProperties.Volume);
        }
        finally
        {
            foreach(var operand in operands)operand.Dispose();
        }
    }

    private static JsonElement Appearance(Solid3d solid)
    {
        using var color=solid.Color;
        return Wire.Element(new{
            object_id=solid.ObjectId.ToString(),owner_id=solid.OwnerId.ToString(),
            layer_id=solid.LayerId.ToString(),linetype_id=solid.LinetypeId.ToString(),
            color=new{method=color.ColorMethod.ToString(),aci=color.ColorIndex,
                rgb=color.IsByColor?new[]{(int)color.Red,color.Green,color.Blue}:null},
            lineweight=(int)solid.LineWeight,linetype_scale=solid.LinetypeScale,visible=solid.Visible,
            record_history=solid.RecordHistory,show_history=solid.ShowHistory,has_fields=solid.HasFields});
    }

    private static JsonElement State(Solid3d solid)
    {
        var mass=solid.MassProperties;var box=solid.GeometricExtents;
        return Wire.Element(new{properties=Appearance(solid),volume=mass.Volume,
            centroid=new[]{mass.Centroid.X,mass.Centroid.Y,mass.Centroid.Z},
            min=new[]{box.MinPoint.X,box.MinPoint.Y,box.MinPoint.Z},
            max=new[]{box.MaxPoint.X,box.MaxPoint.Y,box.MaxPoint.Z}});
    }
}
