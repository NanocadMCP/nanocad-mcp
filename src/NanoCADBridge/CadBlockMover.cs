using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadBlockMover
{
    internal sealed record Resolved(CadBlockWriter.Resolved Definition,ObjectId[] Attributes,CadBlockPlacementGraph.Resolved? Graph=null);
    internal sealed record AttributeState(Point2 Position,double Rotation,JsonElement Properties,Point2? AlignmentAnchor=null,JsonElement? Multiline=null);
    internal sealed class Preview : IDisposable
    {
        public BlockReference Reference {get;}
        public AttributeReference[] Attributes {get;}
        public Preview(BlockReference reference,AttributeReference[] attributes){Reference=reference;Attributes=attributes;}
        public void Dispose()
        {
            System.Exception? error=null;
            foreach(var item in Attributes.Cast<DBObject>().Append(Reference))try{item.Dispose();}catch(System.Exception e){error??=e;}
            if(error!=null)throw new InvalidOperationException("Block preview cleanup failed.",error);
        }
    }
    private static void Require(bool condition,string message)
    {if(!condition)throw new DomainException("UNSUPPORTED_BLOCK_CONTENT",message);}
    private static void Close(double actual,double expected)
    {if(!BlockMoveGeometry.Close(actual,expected))throw new DomainException("TRANSFORM_MISMATCH","Native block/attribute transform differs from the independent WCS result.");}
    private static void Plane(double value)=>Require(double.IsFinite(value)&&Math.Abs(value)<=1e-9,"Blocks require finite WCS XY placement at Z=0; reflected native frames are retained.");
    private static Point2 XY(Point3d p){Plane(p.Z);var point=new Point2(p.X,p.Y);BlockMoveGeometry.Point(point);return point;}
    private static void Dependencies(DBObject obj)
    {Require(!obj.HasFields&&!CadDrawingGroups431.HasNonOrganizationalReactors(obj),"Block transformation excludes fields and geometric persistent dependencies; native selection Groups are retained.");}
    internal static void CheckRoot(BlockReference b)
    {
        Require((b.GetType()==typeof(BlockReference)||b.GetType()==typeof(MInsertBlock))&&!b.IsDynamicBlock&&b.Annotative!=AnnotativeStates.True,
            "Only exact native non-annotative static BlockReference/MInsertBlock occurrences use this placement adapter.");
        Dependencies(b);XY(b.Position);Plane(b.Normal.X);Plane(b.Normal.Y);Plane(Math.Abs(b.Normal.Z)-1);
        var s=b.ScaleFactors;
#if BLOCK_SCALE_FAILURE_PROBE
        if(BlockMoveGeometry.UniformScale(s.X,s.Y,s.Z)&&(s.X!=s.Y||s.X!=s.Z))BlockScaleFailureProbe.RecordRoundoff(s.X,s.Y,s.Z,b.ObjectId.IsNull);
#endif
        Require(SignedUniform(s.X,s.Y,s.Z),"Block scale magnitudes must be finite, nonzero and uniform; reflected placement is retained.");
        BlockWriteValidation.NormalizeRotation(b.Rotation);
        if(b is MInsertBlock array)
        {Require(!array.AttributeCollection.Cast<ObjectId>().Any(),"MInsert placement supports attribute-free occurrences; per-item attributes need their owning adapter.");NativeBlockArrayValidation384.Validate(CadBlockArray384.Parameters(array));}
    }
    internal static bool SignedUniform(double x,double y,double z)=>BlockMoveGeometry.UniformScale(Math.Abs(x),Math.Abs(y),Math.Abs(z));
    internal static void RequireAttributeGeometry(AttributeReference a)
    {
        XY(a.Position);XY(a.AlignmentPoint);Plane(a.Normal.X);Plane(a.Normal.Y);Plane(Math.Abs(a.Normal.Z)-1);
        Require(double.IsFinite(a.Height)&&a.Height>0&&double.IsFinite(a.WidthFactor)&&a.WidthFactor>0&&
            double.IsFinite(a.Rotation)&&double.IsFinite(a.Thickness),"Owned attribute geometry must be finite and planar.");
    }
    internal static object AttributeSnapshot(AttributeReference a,Action cancel)=>Capture(a,cancel);
    private static AttributeState Capture(AttributeReference a,Action cancel)
    {
        Require(a.GetType()==typeof(AttributeReference)&&!a.IsConstant&&
            (a.IsMTextAttribute||a.HorizontalMode is TextHorizontalMode.TextLeft or TextHorizontalMode.TextCenter or TextHorizontalMode.TextRight)&&
            Enum.IsDefined(typeof(TextVerticalMode),a.VerticalMode)&&double.IsFinite(a.Oblique)&&Math.Abs(a.Oblique)<Math.PI/2,
            "Block placement supports native variable multiline attributes and single-line left/center/right attributes; single-line fit/aligned and Fields require their owning route.");
        Dependencies(a);RequireAttributeGeometry(a);
        if(!a.IsMTextAttribute)Require(StructureWriteValidation.IsPlainAttribute(a.TextString),"Single-line attribute text must be bounded plain text.");
        var multiline=NativeBlockMultiline442.Capture(a,cancel);
        return new(XY(a.Position),BlockWriteValidation.NormalizeRotation(a.Rotation),Wire.Element(new{
            a.Tag,TextString=a.IsMTextAttribute?null:a.TextString,a.Height,a.WidthFactor,a.Oblique,a.Invisible,a.IsConstant,a.IsMTextAttribute,a.LockPositionInBlock,a.IsMirroredInX,a.IsMirroredInY,
            style=a.TextStyleId.Handle.ToString(),layer=a.LayerId.Handle.ToString(),linetype=a.LinetypeId.Handle.ToString(),
            color=a.ColorIndex,lineweight=(int)a.LineWeight,a.Visible,a.Thickness,stored=PreservedProperties(a),xdata=NativeBlockMultiline442.RootXData(a,cancel),
            normal=new[]{a.Normal.X,a.Normal.Y,a.Normal.Z},horizontal=(int)a.HorizontalMode,vertical=(int)a.VerticalMode}),
            a.IsMTextAttribute||a.HorizontalMode==TextHorizontalMode.TextLeft&&a.VerticalMode==TextVerticalMode.TextBase?null:XY(a.AlignmentPoint),multiline);
    }
    private static JsonElement Properties(BlockReference b)=>Wire.Element(new{
        definition=b.BlockTableRecord.Handle.ToString(),layer=b.LayerId.Handle.ToString(),linetype=b.LinetypeId.Handle.ToString(),
        color=b.ColorIndex,lineweight=(int)b.LineWeight,b.Visible,scale=new[]{b.ScaleFactors.X,b.ScaleFactors.Y,b.ScaleFactors.Z},
        normal=new[]{b.Normal.X,b.Normal.Y,b.Normal.Z},stored=PreservedProperties(b),
        array=b is MInsertBlock a?CadBlockArray384.Parameters(a):null});
    internal static object PreservedProperties(Entity entity)
    {
        var transparency=entity.Transparency;var plot=entity.PlotStyleNameId;
        string? Handle(ObjectId id)=>id.IsNull?null:id.Handle.ToString();
        return new{common=EntityAdapter.StoredProperties(entity),entity.LinetypeScale,material=Handle(entity.MaterialId),
            plot_type=plot.Type.ToString(),plot_id=Handle(plot.Id),
            transparency=new{alpha=transparency.Alpha,by_alpha=transparency.IsByAlpha,by_layer=transparency.IsByLayer,by_block=transparency.IsByBlock}};
    }
    internal static JsonElement Snapshot(Preview preview,Action cancel)=>Wire.Element(new{
        position=new[]{preview.Reference.Position.X,preview.Reference.Position.Y,preview.Reference.Position.Z},preview.Reference.Rotation,
        properties=Properties(preview.Reference),attributes=preview.Attributes.Select(a=>Capture(a,cancel)).ToArray()});
    private static void Same(JsonElement a,JsonElement b)
    {if(!BlockMoveGeometry.SameProperties(a,b))throw new DomainException("TRANSFORM_MISMATCH","Block transformation changed preserved content or properties.");}
    public static Resolved Resolve(BlockReference b,Database db,Transaction tr,ObjectId model,Action cancel)
    {
        var scope = CadBlockSpace.RequireTopLevel(b, db, tr);
        if (scope.Space != model || CadBlockSpace.RequireWriteSpace(db, tr, scope.Kind, cancel) != model)
            throw new DomainException("OUT_OF_SCOPE", "Block transform requires an occurrence in the requested active Model or paper layout.");
        CheckRoot(b);
        LayerWriteState Layer(ObjectId id)
        {var l=(LayerTableRecord)tr.GetObject(id,OpenMode.ForRead);return new(l.Name,id==db.Clayer,l.IsDependent,l.IsOff,l.IsFrozen,l.IsLocked);}
        var ids=new List<ObjectId>();long attributeBytes=0;
        foreach(ObjectId id in b.AttributeCollection)
        {
            cancel();Require(!id.IsErased&&!id.IsEffectivelyErased&&id.IsValid,"Only live owned attributes are supported.");
            Require(tr.GetObject(id,OpenMode.ForRead) is AttributeReference,"Unexpected attribute collection member.");
            var a=(AttributeReference)tr.GetObject(id,OpenMode.ForRead);Require(a.OwnerId==b.ObjectId,"Attribute ownership mismatch.");
            CadBlockPlacementGraph.CheckLeaf(a,db,tr,cancel);
            var state=Capture(a,cancel);Layer(a.LayerId).RequireAttributeWrite();
            attributeBytes+=JsonSerializer.SerializeToUtf8Bytes(state,Wire.Json).Length;
            if(attributeBytes>Wire.PayloadBudget-8192)throw new DomainException("ENTITY_TOO_LARGE","Owned attribute snapshots exceed the actual byte budget.");
            ids.Add(id);
        }
        var graph=CadBlockPlacementGraph.Resolve(b,db,tr,cancel);
        var block=(BlockTableRecord)tr.GetObject(b.BlockTableRecord,OpenMode.ForRead);
        // The native BlockTransform is authoritative for reflected frames.
        // This placement only carries source/base identities to older callers.
        var placement=new BlockPlacement(XY(block.Origin),XY(b.Position),b.Rotation,Math.Abs(b.ScaleFactors.X));
        var layers=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);
        var linetypes=(LinetypeTable)tr.GetObject(db.LinetypeTableId,OpenMode.ForRead);
        var definition=new CadBlockWriter.Resolved(db.BlockTableId,model,b.BlockTableRecord,layers["0"],b.LayerId,linetypes["ByLayer"],block.Name,
            Array.Empty<ObjectId>(),Array.Empty<ObjectId>(),graph.Leaves.Select(l=>l.Id).Distinct().ToArray(),Array.Empty<ObjectId>(),
            Array.Empty<BlockAttributeSpec>(),Array.Empty<string>(),placement,graph.Vertices){Graph=graph};
        BlockMoveGeometry.Point(definition.Placement!.BasePoint);CheckRoot(b);
        return new(definition,ids.ToArray(),graph);
    }
    private static BlockReference DetachedReference(BlockReference original)
    {
        if(original is MInsertBlock array)
            return new MInsertBlock(original.Position,original.BlockTableRecord,array.Columns,array.Rows,array.ColumnSpacing,array.RowSpacing);
        return new BlockReference(original.Position,original.BlockTableRecord);
    }
    public static Preview Prepare(BlockReference original,Resolved resolved,Transaction tr,Action cancel)
    {
        // Construct only the detached occurrence. Do not ask Clone to duplicate the
        // database-owned attribute collection or shared definition implicitly.
        var reference=DetachedReference(original);
        var attributes=new List<AttributeReference>();
        try
        {
            reference.Normal=original.Normal;reference.Rotation=original.Rotation;reference.ScaleFactors=original.ScaleFactors;
            reference.SetPropertiesFrom(original);
            foreach(var id in resolved.Attributes)
            {
                cancel();var source=(AttributeReference)tr.GetObject(id,OpenMode.ForRead);
                var a=source.IsMTextAttribute?(AttributeReference)NativeBlockMultiline442.Detached(source,cancel):(AttributeReference)source.Clone();attributes.Add(a);
                Require(a.ObjectId.IsNull,"Attribute preview must be detached.");
            }
            VerifyMatrix(reference,original.BlockTransform);Same(Properties(original),Properties(reference));
            return new(reference,attributes.ToArray());
        }
        catch{foreach(var a in attributes)a.Dispose();reference.Dispose();throw;}
    }
    internal static Preview DuplicatePreview(Preview original,Action cancel)
    {
        var b=original.Reference;var reference=DetachedReference(b);var attributes=new List<AttributeReference>();
        try
        {
            reference.Normal=b.Normal;reference.Rotation=b.Rotation;reference.ScaleFactors=b.ScaleFactors;reference.SetPropertiesFrom(b);
            foreach(var a in original.Attributes){cancel();attributes.Add(a.IsMTextAttribute?(AttributeReference)NativeBlockMultiline442.Detached(a,cancel):(AttributeReference)a.Clone());Require(attributes[^1].ObjectId.IsNull,"Attribute copy preview must be detached.");}
            var copy=new Preview(reference,attributes.ToArray());Same(Snapshot(original,cancel),Snapshot(copy,cancel));return copy;
        }
        catch{foreach(var a in attributes)a.Dispose();reference.Dispose();throw;}
    }
    private static void VerifyMatrix(BlockReference b,Matrix3d expected)
    {
        CheckRoot(b);var matrix=b.BlockTransform;
        for(int row=0;row<4;row++)for(int col=0;col<4;col++)Close(matrix[row,col],expected[row,col]);
    }
    public static void Transform(BlockReference b,AttributeReference[] attributes,Resolved resolved,EditSpec operation,Transaction tr,Action cancel,bool preview)
        =>TransformMatrix(b,attributes,resolved,CadModifier.Matrix(operation),tr,cancel,preview,operation is Mirror2dSpec);
    internal static void TransformMatrix(BlockReference b,AttributeReference[] attributes,Resolved resolved,Matrix3d matrix,Transaction tr,Action cancel,bool preview,bool keepAttributeTextReadable=false)
    {
        string stage="capture";
        try
        {
        resolved.Graph?.Verify(tr,cancel);
        var expectedMatrix=matrix*b.BlockTransform;
        // Native MInsert transforms may update its spacing representation.
        // Compare the inherited adapter on a constructed detached occurrence,
        // while independently checking the complete reference matrix product.
        using var expectedReference=DetachedReference(b);
        expectedReference.Normal=b.Normal;expectedReference.Rotation=b.Rotation;expectedReference.ScaleFactors=b.ScaleFactors;
        expectedReference.SetPropertiesFrom(b);expectedReference.TransformBy(matrix);
        var expectedRoot=Properties(expectedReference);
        var expected=new AttributeState[attributes.Length];var textFlags=new (bool X,bool Y)[attributes.Length];
        var retainedMultiline=new JsonElement?[attributes.Length];
        for(int i=0;i<attributes.Length;i++)
        {
            cancel();var source=attributes[i];textFlags[i]=(source.IsMirroredInX,source.IsMirroredInY);
            if(!preview&&source.IsMTextAttribute)retainedMultiline[i]=NativeBlockMultiline442.RootData(source,tr,cancel);
            var embeddedExpected=NativeBlockMultiline442.Expected(source,matrix,cancel);
            using var desired=source.IsMTextAttribute?(AttributeReference)NativeBlockMultiline442.Detached(source,cancel):(AttributeReference)source.Clone();
            Require(desired.ObjectId.IsNull,"Attribute transform oracle must be detached.");desired.TransformBy(matrix);
            if(keepAttributeTextReadable){desired.IsMirroredInX=textFlags[i].X;desired.IsMirroredInY=textFlags[i].Y;}
            NativeBlockMultiline442.Same(embeddedExpected,NativeBlockMultiline442.Capture(desired,cancel));
            expected[i]=Capture(desired,cancel);
        }
        stage="reference_transform";b.TransformBy(matrix);
        stage="attribute_transform";
        for(int i=0;i<attributes.Length;i++)
        {
            cancel();
            if(preview)attributes[i].TransformBy(matrix);
            // The native occurrence transform owns its database attributes. It
            // can close their prior write-open state. Re-read by identity and
            // verify the result; do not transform those attributes a second time.
            else attributes[i]=(AttributeReference)tr.GetObject(resolved.Attributes[i],keepAttributeTextReadable?OpenMode.ForWrite:OpenMode.ForRead);
            // Occurrence attributes are literal values, not definition glyphs.
            // Reflection keeps each value's existing glyph mirror flags, while
            // their anchors and native text frame follow the reflected placement.
            if(keepAttributeTextReadable){attributes[i].IsMirroredInX=textFlags[i].X;attributes[i].IsMirroredInY=textFlags[i].Y;}
        }
        stage="verify";
        VerifyMatrix(b,expectedMatrix);Same(expectedRoot,Properties(b));
        for(int i=0;i<attributes.Length;i++)
        {
            var a=Capture(attributes[i],cancel);Close(a.Position.X,expected[i].Position.X);Close(a.Position.Y,expected[i].Position.Y);
            Close(Math.IEEERemainder(a.Rotation-expected[i].Rotation,2*Math.PI),0);Same(a.Properties,expected[i].Properties);
            NativeBlockMultiline442.Same(expected[i].Multiline,a.Multiline);
            if(retainedMultiline[i] is {} retained)Same(retained,NativeBlockMultiline442.RootData(attributes[i],tr,cancel));
            Require((a.AlignmentAnchor is not null)==(expected[i].AlignmentAnchor is not null),"Attribute alignment mode changed during transform.");
            if(a.AlignmentAnchor is {} actualAnchor && expected[i].AlignmentAnchor is {} expectedAnchor)
            {Close(actualAnchor.X,expectedAnchor.X);Close(actualAnchor.Y,expectedAnchor.Y);}
        }
        if(preview)
        {
            var leaves=resolved.Graph?.Leaves??resolved.Definition.PrimitiveIds.Select(id=>new CadBlockPlacementGraph.Leaf(id,Matrix3d.Identity)).ToArray();
            foreach(var leaf in leaves)
            {
                cancel();var source=(Entity)tr.GetObject(leaf.Id,OpenMode.ForRead);
                // Native reference placement leaves resident fill loops unchanged.
                // Detached Hatch transforms/PAT lookup are not availability gates.
                if(source.GetType()==typeof(Hatch)){CadBlockTemplate391.RequireHatch((Hatch)source,cancel);continue;}
                var multiline=source is DBText text&&NativeAttributeMText377.IsMultiline(text);
                using var clone=multiline?NativeBlockMultiline442.Detached((DBText)source,cancel):(Entity)source.Clone();
                Require(clone.ObjectId.IsNull,"Definition geometry preview must be detached.");
                var leafMatrix=b.BlockTransform*leaf.ToRoot;
                var embeddedExpected=multiline?NativeBlockMultiline442.Expected((DBText)source,leafMatrix,cancel):null;
                clone.TransformBy(leafMatrix);
                if(multiline)NativeBlockMultiline442.Same(embeddedExpected,NativeBlockMultiline442.Capture((DBText)clone,cancel));
                // A reflected native text/curve frame may have -Z normal.
                // The exact transform matrix and retained source graph are the
                // geometry checks; +Z-only writer admission is not an oracle.
                // Empty/invisible text and variable attribute definitions have
                // no rendered extents. Their planar anchors and size are
                // checked above; native glyph extents are not a placement gate.
                if(clone is DBText or MText)continue;
                var bounds=clone.GeometricExtents;XY(bounds.MinPoint);XY(bounds.MaxPoint);
            }
        }
        resolved.Graph?.Verify(tr,cancel);
        }
        catch(Teigha.Runtime.Exception error)
        {throw new DomainException("BLOCK_TRANSFORM_REJECTED","Native block transform rejected at "+stage,false,new{stage,preview,api_status=error.ErrorStatus.ToString()});}
    }
    public static void Apply(BlockReference b,Resolved resolved,EditSpec operation,Transaction tr,Action cancel)
    {
        var id = b.ObjectId; var owner = b.OwnerId; var definition = b.BlockTableRecord;
        var scope = CadBlockSpace.RequireTopLevel(b, b.Database, tr);
        if (owner != resolved.Definition.Model || CadBlockSpace.RequireWriteSpace(b.Database, tr, scope.Kind, cancel) != owner)
            throw new DomainException("OUT_OF_SCOPE", "Active block transform owner changed before apply.");
        AttributeReference[] attributes;
        try{attributes=resolved.Attributes.Select(id=>(AttributeReference)tr.GetObject(id,OpenMode.ForWrite)).ToArray();}
        catch(Teigha.Runtime.Exception error)
        {throw new DomainException("BLOCK_TRANSFORM_REJECTED","Native attribute write-open rejected.",false,new{stage="attribute_open",preview=false,api_status=error.ErrorStatus.ToString()});}
        Transform(b,attributes,resolved,operation,tr,cancel,false);
        Require(b.ObjectId == id && b.OwnerId == owner && b.BlockTableRecord == definition && !definition.IsErased && !definition.IsEffectivelyErased,
            "Block transformation changed occurrence identity, owning space or shared definition.");
        Require(b.AttributeCollection.Cast<ObjectId>().SequenceEqual(resolved.Attributes),"Attribute collection identity changed during transform.");
    }
}
