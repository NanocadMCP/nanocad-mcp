using NanoCAD.Contracts;

internal static class NativeOwnedDataAssociationTests
{
    static readonly NativeOwnedDataAssociationExpected Expected = new(
        "{A1000000-1111-2222-3333-000000000001}", "{A1000000-1111-2222-3333-000000000002}",
        "{A1000000-1111-2222-3333-000000000003}", "{A1000000-1111-2222-3333-000000000004}",
        "{A1000000-1111-2222-3333-000000000005}");
    const string Foreign = "{20000000-AAAA-BBBB-CCCC-000000000099}";
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    static string?[] Slots(bool documents = true) => [Expected.Drawing, Expected.TopDocument, Expected.OwnerDocument,
        Expected.Root, Expected.Data, Expected.Root, documents ? Expected.OwnerDocument : null,
        documents ? Expected.OwnerDocument : null, documents ? Expected.TopDocument : null];
    static char[] Buffer(string?[] slots)
    {
        Need(slots.Length == 9, "The fixture must specify all nine distinct ABI slots.");
        var result = new char[NativeOwnedDataAssociation.BufferLength];
        for (int index = 0; index < slots.Length; index++)
            if (slots[index] is { } value)
            {
                Need(value.Length < NativeOwnedDataAssociation.SlotWidth, "A fixture value exceeds its native GUID slot.");
                value.CopyTo(0, result, index * NativeOwnedDataAssociation.SlotWidth, value.Length);
            }
        return result;
    }
    static NativeOwnedDataAssociationRead Read(char[] text, NativeOwnedDataAssociationExpected? expected = null) =>
        NativeOwnedDataAssociation.Parse(0, NativeOwnedDataAssociation.CompletePhase, text, expected ?? Expected);
    static DomainException Refuse(Action action)
    {
        try { action(); }
        catch (DomainException error)
        {
            Need(error.Error.code == "DEPENDENCY_OWNERSHIP_MISMATCH" && !error.Error.retryable,
                "A failed ownership wire proof became retryable or lost its precise refusal code.");
            return error;
        }
        throw new Exception("An incomplete or conflicting native association receipt supplied ownership proof.");
    }
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("native owned-data ABI retains all nine GUID slots after the first NUL", () =>
        {
            Need(NativeOwnedDataAssociation.SlotCount == 9 && NativeOwnedDataAssociation.SlotWidth == 39 &&
                NativeOwnedDataAssociation.BufferLength == 351 && NativeOwnedDataAssociation.CompletePhase == 9,
                "The parser no longer matches the documented bounded native ABI.");
            var slots = Slots(); var buffer = Buffer(slots.Select(value => value?.ToLowerInvariant()).ToArray());
            Need(buffer[38] == '\0' && buffer[39] == '{' && buffer[8 * 39] == '{',
                "The fixture does not exercise data beyond the first marshalled NUL.");
            var result = Read(buffer);
            string?[] actual = [result.drawing_work_id, result.top_document_work_id, result.owner_document_work_id,
                result.root_work_id, result.data_work_id, result.parent_work_id, result.native_document_work_id,
                result.host_document_work_id, result.host_top_document_work_id];
            Need(actual.SequenceEqual(slots), "A later slot was truncated, reordered, conflated with another ID or failed canonical GUID normalization.");
            Need(result.native_phase == 9 && result.native_hresult == "0x00000000" &&
                result.proof == "actual_current_physical_root_and_native_owner_lookup",
                "A positive parsed receipt lost its completed native-proof scope.");
            return Task.CompletedTask;
        });
        await test("native owned-data receipt allows absent internal document fields only with complete native proof", () =>
        {
            var buffer = Buffer(Slots(documents: false)); var result = Read(buffer);
            Need(result.root_work_id == Expected.Root && result.data_work_id == Expected.Data && result.parent_work_id == Expected.Root &&
                result.owner_document_work_id == Expected.OwnerDocument && result.top_document_work_id == Expected.TopDocument &&
                result.native_document_work_id == null && result.host_document_work_id == null && result.host_top_document_work_id == null,
                "Absent internal document observations supplied fabricated IDs or obscured actual owner/data identity.");
            Refuse(() => NativeOwnedDataAssociation.Parse(1, 9, buffer, Expected));
            Refuse(() => NativeOwnedDataAssociation.Parse(0, 8, buffer, Expected));
            return Task.CompletedTask;
        });
        await test("native owned-data wire refuses every foreign native slot and mismatched caller identity", () =>
        {
            for (int slot = 0; slot < 9; slot++)
            {
                var values = Slots(); values[slot] = Foreign;
                Refuse(() => Read(Buffer(values)));
            }
            foreach (var expected in new[] { Expected with { Drawing = Foreign }, Expected with { TopDocument = Foreign },
                Expected with { OwnerDocument = Foreign }, Expected with { Root = Foreign }, Expected with { Data = Foreign } })
                Refuse(() => Read(Buffer(Slots()), expected));
            return Task.CompletedTask;
        });
        await test("native owned-data wire never infers success from plausible IDs with failed status or partial phase", () =>
        {
            foreach (var status in new[] { 1, unchecked((int)0x80004005), unchecked((int)0x80070057) })
            {
                var error = Refuse(() => NativeOwnedDataAssociation.Parse(status, 9, Buffer(Slots()), Expected));
                var details = Wire.Element(error.Error.details ?? throw new Exception("The failed native status lost its raw receipt."));
                Need(details.GetProperty("native_hresult").GetString() == "0x" + unchecked((uint)status).ToString("X8") &&
                    details.GetProperty("native_phase").GetInt32() == 9 && details.GetProperty("observed_slots").GetArrayLength() == 9,
                    "A failed status was rewritten as success or lost its full observed slots.");
            }
            foreach (int phase in new[] { 0, 1, 8, 10, -1 })
                Refuse(() => NativeOwnedDataAssociation.Parse(0, phase, Buffer(Slots()), Expected));
            return Task.CompletedTask;
        });
        await test("native owned-data ABI refuses truncated overlong unterminated and trailing-data buffers", () =>
        {
            Refuse(() => Read(null!));
            foreach (int count in new[] { 0, 38, 350, 352 }) Refuse(() => Read(new char[count]));
            var firstNulOnly = new string(Buffer(Slots())).Split('\0')[0].ToCharArray();
            Refuse(() => Read(firstNulOnly));
            var unterminated = Buffer(Slots());
            Array.Fill(unterminated, 'X', 8 * 39, 39); Refuse(() => Read(unterminated));
            var trailing = Buffer(Slots(documents: false));
            trailing[6 * 39 + 1] = 'X'; Refuse(() => Read(trailing));
            var terminatedWithGarbage = Buffer(Slots());
            terminatedWithGarbage[39 + 10] = '\0'; Refuse(() => Read(terminatedWithGarbage));
            return Task.CompletedTask;
        });
        await test("native owned-data wire requires nonzero canonical GUIDs and distinguishes optional absence from zero IDs", () =>
        {
            for (int slot = 0; slot < 6; slot++)
            {
                foreach (string? malformed in new string?[] { null, Guid.Empty.ToString("B"), "{not-a-guid}", Expected.Data.Trim('{', '}') })
                {
                    var values = Slots(); values[slot] = malformed;
                    Refuse(() => Read(Buffer(values)));
                }
            }
            for (int slot = 6; slot < 9; slot++)
            {
                var values = Slots(); values[slot] = Guid.Empty.ToString("B");
                Refuse(() => Read(Buffer(values)));
            }
            Refuse(() => Read(Buffer(Slots()), Expected with { Data = Guid.Empty.ToString("B") }));
            Refuse(() => Read(Buffer(Slots()), Expected with { Drawing = "not-an-id" }));
            return Task.CompletedTask;
        });
    }
}
