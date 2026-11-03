using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class AnnotationCreator
{
    private static Point3d P(Point2 p) => new(p.X,p.Y,0);
    public static Entity Create(EntitySpec spec, ObjectId textStyle, ObjectId annotationStyle)
    {
        if (spec is HatchSpec or AssociativeHatchSpec) return new Hatch { Normal=Vector3d.ZAxis, Elevation=0, Associative=false };
        if (spec is MLeaderSpec leader)
        {
            var result=new MLeader();
            try
            {
                result.MLeaderStyle=annotationStyle; result.ContentType=ContentType.MTextContent;
                using var text=new MText { Contents=leader.Text, TextHeight=leader.TextHeight, TextStyleId=textStyle, Location=P(leader.TextPosition) };
                result.MText=text; result.TextLocation=P(leader.TextPosition);
                int line=result.AddLeaderLine(result.AddLeader());
                foreach(var vertex in leader.Vertices) result.AddLastVertex(line,P(vertex));
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        var tableSpec=(TableSpec)spec; var table=new Table();
        try
        {
            table.TableStyle=annotationStyle; table.Position=P(tableSpec.Position);
            table.SetSize(tableSpec.Cells.Length,tableSpec.Cells[0].Length);
            table.UnmergeCells(CellRange.Create(table,0,0,tableSpec.Cells.Length-1,tableSpec.Cells[0].Length-1));
            table.SetRowHeight(tableSpec.RowHeight); table.SetColumnWidth(tableSpec.ColumnWidth);
            for(int row=0;row<tableSpec.Cells.Length;row++) for(int col=0;col<tableSpec.Cells[row].Length;col++)
            { table.Cells[row,col].Style="Data"; table.SetTextString(row,col,tableSpec.Cells[row][col]); }
            return table;
        }
        catch { table.Dispose(); throw; }
    }
    public static void Complete(Entity entity,EntitySpec spec)
    {
        if(entity is Hatch hatch && spec is HatchSpec polygon)
        {
            double bound=CadHatchPattern.Configure(hatch,polygon);
            var points=new Point2dCollection(); var bulges=new DoubleCollection();
            foreach(var p in polygon.Boundary){points.Add(new Point2d(p.X,p.Y));bulges.Add(0);}
            hatch.HatchStyle=HatchStyle.Normal;
            hatch.AppendLoop(HatchLoopTypes.External,points,bulges);
            foreach(var hole in polygon.Holes??Array.Empty<HatchHole>())
            {
                var inner=new Point2dCollection();var curves=new DoubleCollection();
                if(hole.Vertices is {} vertices)
                    foreach(var p in vertices){inner.Add(new Point2d(p.X,p.Y));curves.Add(0);}
                else
                {
                    var c=hole.Center!;
                    inner.Add(new Point2d(c.X+hole.Radius,c.Y));inner.Add(new Point2d(c.X-hole.Radius,c.Y));
                    curves.Add(1);curves.Add(1);
                }
                hatch.AppendLoop(HatchLoopTypes.Default,inner,curves);
            }
            hatch.EvaluateHatch(true);
            CadHatchPattern.VerifyEvaluation(hatch,bound);
        }
        else if(entity is Table table){table.GenerateLayout();table.RecomputeTableBlock(true);}
    }
}

