using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// The description is editable in CLASSICGROUP. The marker identifies the purpose
// of the group; native membership and the drawing dictionary identify its body.
internal static class DwgBodyNameGroup
{
    private const string Marker = "NANOCAD_MCP_BODY_NAME";
    internal sealed record Label(ObjectId Id, string Key, string Description)
    {
        internal ModelBodyNameGroup Reference(EntityRef body) =>
            new(Key, body with { handle = Validation.NormalizeHandle(Id.Handle.ToString()) }) { description = Description };
    }

    internal static (Label? Group, bool Ambiguous) Find(Entity solid, Transaction tx, Action? cancel = null)
    {
        Label? result = null;
        using var reactors = solid.GetPersistentReactorIds();
        foreach (ObjectId id in reactors)
        {
            cancel?.Invoke();
            if (id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased) continue;
            if (!string.Equals(id.ObjectClass.DxfName, "GROUP", StringComparison.OrdinalIgnoreCase)) continue;
            if (tx.GetObject(id, OpenMode.ForRead) is not Group group || group.GetType() != typeof(Group) ||
                group.OwnerId != solid.Database.GroupDictionaryId || group.ExtensionDictionary.IsNull) continue;
            var extension = (DBDictionary)tx.GetObject(group.ExtensionDictionary, OpenMode.ForRead);
            if (!extension.Contains(Marker)) continue;
            var dictionary = (DBDictionary)tx.GetObject(solid.Database.GroupDictionaryId, OpenMode.ForRead);
            if (!dictionary.Contains(id) || extension.OwnerId != id) return (null, true);
            if (tx.GetObject(extension.GetAt(Marker), OpenMode.ForRead) is not Xrecord marker || marker.OwnerId != extension.ObjectId)
                return (null, true);
            using var data = marker.Data;
            var version = data?.AsArray();
            if (version is not [{ TypeCode: (int)DxfCode.Int16, Value: short v }] || v != 1) return (null, true);
            var members = group.GetAllEntityIds();
            if (result != null || members.Length != 1 || members[0] != solid.ObjectId) return (null, true);
            result = new Label(id, dictionary.NameAt(id), group.Description ?? "");
        }
        return (result, false);
    }

    internal static Label Write(Solid3d solid, Label? before, string description, Transaction tx, Action cancel)
    {
        var dictionary = (DBDictionary)tx.GetObject(solid.Database.GroupDictionaryId, OpenMode.ForWrite);
        string key = BodyNameGroupKey.Available(description, solid.Handle.ToString(),
            candidate => dictionary.Contains(candidate) && dictionary.GetAt(candidate) != before?.Id);
        Group group;
        if (before == null)
        {
            group = new Group(description, true);
            dictionary.SetAt(key, group);
            tx.AddNewlyCreatedDBObject(group, true);
            group.Append(solid.ObjectId);
            group.CreateExtensionDictionary();
            var extension = (DBDictionary)tx.GetObject(group.ExtensionDictionary, OpenMode.ForWrite);
            using var data = new ResultBuffer(new TypedValue((int)DxfCode.Int16, (short)1));
            var marker = new Xrecord { Data = data };
            extension.SetAt(Marker, marker);
            tx.AddNewlyCreatedDBObject(marker, true);
        }
        else
        {
            group = (Group)tx.GetObject(before.Id, OpenMode.ForWrite);
            if (!string.Equals(key, before.Key, StringComparison.OrdinalIgnoreCase)) dictionary.SetName(before.Key, key);
            group.Description = description;
        }
        var observed = Find(solid, tx, cancel);
        if (observed.Ambiguous || observed.Group?.Id != group.ObjectId || observed.Group.Description != description)
            throw new DomainException("MODEL_UPDATE_FAILED", "The DWG group did not retain the exact body and description.");
        return observed.Group;
    }
}
