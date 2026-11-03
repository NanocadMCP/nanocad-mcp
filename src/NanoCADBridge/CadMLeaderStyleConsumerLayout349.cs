using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Observed placement supplements the edit-specific retained-content
// snapshot. Only resident defaults Apply may allow automatic text/landing reflow.
internal static class CadMLeaderStyleConsumerLayout349
{
    internal static JsonElement Capture(MLeader leader,MText text,Action cancel)
    {
        double N(double value)=>double.IsFinite(value)?value:throw new DomainException("INVALID_GEOMETRY","Native MLeader layout is nonfinite.");
        double[] P(Point3d point)=>new[]{N(point.X),N(point.Y),N(point.Z)};
        double[] V(Vector3d vector)=>new[]{N(vector.X),N(vector.Y),N(vector.Z)};
        var budget=new ModelInventoryBudget(cancel);var branches=new List<object>();
        foreach(int branch in leader.GetLeaderIndexes())
        {
            cancel();var lines=new List<object>();
            foreach(int line in leader.GetLeaderLineIndexes(branch))
            {
                cancel();int count=leader.VerticesCount(line);
                if(count<0)throw new DomainException("INVALID_GEOMETRY","Native MLeader vertex count is invalid.");
                var vertices=new List<double[]>();
                for(int index=0;index<count;index++){cancel();var point=P(leader.GetVertex(line,index));budget.Add(point);vertices.Add(point);}
                lines.Add(new{line,vertices});
            }
            var value=new{branch,dogleg=V(leader.GetDogleg(branch)),dogleg_length=N(leader.GetDoglegLength(branch)),lines};
            budget.Add(value);branches.Add(value);
        }
        var heights=new List<double>();bool columns=text.ColumnType!=ColumnType.NoColumns;
        int? columnCount=columns?text.ColumnCount:null;
        if(text.ColumnType==ColumnType.StaticColumns)
            for(int index=0;index<columnCount!.Value;index++){cancel();double value=N(text.GetColumnHeight(index));budget.Add(value);heights.Add(value);}
        var result=Wire.Element(new{branches,text_location=P(leader.TextLocation),normal=V(leader.Normal),
            alignment=leader.TextAlignmentType.ToString(),angle_type=leader.TextAngleType.ToString(),
            attachment_direction=leader.TextAttachmentDirection.ToString(),attachment_type=leader.TextAttachmentType.ToString(),
            frame=leader.EnableFrameText,
            embedded=new{location=P(text.Location),normal=V(text.Normal),direction=V(text.Direction),rotation=N(text.Rotation),
                width=N(text.Width),height=N(text.Height),text_height=N(text.TextHeight),attachment=text.Attachment.ToString(),
                columns=new{type=text.ColumnType.ToString(),count=columnCount,static_heights=heights,
                    height_scope=text.ColumnType==ColumnType.DynamicColumns?"aggregate_dynamic_height_only_individual_automatic_heights_unverified":"ordinary_or_stored_static_heights",
                    width=columns?(double?)N(text.ColumnWidth):null,gutter=columns?(double?)N(text.ColumnGutterWidth):null,
                    automatic_height=columns?(bool?)text.ColumnAutoHeight:null,reversed=columns?(bool?)text.ColumnFlowReversed:null}}});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
}
