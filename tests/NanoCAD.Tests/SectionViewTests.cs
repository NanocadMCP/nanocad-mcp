using NanoCAD.Contracts;
using NanoCAD.McpServer;
internal static class SectionViewTests
{
    internal static Dictionary<string, object> Edit() => new() { ["operation"] = "create_section_view", ["source_handles"] = new[] { "00071", "7f" }, ["name"] = "Section A", ["section_line"] = new[] { new[] { 150d, -100, 0 }, new[] { 150d, 300, 0 } }, ["origin"] = new[] { 2000d, 0 }, ["scale"] = 0.5 };
    private static StructureWriteArguments Parse(Dictionary<string, object> edit) => StructureWriteValidation.Parse(Wire.Element(DrawingViewCreateTests.Input(edit)));
    private static void Reject(Dictionary<string, object> edit)
    { try { Parse(edit); } catch (DomainException) { return; } throw new Exception("Invalid section input accepted."); }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("section views require all explicit source geometry and placement fields", () =>
        {
            var spec = (CreateSectionViewEdit)Parse(Edit()).Operations.Single();
            if (!spec.SourceHandles.SequenceEqual(new[] { "71", "7F" }) || spec.SectionLine[0].X != 150 || spec.Scale != 0.5) throw new Exception("Section normalization failed.");
            foreach (var key in new[] { "source_handles", "name", "section_line", "origin", "scale" }) { var e = Edit(); e.Remove(key); Reject(e); }
            var many = Edit(); many["source_handles"] = Enumerable.Range(1, 40).Select(i => i.ToString("X")).ToArray(); Parse(many);
            var input = DrawingViewCreateTests.Input(Edit(), new() { ["operation"] = "create_layer", ["name"] = "L" });
            try { StructureWriteValidation.Parse(Wire.Element(input)); } catch (DomainException e) when (e.Error.code == "INVALID_OPERATION_ORDER") { return Task.CompletedTask; }
            throw new Exception("Mixed section batch accepted.");
        });
        await test("section views reject ambiguous axes duplicate sources and excessive coordinates", () =>
        {
            foreach (var handles in new[] { Array.Empty<string>(), new[] { "71", "00071" }, new[] { "0" } }) { var e = Edit(); e["source_handles"] = handles; Reject(e); }
            foreach (var line in new[] { new[] { new[] { 150d, 0, 0 }, new[] { 151d, 300, 0 } }, new[] { new[] { 150d, 0, 0 }, new[] { 150d, 300, 1 } }, new[] { new[] { 150d, 0, 0 }, new[] { 150d, -1, 0 } }, new[] { new[] { 150d, 0, 0 }, new[] { 150d, 0, 0 } }, new[] { new[] { 1e7, 0, 0 }, new[] { 1e7, 300, 0 } } }) { var e = Edit(); e["section_line"] = line; Reject(e); }
            foreach (var scale in new[] { 0d, -1, 1001 }) { var e = Edit(); e["scale"] = scale; Reject(e); }
            var extra = Edit(); extra["viewing_direction"] = new[] { 0, 1, 0 }; Reject(extra);
            return Task.CompletedTask;
        });
        await test("section view fingerprint binds fields and canonical source set", () =>
        {
            var expected = Parse(Edit()).Fingerprint();
            foreach (var key in new[] { "source_handles", "name", "section_line", "origin", "scale" })
            {
                var e = Edit(); e[key] = key switch { "source_handles" => new[] { "71", "80" }, "name" => "Other", "section_line" => new[] { new[] { 151d, -100, 0 }, new[] { 151d, 300, 0 } }, "origin" => new[] { 2001d, 0 }, _ => 2d };
                if (Parse(e).Fingerprint() == expected) throw new Exception("Unbound section field: " + key);
            }
            var reversed = Edit(); reversed["source_handles"] = new[] { "7F", "71" }; if (Parse(reversed).Fingerprint() != expected) throw new Exception("Equivalent source sets differ.");
            return Task.CompletedTask;
        });
        await test("section schema includes source scope scale meaning and read metadata", () =>
        {
            var write = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_structure");
            var focused = AuditServerContractSupport.Branch("modify_structure", "create_section_view");
            if (!write.InputSchema.GetRawText().Contains("create_section_view") || !focused.GetProperty("description").GetString()!.Contains("denominator") || focused.GetProperty("properties").GetProperty("scale").GetProperty("minimum").GetDouble() != 0.001 || !AuditServerContractSupport.Output("get_drawing_views").GetRawText().Contains("vertical_direction")) throw new Exception("Section contract missing.");
            return Task.CompletedTask;
        });
        await test("section views accept explicit orthogonal frames and retain the legacy default", () =>
        {
            var edit = Edit(); var legacy = Parse(edit); var original = (CreateSectionViewEdit)legacy.Operations.Single();
            if (original.ViewingDirection != new Point3(1, 0, 0) || original.VerticalDirection != new Point3(0, 0, 1)) throw new Exception("Legacy section basis changed.");
            edit["viewing_direction"] = new[] { 1, 0, 0 }; edit["vertical_direction"] = new[] { 0, 0, 1 };
            if (Parse(edit).Fingerprint() != legacy.Fingerprint()) throw new Exception("Explicit default differs from omitted default.");
            edit["viewing_direction"] = new[] { 0, -1, 0 }; edit["section_line"] = new[] { new[] { -50, 0, 0 }, new[] { 50, 0, 0 } };
            var rotated = Parse(edit); if (rotated.Fingerprint() == legacy.Fingerprint() || ((CreateSectionViewEdit)rotated.Operations.Single()).ViewingDirection != new Point3(0, -1, 0)) throw new Exception("Rotated section lost its basis.");
            double a = 1 / Math.Sqrt(2); edit["viewing_direction"] = new[] { a, a, 0 }; edit["section_line"] = new[] { new[] { 0d, 0, 0 }, new[] { -a, a, 0 } }; Parse(edit);
            edit["viewing_direction"] = new[] { 0, 0, 1 }; edit["vertical_direction"] = new[] { 0, 1, 0 }; edit["section_line"] = new[] { new[] { -5, 0, 10 }, new[] { 5, 0, 10 } }; Parse(edit);
            return Task.CompletedTask;
        });
        await test("section views reject incomplete scaled nonorthogonal and reversed frames", () =>
        {
            var edit = Edit(); edit["vertical_direction"] = new[] { 0, 0, 1 }; Reject(edit);
            edit["viewing_direction"] = new[] { 1, 0, 0 };
            foreach (var axis in new object[] { new[] { 0, 0, 0 }, new[] { 2, 0, 0 }, new[] { 0, 0, 1 }, new[] { 1, 0 }, null! }) { edit["viewing_direction"] = axis; Reject(edit); }
            edit["viewing_direction"] = new[] { 0, -1, 0 }; edit["section_line"] = new[] { new[] { 50, 0, 0 }, new[] { -50, 0, 0 } }; Reject(edit);
            edit["section_line"] = new[] { new[] { -50, 0, 0 }, new[] { 50, 0, 0 } }; Parse(edit);
            edit["vertical_direction"] = new[] { 0, 0, -1 }; Reject(edit);
            return Task.CompletedTask;
        });
    }
}
