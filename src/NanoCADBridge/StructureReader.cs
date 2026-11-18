using System.Globalization;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal sealed class StructureReader
{
    private readonly string _instance;
    private readonly SnapshotPages<string> _pages = new(handle => handle);
    public StructureReader(string instance) => _instance = instance;
    private EntityRef Ref(ObjectId id, string doc) => new(_instance, doc, Validation.NormalizeHandle(id.Handle.ToString()));
    private static string Text(string text) => text.Length <= 16384 ? text : throw new DomainException("ENTITY_TOO_LARGE", "Structural text exceeds 16384 characters.");
    private static double Number(double value) => double.IsFinite(value) ? value : throw new DomainException("OBJECT_READ_FAILED", "Non-finite structural geometry.");
    private static double[] Point(Point3d p) => new[] { Number(p.X), Number(p.Y), Number(p.Z) };
    private static double[] Vector(Vector3d v) => new[] { Number(v.X), Number(v.Y), Number(v.Z) };
    private static double[] Matrix(Matrix3d m)
    {
        var values = new double[16];
        for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) values[row * 4 + column] = Number(m[row, column]);
        return values;
    }
    private static object Color(Teigha.Colors.Color color) => new
    {
        method = color.IsByLayer ? "by_layer" : color.IsByBlock ? "by_block" : color.IsByAci ? "aci" : color.IsByColor ? "true_color" : color.ColorMethod.ToString(),
        aci = color.IsByAci ? (int?)color.ColorIndex : null,
        rgb = color.IsByColor ? new[] { (int)color.Red, color.Green, color.Blue } : null
    };
    private BlockInfo BasicBlock(BlockTableRecord block, string doc) => new(Ref(block.ObjectId, doc), Text(block.Name), block.IsLayout, block.IsAnonymous, block.IsDynamicBlock, block.IsFromExternalReference, block.IsFromOverlayReference, block.IsDependent, Point(block.Origin), block.Units.ToString(), block.IsFromExternalReference || block.IsFromOverlayReference ? Text(block.PathName) : null);
    private object Block(BlockTableRecord block, string doc, Database db, Transaction tr, Action cancel)
    {
        var ordinary = BasicBlock(block, doc);
        if (!block.IsFromExternalReference && !block.IsFromOverlayReference) return ordinary;
        var observed = Wire.Element(ordinary).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal);
        observed.Add("external_reference", CadExternalReferences385.ReadDefinition(block, "xref", Ref(block.ObjectId, doc), db, tr, cancel)); return observed;
    }

    public StructurePage Read(Arguments args, DocumentState state, Transaction tr, CancellationToken cancellation)
    {
        var section = args.Section!;
        var db = state.Document.Database;
        void Check() => cancellation.ThrowIfCancellationRequested();
        var ownerId = section switch
        {
            "groups" => db.GroupDictionaryId,
            "table_styles" => db.TableStyleDictionaryId,
            "mleader_styles" => db.MLeaderStyleDictionaryId,
            "layouts" or "viewports" or "viewport_clipping" => db.LayoutDictionaryId,
            "layers" => db.LayerTableId,
            "linetypes" => db.LinetypeTableId,
            "text_styles" => db.TextStyleTableId,
            "dimension_styles" => db.DimStyleTableId,
            "blocks" => db.BlockTableId,
            _ => throw new DomainException("INVALID_ARGUMENT", "Unknown structure section.")
        };
        var owner = tr.GetObject(ownerId, OpenMode.ForRead);
        var dictionary = owner as DBDictionary;
        bool viewports = section is "viewports" or "viewport_clipping";
        IEnumerable<string> Identities()
        {
            if (dictionary == null)
            {
                foreach (ObjectId id in (SymbolTable)owner)
                {
                    Check();
                    if (!id.IsErased) yield return id.Handle.ToString();
                }
                yield break;
            }
            foreach (DBDictionaryEntry entry in dictionary)
            {
                Check();
                if (entry.Value.IsErased) continue;
                if (viewports)
                {
                    var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (section == "viewport_clipping" && layout.ModelType) continue;
                    var space = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        Check();
                        if (!id.IsErased && id.ObjectClass.IsDerivedFrom(Teigha.Runtime.RXObject.GetClass(typeof(Viewport))))
                            yield return id.Handle.ToString();
                    }
                }
                else if (section != "groups" || tr.GetObject(entry.Value, OpenMode.ForRead) is Group)
                    yield return entry.Value.Handle.ToString();
            }
        }
        StructureRecord ReadRecord(string handle)
        {
            Check();
            var nativeHandle = new Handle(unchecked((long)ulong.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            if (!db.TryGetObjectId(nativeHandle, out var id) || id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased)
                throw new DomainException("OBJECT_NOT_FOUND", "Structural record is unavailable.");
            var obj = tr.GetObject(id, OpenMode.ForRead);
            StructureRecord Row(string name, object properties) => new(Ref(id, state.Id), section, Text(name), properties);
            EntityRef Reference(ObjectId item) => Ref(item, state.Id);
            var mismatch = new DomainException("OUT_OF_SCOPE", "The requested record does not belong to this structure section.");
            if (viewports)
            {
                if (obj is not Viewport viewport || tr.GetObject(obj.OwnerId, OpenMode.ForRead) is not BlockTableRecord { IsLayout: true } space ||
                    tr.GetObject(space.LayoutId, OpenMode.ForRead) is not Layout layout || layout.OwnerId != ownerId ||
                    layout.BlockTableRecordId != space.ObjectId || section == "viewport_clipping" && layout.ModelType) throw mismatch;
                return Row(layout.LayoutName + ":" + viewport.Number.ToString(CultureInfo.InvariantCulture),
                    section == "viewport_clipping" ? CadViewportClipping358.Read(viewport, layout, Reference(id), db, tr, Check) :
                    LayoutMetadataReader.ReadViewport(viewport, layout, tr, db, Reference, Check, Check));
            }
            if (obj.OwnerId != ownerId) throw mismatch;
            if (section == "layouts" && obj is Layout sheet)
                return Row(sheet.LayoutName, LayoutMetadataReader.ReadLayout(sheet, tr, db, Reference, Check, Check));
            if (dictionary != null)
            {
                string name = dictionary.NameAt(id);
                if (section == "groups") return obj is Group group ? Row(name, CadDrawingGroups431.Read(group, name, db, tr, Check)) : throw mismatch;
                if (section is not ("table_styles" or "mleader_styles")) throw mismatch;
                bool local = !name.Contains('|');
                bool native = section == "table_styles" ? obj.GetType() == typeof(TableStyle) : obj.GetType() == typeof(MLeaderStyle);
                if (native && obj is MLeaderStyle leader) return Row(name, CadMLeaderStyleWriter337.ReadProperties(leader,name,db,tr,Check));
                if (native && local && obj is TableStyle table)
                {
                    var observed = CadTableStyleWriter337.TryReadProperties(table,db,tr,Check);
                    return observed.Value is { } details
                        ? Row(name, new { native_type = obj.GetType().Name, is_local = local, can_reference = true, is_current = id == db.Tablestyle, table_style = details })
                        : Row(name, new { native_type = obj.GetType().Name, is_local = local, can_reference = true, is_current = id == db.Tablestyle, table_style_unavailable_reason = observed.Reason });
                }
                return Row(name, new { native_type = obj.GetType().Name, is_local = local, can_reference = native && local });
            }
            if (obj is LayerTableRecord layer)
            {
                using var color = layer.Color;
                return Row(layer.Name, new { is_off = layer.IsOff, is_frozen = layer.IsFrozen, is_locked = layer.IsLocked,
                    is_dependent = layer.IsDependent, is_current = id == db.Clayer, is_plottable = layer.IsPlottable,
                    color = Color(color), linetype = Reference(layer.LinetypeObjectId), lineweight = (int)layer.LineWeight,
                    transparency = CadAppearanceProperties.ReadTransparency(layer.Transparency), description = CadAppearanceProperties.ReadDescription(layer) });
            }
            return obj switch
            {
                LinetypeTableRecord line => Row(line.Name, LinetypeMetadataReader.Read(line,tr,db,Reference,Check,Check)),
                TextStyleTableRecord text => Row(text.Name, CurrentStyleProperties(TextStyleMetadataReader.Read(text,Check),id==db.Textstyle)),
                DimStyleTableRecord dim => Row(dim.Name, CurrentStyleProperties(DimensionStyleMetadataReader.Read(dim,tr,db,Reference,Check,Check),id==db.Dimstyle)),
                BlockTableRecord block => Row(block.Name, Block(block,state.Id,db,tr,Check)),
                _ => throw mismatch
            };
        }
        if (args.StructureRecordHandle != null)
            return new(section, [ReadRecord(args.StructureRecordHandle)], 1, 1, true, null);
        var key = args.FilterKey("get_structure");
        var page = args.Cursor == null
            ? _pages.First(state.Id,state.Revision,null,key,Identities().ToArray(),args.PageSize,ReadRecord)
            : _pages.Next(args.Cursor,state.Id,state.Revision,null,key,args.PageSize,ReadRecord);
        return new(section,page.items,page.total_count,page.returned_count,page.complete,page.next_cursor);
    }

    private static JsonElement CurrentStyleProperties(object properties, bool current)
    {
        var value = System.Text.Json.Nodes.JsonNode.Parse(Wire.Element(properties).GetRawText())!;
        value["is_current"] = current;
        return Wire.Element(value);
    }
    public BlockInspection Inspect(Arguments args, DocumentState state, Transaction tr, ObjectId model, CancellationToken cancellation)
    {
        var db = state.Document.Database;
        ulong bits = ulong.Parse(args.BlockHandle!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (!db.TryGetObjectId(new Handle(unchecked((long)bits)), out var rootId) || rootId.IsNull || !rootId.IsValid) throw new DomainException("OBJECT_NOT_FOUND", "Block handle not found.");
        if (rootId.IsErased || rootId.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "Block insertion was erased.");
        var rootObject = tr.GetObject(rootId, OpenMode.ForRead); var root = rootObject as BlockReference; var directDefinition = rootObject as BlockTableRecord;
        CadBlockSpace.Scope? rootScope = null; ObjectId definitionId;
        if (directDefinition != null)
        {
            if (directDefinition.GetType() != typeof(BlockTableRecord) || directDefinition.ObjectId != rootId || directDefinition.OwnerId != db.BlockTableId ||
                directDefinition.Database?.UnmanagedObject != db.UnmanagedObject || directDefinition.IsLayout)
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Expected a current native non-layout block definition.");
            definitionId = rootId;
        }
        else
        {
            if (root == null || root.GetType() != typeof(BlockReference)) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Expected a native BlockReference or block definition; MInsert/custom references are not expanded.");
            rootScope = CadBlockSpace.RequireTopLevel(root, db, tr); definitionId = root.BlockTableRecord;
        }
        var nodes = new List<BlockNode>(); var warnings = new HashSet<string>(StringComparer.Ordinal);
        var stack = new HashSet<ObjectId>(); int bytes = 2048;
        void Check()
        {
            cancellation.ThrowIfCancellationRequested();
        }
        AttributeInfo Attribute(DBText attribute, string tag, bool invisible, bool constant, Matrix3d parent, bool topLevel) =>
            new(Ref(attribute.ObjectId, state.Id), Text(tag), Text(attribute.TextString), Point(attribute.Position), Number(attribute.Height),
                Number(attribute.Rotation), invisible, constant, topLevel ? rootScope!.Kind == "paper" ? "paper" : "WCS" : "parent_definition", Matrix(parent),
                new(Text(attribute.Layer), attribute.LayerId.Handle.ToString()), EntityAdapter.StoredProperties(attribute),
                new(Ref(attribute.TextStyleId, state.Id), Text(attribute.TextStyleName), Number(attribute.WidthFactor), Number(attribute.Oblique),
                    attribute.HorizontalMode.ToString(), attribute.VerticalMode.ToString(), Point(attribute.AlignmentPoint)),
                NativeAttributeMText377.Read(attribute, id => Ref(id, state.Id), Check));
        void Visit(Entity entity, string[] ancestors, Matrix3d parent, int depth)
        {
            Check();
            if (nodes.Count >= args.MaxNodes) throw new DomainException("QUERY_LIMIT_EXCEEDED", "Block occurrence count exceeds max_nodes; request a smaller max_depth.");
            string handle = Ref(entity.ObjectId, state.Id).handle;
            var path = ancestors.Append(handle).ToArray(); bool topLevel = ancestors.Length == 0;
            var summary = EntityAdapter.Summary(entity, model, _instance, state.Id) with { space = topLevel ? rootScope!.Kind : "block_definition" };
            EntityDetail? detail = null; InsertInfo? insert = null; AttributeInfo? definition = null;
            BlockTableRecord? children = null; Matrix3d world = parent;
            if (entity.GetType() == typeof(BlockReference))
            {
                var reference = (BlockReference)entity;
                var block = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                if (block.GetType() != typeof(BlockTableRecord) || block.OwnerId != db.BlockTableId || block.IsLayout)
                    throw new DomainException("OBJECT_READ_FAILED", "Block reference definition has an unexpected native type or owner.");
                var info = BasicBlock(block, state.Id); world = parent * reference.BlockTransform;
                bool dynamic = reference.IsDynamicBlock || info.is_dynamic;
                string expansion = info.is_xref || info.is_overlay || info.is_dependent ? "external_reference" : dynamic ? "dynamic_block" :
                    stack.Contains(block.ObjectId) ? "cycle" : depth >= args.MaxDepth ? "depth_limit" : "expanded";
                if (expansion != "expanded") warnings.Add(expansion);
                else children = block;
                var attributes = new List<AttributeInfo>(); int attributeBytes = 0;
                foreach (ObjectId attributeId in reference.AttributeCollection)
                {
                    Check();
                    if (attributeId.IsErased || attributeId.IsEffectivelyErased || tr.GetObject(attributeId, OpenMode.ForRead) is not AttributeReference attribute || attribute.GetType() != typeof(AttributeReference) || attribute.OwnerId != reference.ObjectId)
                        throw new DomainException("OBJECT_READ_FAILED", "Unexpected, erased or foreign attribute collection member.");
                    var attributeInfo = Attribute(attribute, attribute.Tag, attribute.Invisible, attribute.IsConstant, parent, topLevel);
                    attributeBytes += JsonSerializer.SerializeToUtf8Bytes(attributeInfo, Wire.Json).Length;
                    if (bytes + attributeBytes > Wire.PayloadBudget - 4096) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Block attribute data exceeds the actual response-byte budget; no truncated values returned.");
                    attributes.Add(attributeInfo);
                }
                insert = new(info, Point(reference.Position), Vector(reference.Normal), new[] { Number(reference.ScaleFactors.X), Number(reference.ScaleFactors.Y), Number(reference.ScaleFactors.Z) },
                    Number(reference.Rotation), Matrix(reference.BlockTransform), Matrix(world), attributes.ToArray(), dynamic, expansion, EntityAdapter.StoredProperties(reference));
                summary = summary with { type = "BlockReference", detail_support = "supported" };
            }
            else if (entity.GetType() == typeof(AttributeDefinition))
            {
                var attribute = (AttributeDefinition)entity;
                definition = Attribute(attribute, attribute.Tag, attribute.Invisible, attribute.Constant, parent, topLevel);
                summary = summary with { type = "AttributeDefinition", detail_support = "supported" };
            }
            else if (entity.GetType() == typeof(Line) || entity.GetType() == typeof(Circle) || entity.GetType() == typeof(Arc) ||
                     entity.GetType() == typeof(Ellipse) || entity.GetType() == typeof(Spline) ||
                     entity.GetType() == typeof(Polyline) || entity.GetType() == typeof(DBText) || entity.GetType() == typeof(MText) || entity.GetType() == typeof(Hatch))
            {
                summary = summary with { detail_support = "supported" };
                detail = EntityAdapter.Detail(entity, summary, cancellation, includeNativeCurve: true) with { coordinate_system = "parent_definition" };
            }
            else { warnings.Add("unsupported_entity_type"); summary = summary with { type = "Unknown", detail_support = "unsupported_type" }; }
            var node = new BlockNode(path, summary, Matrix(parent), detail, insert, definition);
            bytes += JsonSerializer.SerializeToUtf8Bytes(node, Wire.Json).Length;
            if (bytes > Wire.PayloadBudget - 4096) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Block hierarchy exceeds response budget; request a smaller max_depth.");
            nodes.Add(node);
            if (children != null)
            {
                stack.Add(children.ObjectId);
                try
                {
                    foreach (ObjectId id in children)
                    {
                        Check(); if (id.IsErased || id.IsEffectivelyErased) continue;
                        if (tr.GetObject(id, OpenMode.ForRead) is not Entity child || child.OwnerId != children.ObjectId) throw new DomainException("OBJECT_READ_FAILED", "Block member is not an owned entity.");
                        Visit(child, path, world, depth + 1);
                    }
                }
                finally { stack.Remove(children.ObjectId); }
            }
        }
        if (args.OccurrencePath is { } occurrence)
        {
            Entity? selected = root;
            Matrix3d frame = Matrix3d.Identity;
            for (int i = 1; i < occurrence.Length; i++)
            {
                Check();
                BlockTableRecord parent;
                if (i == 1 && directDefinition != null) parent = directDefinition;
                else
                {
                    if (selected is not BlockReference reference || reference.GetType() != typeof(BlockReference) || reference.IsDynamicBlock)
                        throw new DomainException("OUT_OF_SCOPE", "The occurrence path does not traverse an ordinary native insertion.");
                    parent = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                    frame *= reference.BlockTransform;
                }
                if (parent.IsLayout || parent.IsFromExternalReference || parent.IsFromOverlayReference || parent.IsDependent ||
                    parent.IsDynamicBlock || parent.OwnerId != db.BlockTableId || !stack.Add(parent.ObjectId))
                    throw new DomainException("OUT_OF_SCOPE", "The occurrence path enters an external, dynamic or cyclic definition.");
                var handle = new Handle(unchecked((long)ulong.Parse(occurrence[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                if (!db.TryGetObjectId(handle, out var childId) || childId.IsNull || !childId.IsValid || childId.IsErased || childId.IsEffectivelyErased ||
                    tr.GetObject(childId, OpenMode.ForRead) is not Entity child || child.OwnerId != parent.ObjectId)
                    throw new DomainException("OBJECT_NOT_FOUND", "The occurrence path no longer belongs to this parent definition.");
                selected = child;
            }
            if (selected == null) throw new DomainException("INVALID_ARGUMENT", "For a definition select a member occurrence_path or omit the path to inspect the definition.");
            Visit(selected, occurrence[..^1], frame, 0);
            return new BlockInspection(Ref(rootId, state.Id), nodes.ToArray(), warnings.Count == 0, warnings.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }
        if (directDefinition != null)
        {
            stack.Add(rootId);
            foreach (ObjectId id in directDefinition)
            {
                Check(); if (id.IsErased || id.IsEffectivelyErased) continue;
                if (!id.IsValid || tr.GetObject(id, OpenMode.ForRead) is not Entity child || child.ObjectId != id || child.OwnerId != rootId || child.Database?.UnmanagedObject != db.UnmanagedObject)
                    throw new DomainException("OBJECT_READ_FAILED", "Definition member has an unavailable/foreign native owner.");
                Visit(child, new[] { Ref(rootId, state.Id).handle }, Matrix3d.Identity, 0);
            }
            stack.Remove(rootId);
        }
        else Visit(root!, Array.Empty<string>(), Matrix3d.Identity, 0);
        var editing = CadBlockDefinitionGeometry388.TryRead(definitionId, _instance, state.Id, db, tr, Check);
        var inspection = new BlockInspection(Ref(rootId, state.Id), nodes.ToArray(), warnings.Count == 0, warnings.OrderBy(s => s, StringComparer.Ordinal).ToArray(), editing, CadBlockDefinitionText391.TryRead(definitionId, _instance, state.Id, db, tr, Check), CadBlockDefinitionMembers392.TryRead(definitionId, _instance, state.Id, db, tr, Check), CadNestedAttributeEditor393.TryRead(definitionId, _instance, state.Id, db, tr, Check));
        return CadDynamicBlockObservation.Block(inspection, root, tr, _instance, state.Id, Check);
    }
}
