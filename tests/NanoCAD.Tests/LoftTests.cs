using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class LoftTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string bridge = Guid.NewGuid().ToString("D"), doc = Guid.NewGuid().ToString("D"), op = Guid.NewGuid().ToString("D");
        object Section(double z, double radius = 2, double x = 0) => new { center = new[] { x, 0, z }, radius };
        object Loft(params object[] sections) => new { type = "Solid3d", kind = "loft", interpolation = "smooth", sections };
        CreateArguments Parse(params object[] entities) => CreateValidation.Parse(Wire.Element(new { bridge_instance_id = bridge, document_id = doc, operation_id = op, expected_revision = "1", units = "Millimeters", entities }));
        void Reject(object item) { try { Parse(item); } catch (DomainException) { return; } throw new Exception("Invalid loft accepted."); }
        await test("direct loft section normal conditions survive parsing and operation replay identity", () =>
        {
            var input = new Dictionary<string, object?>
            {
                ["type"] = "Solid3d",
                ["kind"] = "loft",
                ["interpolation"] = "smooth",
                ["section_handles"] = new[] { "A", "B" },
                ["guide_handles"] = new[] { "C", "D" },
                ["path_handle"] = null
            };
            var original = Parse(input);
            if (Wire.Element((CurveSolidSpec)original.Entities.Single()).TryGetProperty("NormalOption", out _))
                throw new Exception("An omitted option must keep its previous serialized intent.");
            var fingerprints = new HashSet<string> { original.Fingerprint() };
            foreach (var option in new[] { "none", "first", "last", "ends", "all" })
            {
                input["normal_option"] = option;
                var parsed = Parse(input);
                var spec = (CurveSolidSpec)parsed.Entities.Single();
                if (spec.NormalOption != option || !spec.Guides.SequenceEqual(new[] { "C", "D" }) ||
                    !fingerprints.Add(parsed.Fingerprint()))
                    throw new Exception("Loft guide order or section normal intent was lost.");
            }
            input["normal_option"] = "tangent_to_guide";
            Reject(input);
            return Task.CompletedTask;
        });
        await test("loft guide inspection preserves source order and requires distinct current finite-curve references", () =>
        {
            var input = new Dictionary<string, object?>
            {
                ["bridge_instance_id"] = bridge,
                ["document_id"] = doc,
                ["expected_revision"] = "2",
                ["mode"] = LoftGuideContacts.Mode,
                ["guide_handle"] = "0f",
                ["handles"] = new[] { "b", "a" }
            };
            GeometryAnalysisArguments Read() => GeometryAnalysisValidation.Parse(Wire.Element(input));
            void InvalidRead()
            {
                try { Read(); } catch (DomainException) { return; }
                throw new Exception("Invalid loft guide inspection accepted.");
            }
            var parsed = Read();
            if (parsed.GuideHandle != "F" || !parsed.Read.Handles!.SequenceEqual(new[] { "B", "A" }) || parsed.Workplane != null)
                throw new Exception("Expected ordered WCS profile handles and one separate guide.");
            input["workplane"] = "XY"; InvalidRead(); input.Remove("workplane");
            input["handles"] = new[] { "a", "0A" }; InvalidRead();
            input["handles"] = new[] { "F" }; InvalidRead();
            input["handles"] = Array.Empty<string>(); InvalidRead();
            input["handles"] = new[] { "B" }; input.Remove("guide_handle"); InvalidRead();
            input["guide_handle"] = "F"; input.Remove("document_id"); InvalidRead();
            input["document_id"] = doc;
            input["guide_handles"] = new[] { "d", "c" }; InvalidRead(); input.Remove("guide_handle");
            input["handles"] = new[] { "b", "a" };
            var group = Read();
            if (group.GuideHandle != null || !group.GuideHandles!.SequenceEqual(new[] { "D", "C" }) ||
                !group.Read.Handles!.SequenceEqual(new[] { "B", "A" }))
                throw new Exception("Guide or section order changed.");
            input["guide_handles"] = new[] { "d", "0D" }; InvalidRead();
            input["guide_handles"] = new[] { "d", "A" }; InvalidRead();
            input["guide_handles"] = Array.Empty<string>(); InvalidRead();
            var inputSchema = AuditServerContractSupport.Input("analyze_geometry", LoftGuideContacts.Mode);
            if (inputSchema.GetProperty("properties").GetProperty("mode").GetProperty("const").GetString() != LoftGuideContacts.Mode ||
                !inputSchema.GetProperty("properties").TryGetProperty("guide_handles", out _) ||
                !inputSchema.GetProperty("properties").TryGetProperty("guide_handle", out _))
                throw new Exception("Guide comparison is missing from the published analysis schema.");
            var outputBranches = AuditServerContractSupport.Output("analyze_geometry").GetProperty("properties")
                .GetProperty("data").GetProperty("anyOf")[0].GetProperty("oneOf").EnumerateArray();
            var output = outputBranches.Single(node => node.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty("mode", out var mode) && mode.TryGetProperty("const", out var value) &&
                value.GetString() == LoftGuideContacts.Mode).GetProperty("properties");
            var statuses = output.GetProperty("sections").GetProperty("items").GetProperty("properties")
                .GetProperty("status").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToHashSet();
            if (output.GetProperty("overlap_evaluated").GetProperty("const").GetBoolean() ||
                !statuses.SetEquals(new[] { "points_reported", "no_points_reported", "unavailable" }))
                throw new Exception("The published contact output loses native uncertainty.");
            return Task.CompletedTask;
        });
        await test("solid loft preserves all circular sections in ledger intent", () =>
        {
            var value = Parse(Loft(Section(-3), Section(5, 4, 7)));
            var spec = (LoftedSolidSpec)value.Entities.Single();
            if (spec.Sections.Length != 2 || spec.Sections[1] != new CircularLoftSection(new Point3(7, 0, 5), 4)) throw new Exception("Section geometry lost.");
            foreach (var changed in new[] { Loft(Section(-3), Section(6, 4, 7)), Loft(Section(-3), Section(5, 3, 7)), Loft(Section(-3), Section(5, 4, 8)), Loft(Section(-3), Section(0), Section(5, 4, 7)) })
                if (Parse(changed).Fingerprint() == value.Fingerprint()) throw new Exception("Changed loft shares fingerprint.");
            return Task.CompletedTask;
        });
        await test("solid loft rejects unordered intersecting planes and oversized scope", () =>
        {
            Reject(Loft(Section(0))); Reject(Loft(Section(0), Section(0))); Reject(Loft(Section(1), Section(0)));
            Reject(Loft(Section(0), Section(2, 0))); Reject(Loft(Section(0), Section(1e6 + 1)));
            Reject(Loft(Section(0), Section(1, 2, 1e6 + 1)));
            if (((LoftedSolidSpec)Parse(Loft(Enumerable.Range(0, 9).Select(i => Section(i)).ToArray())).Entities.Single()).Sections.Length != 9) throw new Exception("Former loft section count ceiling remains.");
            Reject(new { type = "Solid3d", kind = "loft", sections = new[] { Section(0), Section(10) }, smooth = true });
            Reject(new { type = "Solid3d", kind = "loft", interpolation = "automatic", sections = new[] { Section(0), Section(10) } });
            return Task.CompletedTask;
        });
        await test("solid ruled loft explicit intent differs from smooth", () =>
        {
            var ruled = Parse(new { type = "Solid3d", kind = "loft", interpolation = "ruled", sections = new[] { Section(0), Section(10) } });
            if (((LoftedSolidSpec)ruled.Entities[0]).Interpolation != "ruled" || ruled.Fingerprint() == Parse(Loft(Section(0), Section(10))).Fingerprint()) throw new Exception("Interpolation intent lost.");
            return Task.CompletedTask;
        });
        await test("solid ruled loft independent mass handles slope and translation", () =>
        {
            var sections = new[] { new CircularLoftSection(new Point3(1000000, -20, 5), 2), new CircularLoftSection(new Point3(1000006, -14, 11), 4) };
            var m = RuledLoftGeometry.Mass(sections);
            double t = 17d / 28;
            if (Math.Abs(m.Volume - 56 * Math.PI) > 1e-10 || Math.Abs(m.Centroid.X - (1000000 + 6 * t)) > 1e-9 || Math.Abs(m.Centroid.Y - (-20 + 6 * t)) > 1e-10 || Math.Abs(m.Centroid.Z - (5 + 6 * t)) > 1e-10) throw new Exception("Wrong affine frustum mass.");
            return Task.CompletedTask;
        });
        await test("solid ruled loft accepts different interpolation but rejects invalid samples", () =>
        {
            var sections = new[] { new CircularLoftSection(new Point3(0, 0, 0), 2), new CircularLoftSection(new Point3(4, 2, 10), 3) };
            var side = (from t in new[] { 0d, .25, .5, .75, 1d } from a in new[] { 0d, Math.PI / 2, Math.PI, Math.PI * 1.5, Math.PI * 2 } select new Point3(4 * t + (2 + t) * Math.Cos(a), 2 * t + (2 + t) * Math.Sin(a), 10 * t)).ToArray();
            var bottom = Enumerable.Repeat(new Point3(0, 0, 0), 25).ToArray(); var top = Enumerable.Repeat(new Point3(4, 2, 10), 25).ToArray();
            RuledLoftGeometry.VerifySurface(sections, new[] { side, bottom, top });
            void Bad(Point3[][] f) { try { RuledLoftGeometry.VerifySurface(sections, f); } catch (DomainException) { return; } throw new Exception("Invalid surface accepted."); }
            var distorted = side.ToArray(); distorted[12] = distorted[12] with { X = distorted[12].X + .01 };
            RuledLoftGeometry.VerifySurface(sections, new[] { distorted, bottom, top });
            foreach (double invalidCoordinate in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var invalid = side.ToArray(); invalid[3] = new Point3(invalidCoordinate, 0, 0);
                try { RuledLoftGeometry.VerifySurface(sections, new[] { invalid, bottom, top }); throw new Exception("Nonfinite surface accepted."); }
                catch (DomainException e) when (e.Error.code == "INVALID_GEOMETRY") { }
            }
            Bad(Array.Empty<Point3[]>()); Bad(new[] { side.Take(24).ToArray() });
            return Task.CompletedTask;
        });
        await test("solid loft respects shared body budget block exclusion and schema", () =>
        {
            var loft = Loft(Section(0), Section(10));
            if (Parse(Enumerable.Repeat(loft, 9).ToArray()).Entities.Length != 9) throw new Exception("Former loft body count ceiling remains.");
            try { CreateValidation.ParseEntities(Wire.Element(new[] { loft })); throw new Exception("Loft entered block definition scope."); } catch (DomainException) { }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
