using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using NativeColor = Teigha.Colors.Color;

namespace NanoCADBridge;

// Native metadata only. The caller owns the active ForRead transaction.
internal static class DimensionStyleMetadataReader
{
    public static DimensionStyleProperties Read(DimStyleTableRecord source, Transaction tr, Database db,
        Func<ObjectId, EntityRef> reference, Action check, Action countDependency)
    {
        T Get<T>(Func<T> getter) { check(); T value = getter(); check(); return value; }
        check();
        if (source.GetType() != typeof(DimStyleTableRecord) || Get(() => source.OwnerId) != Get(() => db.DimStyleTableId))
            throw new DomainException("OBJECT_READ_FAILED", "Expected an exact dimension style in the active database table.");
        var sourceId = Get(() => source.ObjectId);
        var identity = Get(() => reference(sourceId));
        return ReadCore(source, identity, tr, db, reference, check, countDependency);
    }

    // The source context validates dependency lifetimes only. It is never
    // returned as the identity of this detached preview object.
    public static DimensionStyleProperties ReadDetached(DimStyleTableRecord copy, EntityRef sourceContext,
        Transaction tr, Database db, Func<ObjectId, EntityRef> reference, Action check, Action countDependency)
    {
        check();
        if (copy.GetType() != typeof(DimStyleTableRecord) || !copy.ObjectId.IsNull)
            throw new DomainException("OBJECT_READ_FAILED", "Expected an exact detached dimension style preview.");
        check();
        return ReadCore(copy, sourceContext, tr, db, reference, check, countDependency);
    }

    private static DimensionStyleProperties ReadCore(DimStyleTableRecord source, EntityRef identity,
        Transaction tr, Database db, Func<ObjectId, EntityRef> reference, Action check, Action countDependency)
    {
        T Get<T>(Func<T> getter) { check(); T value = getter(); check(); return value; }
        var name = DimensionStyleMetadata.Text(Get(() => source.Name), 16384);
        var variables = new DimensionVariables
        {
            dimadec = Get(() => source.Dimadec),
            dimalt = Get(() => source.Dimalt),
            dimaltd = Get(() => source.Dimaltd),
            dimaltf = Get(() => source.Dimaltf),
            dimaltrnd = Get(() => source.Dimaltrnd),
            dimalttd = Get(() => source.Dimalttd),
            dimalttz = Get(() => source.Dimalttz),
            dimaltu = Get(() => source.Dimaltu),
            dimaltz = Get(() => source.Dimaltz),
            dimapost = Get(() => source.Dimapost),
            dimarcsym = Get(() => source.Dimarcsym),
            dimatfit = Get(() => source.Dimatfit),
            dimaunit = Get(() => source.Dimaunit),
            dimazin = Get(() => source.Dimazin),
            dimcen = Get(() => source.Dimcen),
            dimdec = Get(() => source.Dimdec),
            dimdle = Get(() => source.Dimdle),
            dimdli = Get(() => source.Dimdli),
            dimdsep = Get(() => source.Dimdsep),
            dimexe = Get(() => source.Dimexe),
            dimexo = Get(() => source.Dimexo),
            dimfrac = Get(() => source.Dimfrac),
            dimfxlen = Get(() => source.Dimfxlen),
            dimfxlenon = Get(() => source.DimfxlenOn),
            dimgap = Get(() => source.Dimgap),
            dimjogang = Get(() => source.Dimjogang),
            dimjust = Get(() => source.Dimjust),
            dimlfac = Get(() => source.Dimlfac),
            dimlim = Get(() => source.Dimlim),
            dimlunit = Get(() => source.Dimlunit),
            dimpost = Get(() => source.Dimpost),
            dimrnd = Get(() => source.Dimrnd),
            dimsah = Get(() => source.Dimsah),
            dimsd1 = Get(() => source.Dimsd1),
            dimsd2 = Get(() => source.Dimsd2),
            dimse1 = Get(() => source.Dimse1),
            dimse2 = Get(() => source.Dimse2),
            dimsoxd = Get(() => source.Dimsoxd),
            dimtad = Get(() => source.Dimtad),
            dimtdec = Get(() => source.Dimtdec),
            dimtfac = Get(() => source.Dimtfac),
            dimtfill = Get(() => source.Dimtfill),
            dimtih = Get(() => source.Dimtih),
            dimtix = Get(() => source.Dimtix),
            dimtm = Get(() => source.Dimtm),
            dimtmove = Get(() => source.Dimtmove),
            dimtofl = Get(() => source.Dimtofl),
            dimtoh = Get(() => source.Dimtoh),
            dimtol = Get(() => source.Dimtol),
            dimtolj = Get(() => source.Dimtolj),
            dimtp = Get(() => source.Dimtp),
            dimtsz = Get(() => source.Dimtsz),
            dimtvp = Get(() => source.Dimtvp),
            dimtzin = Get(() => source.Dimtzin),
            dimupt = Get(() => source.Dimupt),
            dimzin = Get(() => source.Dimzin),
        };
        var dependencies = new DimensionReferences(
            arrow: Dependency(Get(() => source.Dimblk), "block", tr, db, reference, check, countDependency),
            first_arrow: Dependency(Get(() => source.Dimblk1), "block", tr, db, reference, check, countDependency),
            second_arrow: Dependency(Get(() => source.Dimblk2), "block", tr, db, reference, check, countDependency),
            leader_arrow: Dependency(Get(() => source.Dimldrblk), "block", tr, db, reference, check, countDependency),
            extension1_linetype: Dependency(Get(() => source.Dimltex1), "linetype", tr, db, reference, check, countDependency),
            extension2_linetype: Dependency(Get(() => source.Dimltex2), "linetype", tr, db, reference, check, countDependency),
            dimension_linetype: Dependency(Get(() => source.Dimltype), "linetype", tr, db, reference, check, countDependency),
            text_style: Dependency(Get(() => source.Dimtxsty), "text_style", tr, db, reference, check, countDependency));
        var colors = new DimensionColors(
            dimension: ReadColor(() => source.Dimclrd, check),
            extension: ReadColor(() => source.Dimclre, check),
            text: ReadColor(() => source.Dimclrt, check),
            text_fill: ReadColor(() => source.Dimtfillclr, check));
        var weights = new DimensionLineweights(
            dimension: Weight(Get(() => (int)source.Dimlwd)),
            extension: Weight(Get(() => (int)source.Dimlwe)));
        int annotative = Get(() => (int)source.Annotative);
        bool hasXdata;
        // Do not acquire disposable native wrappers through Get: a deadline
        // exception after acquisition must still enter a using/finally scope.
        check();
        using (var data = source.XData)
        {
            check();
            hasXdata = data is not null && DimensionMetadataEnumeration.HasAny(data.GetEnumerator(), check);
        }
        var extensionDictionary = Get(() => source.ExtensionDictionary);
        var properties = new DimensionStyleProperties(
            Get(() => source.IsDependent), Get(() => source.Dimscale), Get(() => source.Dimtxt), Get(() => source.Dimasz),
            variables, dependencies, colors, weights, DimensionStyleMetadata.Annotative(annotative), annotative,
            Get(() => !extensionDictionary.IsNull), Get(() => source.HasFields), hasXdata,
            DimensionStyleMetadata.FamilyNameCandidate(name));
        var result = DimensionStyleMetadata.Validate(name, identity, properties);
        check();
        return result;
    }

