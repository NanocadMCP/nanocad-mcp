using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadBlockWriter
{
    internal sealed record Resolved(ObjectId BlockTable, ObjectId Model, ObjectId Definition, ObjectId LayerZero,
        ObjectId InsertionLayer, ObjectId ByLayerLinetype, string Name, ObjectId[] EntityStyles, ObjectId[] AttributeStyles,
        ObjectId[] PrimitiveIds, ObjectId[] AttributeIds, BlockAttributeSpec[] Templates, string[] Values, BlockPlacement? Placement, int PreviewVertices)
    {
        internal CadBlockPlacementGraph.Resolved? Graph {get;init;}
        internal Dictionary<int,Resolved> Nested {get;init;}=new();
    }
    internal sealed record Prepared(BlockTableRecord? Definition, Entity[] Members, BlockReference? Insertion, AttributeReference[] Attributes)
    {internal Dictionary<int,Prepared> Nested {get;init;}=new();}
    private static Point3d Point(Point2 point) => new(point.X, point.Y, 0);
    private static bool Primitive(Type type) => type == typeof(Line) || type == typeof(Circle) || type == typeof(Arc) ||
        type == typeof(Ellipse) || type == typeof(Spline) || type == typeof(Polyline) || type == typeof(DBText) || type==typeof(MText) || type==typeof(Hatch) || type==typeof(BlockReference) || type==typeof(MInsertBlock);
    private static void Plane(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1e-9) throw new DomainException("OUT_OF_SCOPE", "Block contents require XY at Z=0 with a positive Z normal.");
    }
    private static Point2 XY(Point3d point)
    {
        Plane(point.Z);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > 1e9 || Math.Abs(point.Y) > 1e9)
            throw new DomainException("INVALID_GEOMETRY", "Block coordinate exceeds finite limits.");
        return new(point.X, point.Y);
    }
    private static ObjectId Style(ObjectId id, Database db, Transaction tr)
    {
        if (tr.GetObject(id, OpenMode.ForRead) is not TextStyleTableRecord style || style.GetType() != typeof(TextStyleTableRecord) ||
            style.OwnerId != db.TextStyleTableId || style.IsDependent || style.IsVertical || style.IsShapeFile || style.TextSize != 0)
            throw new DomainException("TEXT_STYLE_UNAVAILABLE", "Block text requires an independent horizontal non-shape style with no fixed height.");
        return id;
    }
    private static ObjectId Style(string name, Database db, Transaction tr)
    {
        var styles = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (!styles.Has(name)) throw new DomainException("TEXT_STYLE_UNAVAILABLE", "Block text style must already exist.");
        return Style(styles[name], db, tr);
    }
    private static void Plain(DBText text)
    {
        if (text.HasFields || !StructureWriteValidation.IsPlainAttribute(text.TextString))
            throw new DomainException("UNSUPPORTED_BLOCK_CONTENT", "Block text must be bounded plain text without fields or formatting codes.");
    }
    public static Resolved Resolve(StructureEdit operation, Database db, Transaction tr, ObjectId model,
        Func<ObjectId, LayerWriteState> layerState, Action cancel, Action<string>? warning=null,bool memberAddition=false)
    {
        CadBlockSpace.RequireSpace(db, tr, model);
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        var zero = layers["0"]; layerState(zero).RequireAttributeWrite();
        var byLayer = linetypes["ByLayer"];
        if (operation is DefineBlockEdit define)
        {
            if (!memberAddition&&blocks.Has(define.Name)) throw new DomainException("BLOCK_EXISTS", "Block definition exists; redefinition is outside this capability.");
            var nested=new Dictionary<int,Resolved>();
            var entityStyles = new ObjectId[define.Entities.Length]; var attributeStyles = new ObjectId[define.Attributes.Length];
            for (int i = 0; i < define.Entities.Length; i++)
            {
                cancel();
                if(define.Entities[i] is TextSpec text){entityStyles[i]=Style(text.TextStyle,db,tr);Font(tr,entityStyles[i],text.Text,warning);}
                else if(define.Entities[i] is MTextSpec mtext){entityStyles[i]=Style(mtext.TextStyle,db,tr);Font(tr,entityStyles[i],string.Join("\n",mtext.Paragraphs),warning);}
                else if(define.Entities[i] is BlockReferenceSpec391 reference)
                    nested.Add(i,Resolve(reference.Insertion(),db,tr,model,layerState,cancel,warning));
            }
            if(define.Entities.Any(e=>e is HatchSpec or BlockReferenceSpec391))warning?.Invoke(CadBlockTemplate391.Warning);
            for (int i = 0; i < define.Attributes.Length; i++)
            { cancel(); attributeStyles[i] = Style(define.Attributes[i].TextStyle, db, tr); Font(tr,attributeStyles[i],define.Attributes[i].DefaultText,warning); }
            return new(blocks.ObjectId, model, ObjectId.Null, zero, ObjectId.Null, byLayer, define.Name,
                entityStyles, attributeStyles, Array.Empty<ObjectId>(), Array.Empty<ObjectId>(), define.Attributes, Array.Empty<string>(), null,
                define.Entities.Sum(e => e is PolylineSpec p ? p.Vertices.Length : e is SplineSpec s ? s.ControlPoints.Length : 0)) {Nested=nested};
        }
        if (operation is not InsertBlockEdit insert) throw new DomainException("INVALID_ARGUMENT", "Expected a block operation.");
        if (!layers.Has(insert.Layer)) throw new DomainException("LAYER_UNAVAILABLE", "Insertion layer must already exist.");
        ObjectId insertionLayer = layers[insert.Layer]; layerState(insertionLayer).RequireAttributeWrite();
        var definitionId = CadStructureWriter.Find(db, insert.DefinitionHandle);
        if (tr.GetObject(definitionId, OpenMode.ForRead) is not BlockTableRecord definition || definition.GetType() != typeof(BlockTableRecord) ||
            definition.OwnerId != blocks.ObjectId) throw new DomainException("OUT_OF_SCOPE", "Expected a native block definition from this database.");
        if (definition.IsAnonymous || definition.IsDynamicBlock || definition.IsLayout || definition.IsDependent ||
            definition.IsFromExternalReference || definition.IsFromOverlayReference || !BlockWriteValidation.OrdinaryName(definition.Name))
            throw new DomainException("UNSUPPORTED_BLOCK_TYPE", "Only ordinary local static definitions are insertable.");
        if ((int)definition.Units != 4) throw new DomainException("BLOCK_UNITS_MISMATCH", "Definition units must be Millimeters; no implicit conversion.");
        var placement = new BlockPlacement(XY(definition.Origin), insert.Position, insert.Rotation, insert.Scale);
        var primitives = new List<ObjectId>(); var attributeIds = new List<ObjectId>(); var templates = new List<BlockAttributeSpec>();
        int vertices = 0;bool hasClosedSpline=false;
        var sourceBudget=new SerializedCapacity357(cancel:cancel);
        foreach (ObjectId id in definition)
        {
            cancel(); if (id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity || entity.ObjectId!=id || entity.OwnerId != definitionId || entity.HasFields || entity.Database?.UnmanagedObject!=db.UnmanagedObject)
                throw new DomainException("UNSUPPORTED_BLOCK_CONTENT", "Definition members must be native locally owned ordinary geometry without fields.");
            if (entity.GetType() == typeof(AttributeDefinition))
            {
                var attribute = (AttributeDefinition)entity;
                if (attribute.Constant || attribute.Invisible || attribute.IsMTextAttributeDefinition ||
                    attribute.HorizontalMode != TextHorizontalMode.TextLeft || attribute.VerticalMode != TextVerticalMode.TextBase ||
                    attribute.WidthFactor != 1 || attribute.Oblique != 0)
                    throw new DomainException("UNSUPPORTED_BLOCK_CONTENT", "Visible variable left/baseline plain attributes are supported.");
                if(attribute.LayerId!=zero)throw new DomainException("UNSUPPORTED_BLOCK_CONTENT","New root attributes require the existing layer0 attribute template route.");
                CadModifier.CheckGeometry(attribute, cancel); Plain(attribute); Style(attribute.TextStyleId, db, tr);
                templates.Add(new(attribute.Tag, attribute.TextString, XY(attribute.Position), attribute.Height, "", BlockWriteValidation.NormalizeRotation(attribute.Rotation)));
                sourceBudget.Add(templates[^1]);
                attributeIds.Add(id);
            }
            else
            {
                if (!Primitive(entity.GetType())) throw new DomainException("UNSUPPORTED_BLOCK_CONTENT", "Ordinary native curve/Text/MText, independent Hatch and static nested BlockReference/MInsertBlock members only.");
                hasClosedSpline|=entity is Spline { Closed: true };
                if(entity.GetType()==typeof(Hatch))CadBlockTemplate391.RequireHatch((Hatch)entity,cancel);
                else if(entity is BlockReference nestedReference)CadBlockMover.CheckRoot(nestedReference);
                else CadModifier.CheckGeometry(entity, cancel);
                // Retained graphic text keeps its source style/height. The
                // graph validates that local style; creation's fixed-height
                // restriction applies only to newly constructed text/ATTDEF.
                if (entity is DBText text) Plain(text);
                if (entity is Polyline polyline) vertices += polyline.NumberOfVertices;
                if (entity is Spline spline) vertices += spline.NumControlPoints;
                sourceBudget.Add(EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,ObjectId.Null,"00000000-0000-0000-0000-000000000000","00000000-0000-0000-0000-000000000000"),CancellationToken.None));
                if(entity is DBText or MText)FontCoverage.RequireEntity(tr,entity,warning,cancel);
                primitives.Add(id);
            }
        }
        if (hasClosedSpline && attributeIds.Count!=0)throw new DomainException("UNSUPPORTED_BLOCK_CONTENT","Closed spline block members require an attribute-free definition.");
        if (primitives.Count == 0) throw new DomainException("UNSUPPORTED_BLOCK_CONTENT", "An insertable definition requires at least one supported primitive.");
        string[] values = BlockWriteValidation.ResolveAttributeValues(templates, insert.AttributeValues);
        for(int i=0;i<values.Length;i++)Font(tr,((AttributeDefinition)tr.GetObject(attributeIds[i],OpenMode.ForRead)).TextStyleId,values[i],warning);
        using var probe=new BlockReference(Point(insert.Position),definitionId){Normal=Vector3d.ZAxis,Rotation=insert.Rotation,ScaleFactors=new Scale3d(insert.Scale)};
        var graph=CadBlockPlacementGraph.Resolve(probe,db,tr,cancel);
        foreach(var leaf in graph.Leaves)
        {
            cancel();var native=tr.GetObject(leaf.Id,OpenMode.ForRead);
            if(native is DBText or MText)FontCoverage.RequireEntity(tr,native,warning,cancel);
        }
        warning?.Invoke(CadBlockTemplate391.Warning);
        return new(blocks.ObjectId, model, definitionId, zero, insertionLayer, byLayer, definition.Name,
            Array.Empty<ObjectId>(), Array.Empty<ObjectId>(), primitives.ToArray(), attributeIds.ToArray(), templates.ToArray(), values, placement, vertices) {Graph=graph};
    }
    private static void Font(Transaction tr,ObjectId style,string text,Action<string>? warning)
    {
        var record=(TextStyleTableRecord)tr.GetObject(style,OpenMode.ForRead);
        if(!string.IsNullOrEmpty(record.BigFontFileName)||string.IsNullOrWhiteSpace(record.Font.TypeFace))
        {warning?.Invoke("FONT_CHECK_UNAVAILABLE: Native SHX/big-font glyph coverage needs its font adapter; review actual rendered block text.");return;}
        try{FontCoverage.Require(tr,style,text);}
        catch(DomainException error)when(error.Error.code=="FONT_CHECK_UNAVAILABLE")
        {warning?.Invoke("FONT_CHECK_UNAVAILABLE: Requested block text glyph coverage could not be verified; review actual rendered text.");}
    }
    private static void InheritedProperties(Entity entity, ObjectId layer, ObjectId linetype)
    { entity.LayerId = layer; entity.ColorIndex = 256; entity.LinetypeId = linetype; entity.LineWeight = (LineWeight)(-1); }
    private static void Close(double actual, double expected, string label)
    {
        if (!double.IsFinite(actual) || Math.Abs(actual - expected) > 1e-7 + Math.Abs(expected) * 1e-12)
            throw new DomainException("TRANSFORM_MISMATCH", "Native block transform differs from the independent placement: " + label);
    }
    public static Prepared Preview(StructureEdit operation, Resolved resolved, Transaction tr, List<DBObject> owned, Action cancel)
    {
        if (operation is DefineBlockEdit define)
        {
            var definition = new BlockTableRecord(); owned.Add(definition);
            definition.Name = define.Name; definition.Origin = Point(define.BasePoint); definition.Units = UnitsValue.Millimeters;
            definition.BlockScaling = BlockScaling.Any; definition.Explodable = true;
            var members = new List<Entity>();var nested=new Dictionary<int,Prepared>();
            for (int i = 0; i < define.Entities.Length; i++)
            {
                cancel();Entity entity;
                if(define.Entities[i] is BlockReferenceSpec391 nestedSpec)
                {
                    var child=Preview(nestedSpec.Insertion(),resolved.Nested[i],tr,owned,cancel);nested.Add(i,child);entity=child.Insertion!;
                }
                else{entity=CadWriter.Create(define.Entities[i],resolved.EntityStyles[i]);owned.Add(entity);}
                InheritedProperties(entity, resolved.LayerZero, resolved.ByLayerLinetype);
                if(entity.GetType()!=typeof(Hatch))CadBlockTemplate391.VerifyMember(entity,define.Entities[i],resolved.EntityStyles[i],cancel);
                members.Add(entity);
            }
            for (int i = 0; i < define.Attributes.Length; i++)
            {
                cancel(); var template = define.Attributes[i]; var attribute = new AttributeDefinition(); owned.Add(attribute);
                attribute.Tag = template.Tag; attribute.Prompt = template.Tag; attribute.TextString = template.DefaultText;
                attribute.Position = Point(template.Position); attribute.Height = template.Height; attribute.Rotation = template.Rotation;
                attribute.TextStyleId = resolved.AttributeStyles[i]; attribute.Normal = Vector3d.ZAxis; attribute.WidthFactor = 1; attribute.Oblique = 0;
                attribute.HorizontalMode = TextHorizontalMode.TextLeft; attribute.VerticalMode = TextVerticalMode.TextBase;
                attribute.Constant = false; attribute.Invisible = false; attribute.Verifiable = false; attribute.Preset = false; attribute.LockPositionInBlock = true;
                InheritedProperties(attribute, resolved.LayerZero, resolved.ByLayerLinetype);
                CadModifier.CheckGeometry(attribute, cancel); members.Add(attribute);
            }
            return new(definition, members.ToArray(), null, Array.Empty<AttributeReference>()) {Nested=nested};
        }
        var insert = (InsertBlockEdit)operation; var placement = resolved.Placement!;
        var reference = new BlockReference(Point(insert.Position), resolved.Definition); owned.Add(reference);
        reference.Normal = Vector3d.ZAxis; reference.Rotation = insert.Rotation; reference.ScaleFactors = new Scale3d(insert.Scale);
        InheritedProperties(reference, resolved.InsertionLayer, resolved.ByLayerLinetype);
        Matrix3d native = reference.BlockTransform; var expected = placement.Matrix();
        for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) Close(native[row,column], expected[row*4+column], "matrix");
        resolved.Graph?.Verify(tr,cancel);
        var leaves=resolved.Graph?.Leaves??resolved.PrimitiveIds.Select(id=>new CadBlockPlacementGraph.Leaf(id,Matrix3d.Identity)).ToArray();
        foreach(var leaf in leaves)
        {
            cancel();var source=(Entity)tr.GetObject(leaf.Id,OpenMode.ForRead);
            if(source.GetType()==typeof(Hatch)){CadBlockTemplate391.RequireHatch((Hatch)source,cancel);continue;}
            using var clone=(Entity)source.Clone();
            if(!clone.ObjectId.IsNull)throw new DomainException("API_EXCEPTION","Geometry preview clone is not detached.");
            clone.TransformBy(native*leaf.ToRoot);CadModifier.CheckGeometry(clone,cancel);
        }
        resolved.Graph?.Verify(tr,cancel);
        var attributes = new List<AttributeReference>();
        for (int i = 0; i < resolved.AttributeIds.Length; i++)
        {
            cancel(); var template = (AttributeDefinition)tr.GetObject(resolved.AttributeIds[i], OpenMode.ForRead);
            var attribute = new AttributeReference(); owned.Add(attribute);
            attribute.SetAttributeFromBlock(template, native); attribute.TextString = resolved.Values[i];
            CadModifier.CheckGeometry(attribute, cancel); var target = placement.Transform(resolved.Templates[i].Position);
            Close(attribute.Position.X, target.X, "attribute X"); Close(attribute.Position.Y, target.Y, "attribute Y"); Plane(attribute.Position.Z);
            Close(attribute.Height, placement.Size(resolved.Templates[i].Height), "attribute height");
            Close(Math.IEEERemainder(attribute.Rotation - resolved.Templates[i].Rotation - insert.Rotation, Math.PI * 2), 0, "attribute rotation");
            if (attribute.Tag != resolved.Templates[i].Tag || attribute.IsConstant || attribute.IsMTextAttribute || attribute.LayerId != resolved.LayerZero ||
                attribute.Invisible || attribute.TextStyleId != template.TextStyleId || attribute.WidthFactor != 1 || attribute.Oblique != 0 ||
                attribute.HorizontalMode != TextHorizontalMode.TextLeft || attribute.VerticalMode != TextVerticalMode.TextBase)
                throw new DomainException("TRANSFORM_MISMATCH", "Native attribute copy did not preserve the supported definition properties.");
            attributes.Add(attribute);
        }
        return new(null, Array.Empty<Entity>(), reference, attributes.ToArray());
    }
    public static StructureChange Project(StructureWriteArguments args, int index, Resolved resolved)
    {
        EntityRef Dummy() => new(args.BridgeId, args.DocumentId, "FFFFFFFFFFFFFFFF");
        var children = new List<StructureChild>();
        if (args.Operations[index] is DefineBlockEdit define)
        {
            children.AddRange(define.Entities.Select((_, i) => new StructureChild(Dummy(), "entity", i, null)));
            children.AddRange(define.Attributes.Select((a, i) => new StructureChild(Dummy(), "attribute_definition", i, a.Tag)));
            foreach(var pair in resolved.Nested)children.AddRange(pair.Value.Templates.Select(a=>new StructureChild(Dummy(),"nested_attribute",pair.Key,a.Tag)));
        }
        else children.AddRange(resolved.Templates.Select((a, i) => new StructureChild(Dummy(), "attribute", i, a.Tag)));
        return new(index, args.Operations[index].Operation, Dummy(), args.Operations[index] is DefineBlockEdit ? "block_definition" : "block_insert", resolved.Name, true, children.ToArray());
    }
    public static StructureChange Apply(StructureWriteArguments args, int index, Resolved resolved, Prepared prepared, Transaction tr, WriteJob job)
    {
        EntityRef Ref(ObjectId id) => new(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(id.Handle.ToString()));
        var children = new List<StructureChild>(); ObjectId parent;
        if (args.Operations[index] is DefineBlockEdit define)
        {
            var definition = prepared.Definition!;
            parent = ((BlockTable)tr.GetObject(resolved.BlockTable, OpenMode.ForWrite)).Add(definition);
            tr.AddNewlyCreatedDBObject(definition, true);
            for (int i = 0; i < prepared.Members.Length; i++)
            {
                job.CheckCancellation(); ObjectId child = definition.AppendEntity(prepared.Members[i]); tr.AddNewlyCreatedDBObject(prepared.Members[i], true);
                bool attribute = i >= define.Entities.Length; int sourceIndex = attribute ? i - define.Entities.Length : i;
                if(!attribute)
                {
                    if(define.Entities[i] is HatchSpec)AnnotationCreator.Complete(prepared.Members[i],define.Entities[i]);
                    CadBlockTemplate391.VerifyMember(prepared.Members[i],define.Entities[i],resolved.EntityStyles[i],job.CheckCancellation);
                    if(prepared.Nested.TryGetValue(i,out var nested))
                    {
                        var reference=(BlockReference)prepared.Members[i];
                        foreach(var ownedAttribute in nested.Attributes)
                        {
                            job.CheckCancellation();var nestedId=reference.AttributeCollection.AppendAttribute(ownedAttribute);tr.AddNewlyCreatedDBObject(ownedAttribute,true);
                            if(ownedAttribute.ObjectId!=nestedId||ownedAttribute.OwnerId!=child)throw new DomainException("BLOCK_INSERT_MISMATCH","New nested occurrence attribute has a different native identity/owner.");
                            children.Add(new(Ref(nestedId),"nested_attribute",i,ownedAttribute.Tag));
                        }
                        VerifyInsertion(resolved.Nested[i],nested,tr,job.CheckCancellation);
                    }
                }
                children.Add(new(Ref(child), attribute ? "attribute_definition" : "entity", sourceIndex, attribute ? define.Attributes[sourceIndex].Tag : null));
            }
        }
        else
        {
            var reference = prepared.Insertion!;
            var space = (BlockTableRecord)tr.GetObject(resolved.Model, OpenMode.ForWrite);
            var scope = CadBlockSpace.RequireSpace(space.Database, tr, space.ObjectId);
            if (CadBlockSpace.RequireWriteSpace(space.Database, tr, scope.Kind, job.CheckCancellation) != space.ObjectId)
                throw new DomainException("OUT_OF_SCOPE", "Active block insertion space changed before apply.");
            parent = space.AppendEntity(reference);
            tr.AddNewlyCreatedDBObject(reference, true);
            if (reference.ObjectId != parent || reference.OwnerId != resolved.Model || reference.BlockTableRecord != resolved.Definition)
                throw new DomainException("BLOCK_INSERT_MISMATCH", "Native insertion did not preserve the selected space or shared definition.");
            for (int i = 0; i < prepared.Attributes.Length; i++)
            {
                job.CheckCancellation(); ObjectId child = reference.AttributeCollection.AppendAttribute(prepared.Attributes[i]);
                tr.AddNewlyCreatedDBObject(prepared.Attributes[i], true); children.Add(new(Ref(child), "attribute", i, resolved.Templates[i].Tag));
                if (prepared.Attributes[i].ObjectId != child || prepared.Attributes[i].OwnerId != parent)
                    throw new DomainException("BLOCK_INSERT_MISMATCH", "Native insertion attribute identity or owner differs from its occurrence.");
            }
            if (!reference.AttributeCollection.Cast<ObjectId>().SequenceEqual(prepared.Attributes.Select(a => a.ObjectId)))
                throw new DomainException("BLOCK_INSERT_MISMATCH", "Native insertion changed the owned attribute collection.");
        }
        if(args.Operations[index] is DefineBlockEdit)foreach(var pair in resolved.Nested)pair.Value.Graph?.Verify(tr,job.CheckCancellation);
        else VerifyInsertion(resolved,prepared,tr,job.CheckCancellation);
        return new(index, args.Operations[index].Operation, Ref(parent), args.Operations[index] is DefineBlockEdit ? "block_definition" : "block_insert", resolved.Name, true, children.ToArray());
    }
    internal static void VerifyInsertion(Resolved resolved,Prepared prepared,Transaction tr,Action cancel)
    {
        var reference=prepared.Insertion!;var placement=resolved.Placement!;
        if(prepared.Attributes.Length!=resolved.Templates.Length)
            throw new DomainException("BLOCK_INSERT_MISMATCH","Native occurrence does not retain every requested root attribute.");
        CadBlockMover.CheckRoot(reference);
        if(reference.BlockTableRecord!=resolved.Definition)throw new DomainException("BLOCK_INSERT_MISMATCH","Native occurrence changed its shared definition.");
        var actual=reference.BlockTransform;var expected=placement.Matrix();
        for(int row=0;row<4;row++)for(int column=0;column<4;column++)Close(actual[row,column],expected[row*4+column],"retained native matrix");
        if(!reference.AttributeCollection.Cast<ObjectId>().SequenceEqual(prepared.Attributes.Select(a=>a.ObjectId)))
            throw new DomainException("BLOCK_INSERT_MISMATCH","Native occurrence changed the complete owned attribute collection.");
        for(int i=0;i<prepared.Attributes.Length;i++)
        {
            cancel();var attribute=prepared.Attributes[i];var template=(AttributeDefinition)tr.GetObject(resolved.AttributeIds[i],OpenMode.ForRead);
            var target=placement.Transform(resolved.Templates[i].Position);
            Close(attribute.Position.X,target.X,"retained attribute X");Close(attribute.Position.Y,target.Y,"retained attribute Y");Plane(attribute.Position.Z);
            Close(attribute.Height,placement.Size(resolved.Templates[i].Height),"retained attribute height");
            Close(Math.IEEERemainder(attribute.Rotation-resolved.Templates[i].Rotation-placement.Rotation,2*Math.PI),0,"retained attribute rotation");
            if(attribute.OwnerId!=reference.ObjectId||attribute.Tag!=resolved.Templates[i].Tag||attribute.TextString!=resolved.Values[i]||
                attribute.TextStyleId!=template.TextStyleId||attribute.IsConstant||attribute.IsMTextAttribute||attribute.Invisible||attribute.LayerId!=resolved.LayerZero||
                attribute.WidthFactor!=1||attribute.Oblique!=0||attribute.HorizontalMode!=TextHorizontalMode.TextLeft||attribute.VerticalMode!=TextVerticalMode.TextBase)
                throw new DomainException("BLOCK_INSERT_MISMATCH","Native occurrence changed requested root attribute values, ownership, placement or style.");
        }
        resolved.Graph?.Verify(tr,cancel);
    }
    internal static void VerifyCommitted(StructureWriteArguments args,Dictionary<int,Resolved> sources,StructureChange[] changes,Database db,Action cancel)
    {
        using var tr=db.TransactionManager.StartTransaction();
        foreach(var pair in sources)
        {
            cancel();int index=pair.Key;var source=pair.Value;var result=changes.Single(c=>c.operation_index==index);
            var id=CadStructureWriter.Find(db,result.@ref.handle);
            if(args.Operations[index] is DefineBlockEdit define)
            {
                var block=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);
                if(block.GetType()!=typeof(BlockTableRecord)||block.ObjectId!=id||block.OwnerId!=db.BlockTableId||block.Name!=define.Name||
                    block.IsLayout||block.IsAnonymous||block.IsDynamicBlock||block.IsDependent||block.IsFromExternalReference||block.IsFromOverlayReference||block.Units!=UnitsValue.Millimeters||
                    !block.Origin.IsEqualTo(Point(define.BasePoint)))throw new DomainException("BLOCK_INSERT_MISMATCH","Created definition does not retain its requested native identity/header.");
                var members=result.children!.Where(c=>c.kind is "entity" or "attribute_definition").Select(c=>CadStructureWriter.Find(db,c.@ref.handle)).ToArray();
                if(!block.Cast<ObjectId>().SequenceEqual(members)||members.Length!=define.Entities.Length+define.Attributes.Length)
                    throw new DomainException("BLOCK_INSERT_MISMATCH","Created definition does not retain the complete requested member collection.");
                for(int i=0;i<define.Entities.Length;i++)
                {
                    cancel();var member=(Entity)tr.GetObject(members[i],OpenMode.ForRead);
                    if(member.ObjectId!=members[i]||member.OwnerId!=id||member.LayerId!=source.LayerZero)
                        throw new DomainException("BLOCK_INSERT_MISMATCH","Created definition member has a different native identity/owner/layer.");
                    CadBlockTemplate391.VerifyMember(member,define.Entities[i],source.EntityStyles[i],cancel);
                    if(define.Entities[i] is BlockReferenceSpec391)
                    {
                        var nested=(BlockReference)member;var attrs=nested.AttributeCollection.Cast<ObjectId>().Select(a=>(AttributeReference)tr.GetObject(a,OpenMode.ForRead)).ToArray();
                        VerifyInsertion(source.Nested[i],new(null,Array.Empty<Entity>(),nested,attrs),tr,cancel);
                        var children=result.children!.Where(c=>c.kind=="nested_attribute"&&c.source_index==i).Select(c=>CadStructureWriter.Find(db,c.@ref.handle));
                        if(!children.SequenceEqual(attrs.Select(a=>a.ObjectId)))throw new DomainException("BLOCK_INSERT_MISMATCH","Nested attribute receipt differs from actual native ownership.");
                    }
                }
            }
            else
            {
                var reference=(BlockReference)tr.GetObject(id,OpenMode.ForRead);
                if(reference.ObjectId!=id||reference.OwnerId!=source.Model)throw new DomainException("BLOCK_INSERT_MISMATCH","Created insertion changed its native identity/space.");
                var attributes=reference.AttributeCollection.Cast<ObjectId>().Select(a=>(AttributeReference)tr.GetObject(a,OpenMode.ForRead)).ToArray();
                VerifyInsertion(source,new(null,Array.Empty<Entity>(),reference,attributes),tr,cancel);
                if(!result.children!.Select(c=>CadStructureWriter.Find(db,c.@ref.handle)).SequenceEqual(attributes.Select(a=>a.ObjectId)))
                    throw new DomainException("BLOCK_INSERT_MISMATCH","Insertion attribute receipt differs from its actual native collection.");
            }
        }
    }
}
