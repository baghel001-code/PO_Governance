using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Services.V1.Analytics.Grc;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace POGovernance.Tests.Analytics.Grc
{
    /// <summary>
    /// Loads tests/parity/grc/fixture.json + expected.json (written by tests/parity/grc/run.js, which runs
    /// the original index.html functions) and builds the matching EngineData snapshots.
    /// </summary>
    public sealed class GrcParityFixture : IDisposable
    {
        public JsonDocument Fixture { get; }
        public JsonDocument Expected { get; }
        public JsonElement E => Expected.RootElement;

        public GrcParityFixture()
        {
            var dir = FindDir();
            Fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "fixture.json")));
            Expected = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "expected.json")));
        }

        private static string FindDir()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                var p = Path.Combine(d.FullName, "tests", "parity", "grc");
                if (File.Exists(Path.Combine(p, "expected.json"))) return p;
                d = d.Parent;
            }
            throw new FileNotFoundException("tests/parity/grc/expected.json not found — run `node tests/parity/grc/run.js`");
        }

        public static List<SheetRow> Rows(JsonElement arr) => arr.EnumerateArray().Select(SheetRow.FromJson).ToList();

        public EngineParams Params(JsonElement? over)
        {
            var obj = JsonNode.Parse(Fixture.RootElement.GetProperty("baseParams").GetRawText())!.AsObject();
            if (over is { ValueKind: JsonValueKind.Object } o)
                foreach (var p in o.EnumerateObject()) obj[p.Name] = JsonNode.Parse(p.Value.GetRawText());
            return JsonSerializer.Deserialize<EngineParams>(obj.ToJsonString())!;
        }

        private readonly Dictionary<string, Dictionary<string, IReadOnlyList<SheetRow>?>> _files = new();

        public EngineData Data(string ds, JsonElement? paramsOver = null)
        {
            var d = Fixture.RootElement.GetProperty("datasets").GetProperty(ds);
            if (!_files.TryGetValue(ds, out var files))
            {
                files = new Dictionary<string, IReadOnlyList<SheetRow>?>();
                foreach (var f in d.GetProperty("files").EnumerateObject()) files[f.Name] = Rows(f.Value);
                _files[ds] = files;
            }
            return new EngineData(files, Params(paramsOver), 1, 1, d.GetProperty("grcPeriodType").GetString());
        }

        public static EngineData DataFrom(Dictionary<string, IReadOnlyList<SheetRow>?> files, EngineParams p, string? periodType = null) =>
            new(files, p, 1, 1, periodType);

        public void Dispose()
        {
            Fixture.Dispose();
            Expected.Dispose();
        }
    }

    public sealed class FixedClock : IAppClock
    {
        public FixedClock(DateTimeOffset now) => Now = now;
        public DateTimeOffset Now { get; }
        public string Today => Now.ToString("yyyy-MM-dd");
        public string NowIso => Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
    }

    /// <summary>The service under test, wired without DI (the dataset cache is not used by the sync methods).</summary>
    public static class GrcTestServices
    {
        public static readonly FixedClock Clock = new(new DateTimeOffset(2026, 10, 14, 10, 30, 0, TimeSpan.FromHours(5.5)));
        public static SeasonService Seasons() => new(null!);
        public static GrcBacktestService Grc() => new(null!, Clock, Seasons());
    }
}
