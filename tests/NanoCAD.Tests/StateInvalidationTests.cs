using NanoCAD.Contracts;

internal static class StateInvalidationTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("state invalidation rejects cursors after shared context changes without native reads", () =>
        {
            var context = new ContextEpoch(); var first = new DocumentVersion(context); var second = new DocumentVersion(context);
            var pages = new SnapshotPages<EntityRef>(r => r.handle);
            var firstId = Guid.NewGuid().ToString("D");
            EntityRef[] items = { new("bridge",firstId,"A"),new("bridge",firstId,"B") };
            var page = pages.First(firstId, first.Revision, null, "model", items, 1);
            string oldFirst = first.Revision, oldSecond = second.Revision;
            second.Changed();
            pages.Next(page.next_cursor!, firstId, first.Revision, null, "model", 1);
            context.Advance();
            if (first.Revision == oldFirst || second.Revision == oldSecond) throw new Exception("Shared context did not invalidate both documents.");
            try { pages.Next(page.next_cursor!, firstId, first.Revision, null, "model", 1); }
            catch (DomainException e) when (e.Error.code == "STALE_DOCUMENT") { return Task.CompletedTask; }
            throw new Exception("Old cursor survived context change.");
        });
        await test("state invalidation distinguishes selection edits and never decreases on undo events", () =>
        {
            var context = new ContextEpoch(); var state = new DocumentVersion(context);
            string revision = state.Revision, selection = state.SelectionRevision;
            state.SelectionChanged();
            if (revision != state.Revision || selection == state.SelectionRevision) throw new Exception("Selection changed geometry revision.");
            ulong previous = ulong.Parse(state.Revision);
            // Object erase/unappend/reappend and command completion all use Changed.
            for (int i=0;i<6;i++) { state.Changed(); ulong now=ulong.Parse(state.Revision); if(now<=previous) throw new Exception("Revision went backwards."); previous=now; }
            selection=state.SelectionRevision; context.Advance();
            if (ulong.Parse(state.Revision)<=previous || state.SelectionRevision==selection) throw new Exception("Context did not invalidate both revision kinds.");
            return Task.CompletedTask;
        });
    }
}