    private static DimensionLineweight Weight(int value) => new(DimensionStyleMetadata.WeightMode(value), value);

    private static DimensionColor ReadColor(Func<NativeColor> acquire, Action check)
    {
        check();
        using var color = acquire();
        check();
        if (color is null) throw new DomainException("OBJECT_READ_FAILED", "Missing native dimension style color.");
        T Get<T>(Func<T> getter) { check(); T value = getter(); check(); return value; }
        int method = Get(() => (int)color.ColorMethod);
        short? aci = method == 195 ? Get(() => color.ColorIndex) : null;
        var rgb = method == 194 ? new DimensionRgb(Get(() => color.Red), Get(() => color.Green), Get(() => color.Blue)) : null;
        var result = new DimensionColor(DimensionStyleMetadata.ColorMethod(method), method, aci, rgb,
            Get(() => color.HasColorName), Get(() => color.ColorName), Get(() => color.BookName));
        check();
        return result;
    }

    private static DimensionDependency Dependency(ObjectId id, string kind, Transaction tr, Database db,
        Func<ObjectId, EntityRef> reference, Action check, Action countDependency)
    {
        T Get<T>(Func<T> getter) { check(); T value = getter(); check(); return value; }
        DimensionDependency Unavailable(string reason) => new(kind, "unavailable", null, null, reason);
        if (Get(() => id.IsNull)) return new(kind, "null", null, null, null);
        if (!Get(() => id.IsValid)) return Unavailable("invalid_id");
        if (Get(() => id.IsErased)) return Unavailable("erased");
        try
        {
            ObjectId owner = kind switch {
                "block" => Get(() => db.BlockTableId),
                "text_style" => Get(() => db.TextStyleTableId),
                "linetype" => Get(() => db.LinetypeTableId),
                _ => throw new DomainException("OBJECT_READ_FAILED", "Unknown dimension dependency table.") };
            Type expected = kind switch { "block" => typeof(BlockTableRecord), "text_style" => typeof(TextStyleTableRecord), _ => typeof(LinetypeTableRecord) };
            countDependency();
            // Objects opened through the caller's transaction remain its responsibility.
            var target = Get(() => tr.GetObject(id, OpenMode.ForRead));
            if (target is null || target.GetType() != expected) return Unavailable("wrong_type");
            if (Get(() => target.OwnerId) != owner) return Unavailable("wrong_owner");
            string name = DimensionStyleMetadata.Text(Get(() => ((SymbolTableRecord)target).Name), 16384);
            return new(kind, "resolved", Get(() => reference(id)), name, null);
        }
        catch (Teigha.Runtime.Exception) { check(); return Unavailable("read_failed"); }
    }
}
