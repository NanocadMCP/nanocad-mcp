using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed class CadSolidGroupMembership
{
    private sealed record GroupState(string Name, string Description, bool Selectable, ObjectId[] Members, HashSet<ObjectId> MemberIds);

    private readonly Dictionary<ObjectId, HashSet<ObjectId>> groupsByBody = new();
    private readonly Dictionary<ObjectId, GroupState> groups = new();

    internal static IEnumerable<string> StrictSources(EditSpec operation) => operation switch
    {
        SolidBooleanEditSpec boolean => boolean.ToolHandles,
        SolidFaceEditSpec { Kind: "offset_faces" or "presspull_faces" or "extrude_faces" or "taper_faces" } => Array.Empty<string>(),
        MoveSpec or RotateSpec or ScaleSpec or TransformSolidSpec or PropertiesSpec or
            SolidFinishingEditSpec or SolidDrillEditSpec or SolidOffsetEdit353 => Array.Empty<string>(),
        _ => EditReferences.All(operation)
    };

    private HashSet<ObjectId> MemberGroups(Solid3d solid, Transaction transaction, Action cancel, bool capture)
    {
        var result = new HashSet<ObjectId>();
        using var reactors = solid.GetPersistentReactorIds();
        foreach (ObjectId id in reactors)
        {
            cancel();
            if (groups.TryGetValue(id, out var known))
            {
                if (known.MemberIds.Contains(solid.ObjectId)) result.Add(id);
                continue;
            }
            if (id.IsNull || !id.IsValid || id.Database != solid.Database ||
                !CadDrawingGroups431.OrganizationalReactor(solid, id, transaction, cancel)) continue;
            result.Add(id);
            if (capture)
            {
                var db = solid.Database;
                var dictionary = (DBDictionary)transaction.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var group = (Group)transaction.GetObject(id, OpenMode.ForRead);
                var members = group.GetAllEntityIds() ?? throw new DomainException("GROUP_MEMBERS_UNAVAILABLE", "The native group member list is unavailable.");
                var memberIds = new HashSet<ObjectId>();
                foreach (var member in members)
                {
                    cancel();
                    if (member.IsNull || !member.IsValid || !memberIds.Add(member))
                        throw new DomainException("GROUP_MEMBERS_UNAVAILABLE", "A native group's member identity is unavailable or duplicated.");
                }
                if (!memberIds.Contains(solid.ObjectId))
                    throw new DomainException("GROUP_MEMBERS_UNAVAILABLE", "The group does not retain the solid identified by its persistent reactor.");
                groups.Add(id, new(dictionary.NameAt(id), group.Description ?? "", group.Selectable, members, memberIds));
            }
        }
        return result;
    }

    internal IReadOnlySet<ObjectId> Capture(Solid3d solid, Transaction transaction, Action cancel)
    {
        var ids = MemberGroups(solid, transaction, cancel, capture: true);
        if (ids.Count != 0) groupsByBody.Add(solid.ObjectId, ids);
        return ids;
    }

    internal void Verify(Transaction transaction, Action cancel)
    {
        // Re-read each whole group once before using its member index to check the current reverse links.
        foreach (var (id, before) in groups)
        {
            cancel();
            if (!id.IsValid || id.IsErased || id.IsEffectivelyErased)
                throw new DomainException("OBJECT_IDENTITY_MISMATCH", "A native group was lost during the in-place solid edit.");
            var db = id.Database;
            var dictionary = (DBDictionary)transaction.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
            if (!dictionary.Contains(id) || dictionary.NameAt(id) != before.Name ||
                transaction.GetObject(id, OpenMode.ForRead) is not Group group || group.GetType() != typeof(Group) ||
                group.OwnerId != db.GroupDictionaryId || (group.Description ?? "") != before.Description || group.Selectable != before.Selectable ||
                group.GetAllEntityIds() is not { } members || !before.Members.SequenceEqual(members))
                throw new DomainException("OBJECT_IDENTITY_MISMATCH", "A native group changed its name, properties or members during the in-place solid edit.");
        }
        foreach (var (bodyId, expected) in groupsByBody)
        {
            if (bodyId.IsErased || bodyId.IsEffectivelyErased ||
                transaction.GetObject(bodyId, OpenMode.ForRead) is not Solid3d solid ||
                !expected.SetEquals(MemberGroups(solid, transaction, cancel, capture: false)))
                throw new DomainException("OBJECT_IDENTITY_MISMATCH", "The solid's native group membership changed during the in-place edit.");
        }
    }
}
