using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class EntityAdapter
{
    private static object SpatialGeometry441(Polyline3d polyline,Action cancel)
    {
        var observed=CadSpatialCurveCreator441.ReadPolyline3d(polyline,cancel);
        return new{kind="polyline3d",observed.polyline_type,observed.closed,observed.planar,observed.vertex_count,observed.vertices};
    }
    public static EntitySummary Summary(Entity entity, ObjectId modelId, string instanceId, string documentId,bool paper=false)
    {
        string type = entity switch
        {
            MInsertBlock when entity.GetType()==typeof(MInsertBlock) => "MInsertBlock",
            Wipeout when entity.GetType()==typeof(Wipeout)=>"Wipeout",
            RasterImage when entity.GetType()==typeof(RasterImage)=>"RasterImage",
            DwfReference when entity.GetType()==typeof(DwfReference)=>"DwfUnderlay",
            DgnReference when entity.GetType()==typeof(DgnReference)=>"DgnUnderlay",
            PdfReference when entity.GetType()==typeof(PdfReference)=>"PdfUnderlay",
            BlockReference when entity.GetType()==typeof(BlockReference) => "BlockReference",
            FeatureControlFrame when entity.GetType()==typeof(FeatureControlFrame)=>"Tolerance",
            Helix => "Helix", Region => "Region",
            Ellipse when entity.GetType()==typeof(Ellipse) => "Ellipse",
            Spline when entity.GetType()==typeof(Spline) => "Spline",
            Polyline2d when entity.GetType()==typeof(Polyline2d) => "Polyline2d",
            Polyline3d when entity.GetType()==typeof(Polyline3d) => "Polyline3d",
            DBPoint when entity.GetType()==typeof(DBPoint) => "Point", Ray when entity.GetType()==typeof(Ray) => "Ray", Xline when entity.GetType()==typeof(Xline) => "Xline",
            Line => "Line", Circle => "Circle", Arc => "Arc", Polyline => "Polyline", DBText => "Text", MText => "MText", Dimension => "Dimension", MLeader => "MLeader", Hatch => "Hatch", Table => "Table", Solid3d => "Solid3d", _ => "Unknown"
        };
        if(type=="Unknown" && entity.OwnerId==modelId && PointCloudDiscovery.IsInsertion(entity.ObjectId)) type="PointCloud";
        bool model = entity.OwnerId == modelId;
        return new(new(instanceId, documentId, Validation.NormalizeHandle(entity.Handle.ToString())), type,
            entity.GetType().FullName ?? entity.GetType().Name, new(entity.Layer, entity.LayerId.Handle.ToString()),
            model ? "model" : paper ? "paper" : "non_model", !model&&!paper ? "out_of_scope" : type=="PointCloud" ? "point_cloud_tools" : type == "Unknown" || entity is Polyline3d spatial&&spatial.PolyType!=Poly3dType.SimplePoly || entity is Dimension && !CadNativeDimensionBinding357.Supported(entity) ? "unsupported_type" : "supported");
    }
    public static EntityDetail Detail(Entity entity, EntitySummary summary, CancellationToken cancellationToken,bool includeNativeCurve=false,bool includeAppearance=true)
    {
        string? mtextContents = entity is MText richText ? Text(richText.Contents) : null;
        string? plainText = mtextContents == null ? null : MTextContent.PlainText(mtextContents);
        object geometry = entity switch
        {
            MInsertBlock array when entity.GetType()==typeof(MInsertBlock) => CadBlockArray384.Read(array,summary.@ref,
                array.Database.TransactionManager.TopTransaction ?? throw new DomainException("OBJECT_READ_FAILED","Native array detail requires the current read transaction."),cancellationToken.ThrowIfCancellationRequested),
            Wipeout mask when entity.GetType()==typeof(Wipeout)=>CadWipeout431.Read(mask,cancellationToken.ThrowIfCancellationRequested),
            BlockReference reference when entity.GetType()==typeof(BlockReference) && IsXref(reference)=>ReferenceGeometry(reference,summary.@ref,cancellationToken),
            RasterImage image when entity.GetType()==typeof(RasterImage)=>ReferenceGeometry(image,summary.@ref,cancellationToken),
            UnderlayReference underlay when entity.GetType()==typeof(DwfReference)||entity.GetType()==typeof(DgnReference)||entity.GetType()==typeof(PdfReference)=>ReferenceGeometry(underlay,summary.@ref,cancellationToken),
            BlockReference reference when entity.GetType()==typeof(BlockReference) => BlockReferenceGeometry(reference,summary.@ref,cancellationToken),
            Solid3d solid => SolidGeometryReader.Read(solid, cancellationToken),
            Dimension dimension => DimensionGeometryReader.Read(dimension, summary, cancellationToken),
            FeatureControlFrame frame when entity.GetType()==typeof(FeatureControlFrame)=>CadToleranceFrame356.Read(frame,summary.@ref,frame.Database.TransactionManager.TopTransaction??throw new DomainException("OBJECT_READ_FAILED","Native frame detail requires the current read transaction."),cancellationToken.ThrowIfCancellationRequested),
            MLeader leader => AnnotationReader.Leader(leader, summary, cancellationToken),
            Hatch hatch => AnnotationReader.HatchGeometry(hatch, cancellationToken),
            Table table => AnnotationReader.TableGeometry(table, cancellationToken),
            Line line => new { start = Point(line.StartPoint), end = Point(line.EndPoint), normal = Vector(line.Normal), thickness = Number(line.Thickness) },
            Circle circle => new { center = Point(circle.Center), radius = Number(circle.Radius), normal = Vector(circle.Normal), thickness = Number(circle.Thickness) },
            Ellipse ellipse => CadEllipseReader.Read(ellipse,cancellationToken.ThrowIfCancellationRequested),
            Helix helix => new {axis_origin=Point(helix.GetAxisPoint()),axis_direction=Vector(helix.AxisVector),start_point=Point(helix.StartPoint),base_radius=Number(helix.BaseRadius),top_radius=Number(helix.TopRadius),height=Number(helix.Height),turns=Number(helix.Turns),right_handed=helix.Twist},
            Region region => CadRegionBooleanEditor.Read(region,cancellationToken.ThrowIfCancellationRequested),
            Spline spline => CadSplineReader.ReadReported(spline,cancellationToken.ThrowIfCancellationRequested),
            Arc arc => new { center = Point(arc.Center), radius = Number(arc.Radius), normal = Vector(arc.Normal),
                start_point = Point(arc.StartPoint), end_point = Point(arc.EndPoint), start_angle_ocs = Number(arc.StartAngle),
                end_angle_ocs = Number(arc.EndAngle), sweep_angle = Number(arc.TotalAngle), thickness = Number(arc.Thickness) },
            Polyline polyline => PolylineGeometry(polyline, cancellationToken),
            Polyline2d polyline => CadImportedGeometry407.PolylineGeometry(polyline, cancellationToken.ThrowIfCancellationRequested),
            Polyline3d polyline => SpatialGeometry441(polyline,cancellationToken.ThrowIfCancellationRequested),
            DBPoint point => CadPointEditor408.Geometry(point), Ray ray => CadImportedGeometry407.RayGeometry(ray), Xline line => CadImportedGeometry407.XlineGeometry(line),
            DBText text => new { text = Text(text.TextString), position = Point(text.Position), alignment_point = Point(text.AlignmentPoint),
                height = Number(text.Height), width_factor = Number(text.WidthFactor), rotation = Number(text.Rotation),
                normal = Vector(text.Normal), horizontal_mode = text.HorizontalMode.ToString(), vertical_mode = text.VerticalMode.ToString(),
                text_style = Text(text.TextStyleName), formatting=TextFormatReader.Read(text,summary.@ref,cancellationToken) },
            MText mtext => new { contents = mtextContents!, plain_text = plainText, location = Point(mtext.Location),
                normal = Vector(mtext.Normal), direction = Vector(mtext.Direction), text_height = Number(mtext.TextHeight), width = Number(mtext.Width),
                attachment = mtext.Attachment.ToString(), text_style = Text(mtext.TextStyleName), formatting=MTextFormatReader.Read(mtext,summary.@ref,cancellationToken) },
            _ => throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Unsupported entity geometry.")
        };
        if(entity is BlockReference reference431&&!IsXref(reference431))
        {
            var tr431=entity.Database.TransactionManager.TopTransaction??throw new DomainException("OBJECT_READ_FAILED","Native array observation requires the active read transaction.");
            var array431=CadAssociativeArray431.Read(entity,tr431,cancellationToken.ThrowIfCancellationRequested);
            if(array431!=null)
            {
                var observed431=Wire.Element(geometry).EnumerateObject().ToDictionary(p=>p.Name,p=>(object?)p.Value.Clone(),StringComparer.Ordinal);
                observed431.Add("associative_array",array431);geometry=observed431;
            }
        }
        if(includeNativeCurve && entity is Curve curve && CadRetainedSolidPath392.Supported(curve))
        {
            // Keep the ordinary geometry shape compatible. Native parameters
            // and the complete-shape token are additive read observations.
            var observed=Wire.Element(geometry).EnumerateObject().ToDictionary(
                property=>property.Name,property=>(object?)property.Value.Clone(),StringComparer.Ordinal);
            observed.Add("native_curve",curve.GetType()==typeof(Helix)
                ?CadRetainedSolidPath392.ReadHelix((Helix)curve,cancellationToken.ThrowIfCancellationRequested)
                :CadNativeCurveInspection342.ReadProperties(curve,cancellationToken.ThrowIfCancellationRequested,geometry as NativeSplineGeometry342));
            if(CadNativeStretch345.Supported(curve.GetType()))
            {
                var stretch=CadNativeStretch345.TryReadProperties(curve,cancellationToken.ThrowIfCancellationRequested);
                observed.Add("native_stretch",stretch.Value);
                observed.Add("native_stretch_unavailable_reason",stretch.Reason);
            }
            NativeCurveRead342.CheckSerializedBudget(observed,NativeCurveRead342.PublicGeometryByteBudget,cancellationToken.ThrowIfCancellationRequested);
            geometry=observed;
        }
        return new(summary.@ref, summary.type, summary.runtime_type, summary.layer, summary.space, summary.detail_support,
            "WCS", geometry, includeAppearance ? StoredProperties(entity) : new Dictionary<string,object>(), mtextContents != null && plainText == null
                ? new[] { "MTEXT_PLAIN_TEXT_UNAVAILABLE: unsupported formatting; use contents." } : Array.Empty<string>());
    }
    static bool IsXref(BlockReference reference)
    {
        var tx=reference.Database.TransactionManager.TopTransaction??throw new DomainException("OBJECT_READ_FAILED","Reference read requires the current transaction.");
        return tx.GetObject(reference.BlockTableRecord,OpenMode.ForRead) is BlockTableRecord block&&(block.IsFromExternalReference||block.IsFromOverlayReference);
    }
    static object ReferenceGeometry(Entity entity,EntityRef context,CancellationToken cancellation)=>CadExternalReferences385.Read(entity,context,entity.Database.TransactionManager.TopTransaction??throw new DomainException("OBJECT_READ_FAILED","Reference read requires the current transaction."),cancellation.ThrowIfCancellationRequested);
    private static object BlockReferenceGeometry(BlockReference reference, EntityRef context, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        EntityRef Identity(ObjectId id)
        {
            if (id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased)
                throw new DomainException("OBJECT_READ_FAILED", "Block reference owner or definition identity is unavailable.");
            return new(context.bridge_instance_id,context.document_id,Validation.NormalizeHandle(id.Handle.ToString()));
        }
        var scale = reference.ScaleFactors;
        // Read only the selected resident insertion. Its definition identity is a
        // reference, not evidence of inspected members, attributes or dynamic geometry.
        var result = new {
            inspection_scope = "reference_only",
            owner_ref = Identity(reference.OwnerId), definition_ref = Identity(reference.BlockTableRecord),
            position = Point(reference.Position), rotation = Number(reference.Rotation),
            scale_factors = new[] { Number(scale.X), Number(scale.Y), Number(scale.Z) },
            normal = Vector(reference.Normal), is_dynamic = reference.IsDynamicBlock
        };
        cancellation.ThrowIfCancellationRequested(); return result;
    }
    internal static object StoredProperties(Entity entity)
    {
        using var color = entity.Color;
        int weight = (int)entity.LineWeight;
        return new
        {
            visible = entity.Visible,
            transparency = CadAppearanceProperties.ReadTransparency(entity.Transparency),
            color = new
            {
                method = color.IsByLayer ? "by_layer" : color.IsByBlock ? "by_block" : color.IsByAci ? "aci" : color.IsByColor ? "true_color" : color.ColorMethod.ToString(),
                aci = color.IsByAci ? (int?)color.ColorIndex : null,
                rgb = color.IsByColor ? new[] { (int)color.Red, color.Green, color.Blue } : null
            },
            linetype = entity.Linetype,
            linetype_scale = Number(entity.LinetypeScale),
            lineweight = new { mode = weight switch { -1 => "by_layer", -2 => "by_block", -3 => "default", >= 0 => "explicit", _ => "unknown" },
                value_hundredth_mm = weight >= 0 ? (int?)weight : null, native_value = weight }
        };
    }
    private static object PolylineGeometry(Polyline polyline, CancellationToken cancellationToken)
    {
        int count = polyline.NumberOfVertices;
        if (count < 0) throw new DomainException("OBJECT_READ_FAILED", "Polyline returned a negative vertex count.");
        var vertices = new List<object>();
        long bytes = 1024;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ocs = polyline.GetPoint2dAt(i);
            var vertex = new { point_wcs = Point(polyline.GetPoint3dAt(i)), point_ocs = new[] { Number(ocs.X), Number(ocs.Y) },
                bulge = Number(polyline.GetBulgeAt(i)), start_width = Number(polyline.GetStartWidthAt(i)), end_width = Number(polyline.GetEndWidthAt(i)) };
            bytes += System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(vertex, Wire.Json).Length + 1;
            ReadBudget(bytes);
            vertices.Add(vertex);
        }
        var geometry = new { closed = polyline.Closed, elevation = Number(polyline.Elevation), normal = Vector(polyline.Normal),
            thickness = Number(polyline.Thickness), constant_width = Number(polyline.ConstantWidth), vertex_count = count, vertices };
        ReadBudget(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(geometry, Wire.Json).Length);
        return geometry;
    }
    private static double Number(double number) => double.IsFinite(number) ? number :
        throw new DomainException("OBJECT_READ_FAILED", "Required geometry contains a non-finite number.");
    private static double[] Point(Point3d point) => new[] { Number(point.X), Number(point.Y), Number(point.Z) };
    private static double[] Vector(Vector3d vector) => new[] { Number(vector.X), Number(vector.Y), Number(vector.Z) };
    private static void ReadBudget(long bytes)
    {
        if (bytes > Wire.PayloadBudget - 8192)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Stored object detail exceeds the actual response-byte budget; no vertex or text truncation occurred.");
    }
    private static string Text(string text)
    {
        ReadBudget(System.Text.Encoding.UTF8.GetByteCount(text));
        ReadBudget(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(text, Wire.Json).Length);
        return text;
    }
}
