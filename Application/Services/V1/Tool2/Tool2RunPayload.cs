using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.DTOs.Recommendation;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Entities.Store;

namespace Application.Services.V1.Tool2
{
    /// <summary>
    /// The saved-run document — exactly what v1.1.4's <c>Shared.saveRun</c> (shared.js) uploads:
    /// <c>{params, dataVersions:{key: manifest entry}, packed:{rows:[BSR rows…], results:[…]}}</c>, gzip JSON.
    /// <see cref="Pack"/> / <see cref="Unpack"/> are ports of shared.js <c>packResults</c> /
    /// <c>unpackResults</c>: rows of the peer pools / siblings / sibRanked are stored once and referenced
    /// by index. Runs saved by v1.1.4 and by this version read the same.
    /// </summary>
    public sealed class Tool2RunPayload
    {
        public JsonObject Params { get; set; } = new();
        public Dictionary<string, DatasetEntry> DataVersions { get; set; } = new();
        public List<Tool2LineResult> Results { get; set; } = new();

        private static readonly JsonSerializerOptions JsonOpts = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>The planner parameters of the run.</summary>
        public EngineParams GetParams()
        {
            try { return Params.Deserialize<EngineParams>() ?? new EngineParams(); }
            catch (JsonException) { return new EngineParams(); }
        }

        // ── write ──────────────────────────────────────────────────────────

        public JsonObject ToJson()
        {
            var o = new JsonObject
            {
                ["params"] = Params.DeepClone(),
                ["dataVersions"] = JsonSerializer.SerializeToNode(DataVersions, JsonOpts),
                ["packed"] = Pack(Results)
            };
            return o;
        }

        public byte[] ToGzip()
        {
            var bytes = Encoding.UTF8.GetBytes(ToJson().ToJsonString(JsonOpts));
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(bytes);
            return ms.ToArray();
        }

        /// <summary>Port of shared.js <c>packResults(results)</c>.</summary>
        public static JsonObject Pack(IReadOnlyList<Tool2LineResult> results)
        {
            var idx = new Dictionary<SheetRow, int>(ReferenceEqualityComparer.Instance);
            var rows = new JsonArray();
            int Ref(SheetRow r)
            {
                if (!idx.TryGetValue(r, out var i)) { i = rows.Count; idx[r] = i; rows.Add(RowNode(r)); }
                return i;
            }
            JsonNode? Pool(List<SheetRow>? l)
            {
                if (l == null) return null;
                var a = new JsonArray();
                foreach (var r in l) a.Add(Ref(r));
                return new JsonObject { ["__rows"] = a };
            }
            var outArr = new JsonArray();
            foreach (var r in results)
            {
                var c = new JsonObject();
                c["idx"] = r.Idx; c["gm"] = r.Gm; c["colour"] = r.Colour; c["dm"] = r.Dm; c["relQty"] = Num(r.RelQty); c["poNum"] = r.PoNum;
                c["engine"] = r.Engine; c["decision"] = r.Decision; c["decisionReason"] = r.DecisionReason;
                if (r.IsErrorRow)
                {
                    c["artSTR"] = null; c["recQty"] = Num(r.RecQty); c["excessQty"] = Num(r.ExcessQty); c["mixPct"] = Num(r.MixPct); c["opStock"] = Num(r.OpStock);
                    c["div"] = r.Div; c["mcDesc"] = r.McDesc; c["mrp"] = r.Mrp; c["vendor"] = r.Vendor; c["info"] = null; c["poValue"] = null; c["poRate"] = null;
                    outArr.Add(c);
                    continue;
                }
                c["strCaution"] = r.StrCaution == null ? "" : CautionNode(r.StrCaution);
                c["artSTR"] = Num(r.ArtStr); c["recQty"] = Num(r.RecQty); c["excessQty"] = Num(r.ExcessQty); c["mixPct"] = Num(r.MixPct); c["opStock"] = Num(r.OpStock);
                c["div"] = r.Div; c["mcDesc"] = r.McDesc; c["mrp"] = r.Mrp; c["vendor"] = r.Vendor;
                c["info"] = InfoNode(r.Info);
                c["poValue"] = Num(r.PoValue); c["poRate"] = Num(r.PoRate);
                c["optionType"] = r.OptionType; c["quartile_"] = r.Quartile; c["percentile"] = Num(r.Percentile); c["pdstr"] = Num(r.Pdstr);
                c["peerAvg"] = Num(r.PeerAvg); c["gapPct"] = Num(r.GapPct); c["inBorderZone"] = r.InBorderZone; c["borderTolUsed"] = Num(r.BorderTolUsed);
                c["wof"] = Num(r.Wof); c["totalBSR"] = Num(r.TotalBsr);
                c["peerPool"] = Pool(r.PeerPool); c["cleanPool"] = Pool(r.CleanPool); c["bsrLabel"] = r.BsrLabel;
                c["siblings"] = Pool(r.Siblings); c["qualSibs"] = Pool(r.QualSibs);
                if (r.SibRanked == null) c["sibRanked"] = null;
                else
                {
                    var a = new JsonArray();
                    foreach (var s in r.SibRanked) a.Add(new JsonObject { ["row"] = Ref(s.Row), ["q"] = s.Q, ["pdstr"] = Num(s.Pdstr) });
                    c["sibRanked"] = new JsonObject { ["__sib"] = a };
                }
                c["windowDailyRate"] = Num(r.WindowDailyRate); c["winStockStart"] = Num(r.WinStockStart);
                if (r.Chain == null) c["chain"] = null;
                else
                {
                    var a = new JsonArray();
                    foreach (var ch in r.Chain)
                        a.Add(new JsonObject
                        {
                            ["y"] = ch.Y, ["m"] = ch.M, ["mkey"] = ch.MKey, ["openStock"] = Num(ch.OpenStock), ["ePO"] = Num(ch.EPO), ["colPlan"] = Num(ch.ColPlan),
                            ["isCurrMonth"] = ch.IsCurrMonth, ["demandDays"] = Num(ch.DemandDays), ["totalDays"] = Num(ch.TotalDays), ["fc"] = Num(ch.Fc),
                            ["closeStock"] = Num(ch.CloseStock), ["str"] = Num(ch.Str), ["status"] = ch.Status
                        });
                    c["chain"] = a;
                }
                outArr.Add(c);
            }
            return new JsonObject { ["rows"] = rows, ["results"] = outArr };
        }

        /// <summary>JSON.stringify of a number: non-finite → null.</summary>
        private static JsonNode? Num(double? d) => d.HasValue && double.IsFinite(d.Value) ? JsonValue.Create(d.Value) : null;

        private static JsonNode RowNode(SheetRow r)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms)) r.WriteJson(w);
            return JsonNode.Parse(ms.ToArray())!;
        }

        private static JsonNode? InfoNode(GmInfo? i) => i == null ? null : new JsonObject
        {
            ["brand"] = i.Brand, ["div"] = i.Div, ["mcCd"] = i.McCd, ["mcDesc"] = i.McDesc, ["mrp"] = i.Mrp, ["flag"] = i.Flag,
            ["season"] = i.Season, ["artDesc"] = i.ArtDesc, ["vendorName"] = i.VendorName
        };

        private static JsonNode CautionNode(StrCautionDto sc)
        {
            // the JS object is {effT2, delMonth, items:[{label, str, cs}]} — display texts are recomputed on read
            var items = new JsonArray();
            foreach (var it in sc.Items)
                items.Add(new JsonObject { ["label"] = it.Label, ["str"] = Num(it.Str), ["cs"] = JsonSerializer.SerializeToNode(it.Cs) });
            return new JsonObject { ["effT2"] = Num(sc.EffT2), ["delMonth"] = sc.DelMonth, ["items"] = items };
        }

        // ── read ───────────────────────────────────────────────────────────

        public static Tool2RunPayload FromGzip(byte[] gz)
        {
            byte[] json;
            if (gz.Length >= 2 && gz[0] == 0x1f && gz[1] == 0x8b)
            {
                using var src = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
                using var ms = new MemoryStream();
                src.CopyTo(ms);
                json = ms.ToArray();
            }
            else json = gz;
            return FromJson(JsonNode.Parse(json)!.AsObject());
        }

        public static Tool2RunPayload FromJson(JsonObject o)
        {
            var p = new Tool2RunPayload();
            if (o["params"] is JsonObject po) p.Params = po.DeepClone().AsObject();
            if (o["dataVersions"] is JsonObject dv)
                foreach (var kv in dv)
                    if (kv.Value is JsonObject e)
                        p.DataVersions[kv.Key] = e.Deserialize<DatasetEntry>() ?? new DatasetEntry();
            if (o["packed"] is JsonObject packed) p.Results = Unpack(packed);
            return p;
        }

        /// <summary>Port of shared.js <c>unpackResults(p)</c>.</summary>
        public static List<Tool2LineResult> Unpack(JsonObject packed)
        {
            var rows = new List<SheetRow>();
            if (packed["rows"] is JsonArray ra)
                foreach (var n in ra)
                {
                    if (n is JsonObject) { using var d = JsonDocument.Parse(n.ToJsonString()); rows.Add(SheetRow.FromJson(d.RootElement)); }
                    else rows.Add(new SheetRow());
                }
            SheetRow RowAt(JsonNode? n) { var i = (int)(D(n) ?? -1); return i >= 0 && i < rows.Count ? rows[i] : new SheetRow(); }
            List<SheetRow>? Pool(JsonNode? n)
            {
                if (n is JsonObject po && po["__rows"] is JsonArray a) return a.Select(RowAt).ToList();
                if (n is JsonArray direct) return direct.Select(x => x is JsonObject ? RowAt(null) : RowAt(x)).ToList();
                return null;
            }
            var list = new List<Tool2LineResult>();
            if (packed["results"] is not JsonArray results) return list;
            foreach (var node in results)
            {
                if (node is not JsonObject c) continue;
                var r = new Tool2LineResult
                {
                    Idx = (int)(D(c["idx"]) ?? 0),
                    Gm = S(c["gm"]) ?? "", Colour = S(c["colour"]) ?? "", Dm = S(c["dm"]) ?? "",
                    RelQty = D(c["relQty"]) ?? 0, PoNum = S(c["poNum"]) ?? "",
                    Engine = S(c["engine"]) ?? "", Decision = S(c["decision"]) ?? "", DecisionReason = S(c["decisionReason"]) ?? "",
                    ArtStr = D(c["artSTR"]), RecQty = D(c["recQty"]) ?? 0, ExcessQty = D(c["excessQty"]) ?? 0,
                    MixPct = D(c["mixPct"]) ?? 0, OpStock = D(c["opStock"]) ?? 0,
                    Div = S(c["div"]) ?? "", McDesc = S(c["mcDesc"]) ?? "", Mrp = S(c["mrp"]) ?? "", Vendor = S(c["vendor"]) ?? "",
                    PoValue = D(c["poValue"]), PoRate = D(c["poRate"]),
                    OptionType = S(c["optionType"]), Quartile = S(c["quartile_"]), Percentile = D(c["percentile"]), Pdstr = D(c["pdstr"]),
                    PeerAvg = D(c["peerAvg"]), GapPct = D(c["gapPct"]), InBorderZone = c["inBorderZone"] is JsonValue bz && bz.GetValueKind() == JsonValueKind.True,
                    BorderTolUsed = D(c["borderTolUsed"]), Wof = D(c["wof"]), TotalBsr = D(c["totalBSR"]),
                    PeerPool = Pool(c["peerPool"]), CleanPool = Pool(c["cleanPool"]), BsrLabel = S(c["bsrLabel"]),
                    Siblings = Pool(c["siblings"]), QualSibs = Pool(c["qualSibs"]),
                    WindowDailyRate = D(c["windowDailyRate"]), WinStockStart = D(c["winStockStart"]),
                    IsErrorRow = !c.ContainsKey("strCaution") && S(c["engine"]) == "?"
                };
                if (c["info"] is JsonObject io)
                    r.Info = new GmInfo(Str(io["brand"]), Str(io["div"]), Str(io["mcCd"]), Str(io["mcDesc"]), Str(io["mrp"]), Str(io["flag"]),
                        Str(io["season"]), Str(io["artDesc"]), Str(io["vendorName"]));
                if (c["sibRanked"] is JsonObject so && so["__sib"] is JsonArray sa)
                    r.SibRanked = sa.OfType<JsonObject>().Select(x => new SibRank { Row = RowAt(x["row"]), Q = S(x["q"]) ?? "", Pdstr = D(x["pdstr"]) ?? 0 }).ToList();
                if (c["strCaution"] is JsonObject sc)
                {
                    try
                    {
                        var dto = new StrCautionDto { EffT2 = D(sc["effT2"]) ?? 0, DelMonth = S(sc["delMonth"]) ?? "" };
                        if (sc["items"] is JsonArray items)
                            foreach (var it in items.OfType<JsonObject>())
                                dto.Items.Add(new StrCautionItemDto
                                {
                                    Label = S(it["label"]) ?? "", Str = D(it["str"]) ?? 0,
                                    Cs = it["cs"] is JsonObject cs ? cs.Deserialize<CoreStrResultDto>() ?? new CoreStrResultDto() : new CoreStrResultDto()
                                });
                        r.StrCaution = dto;
                    }
                    catch (JsonException) { r.StrCaution = null; }
                }
                if (c["chain"] is JsonArray ca)
                    r.Chain = ca.OfType<JsonObject>().Select(x => new Tool2ChainStep
                    {
                        Y = (int)(D(x["y"]) ?? 0), M = (int)(D(x["m"]) ?? 0), MKey = S(x["mkey"]) ?? "", OpenStock = D(x["openStock"]) ?? 0,
                        EPO = D(x["ePO"]) ?? 0, ColPlan = D(x["colPlan"]) ?? 0, IsCurrMonth = x["isCurrMonth"] is JsonValue b && b.GetValueKind() == JsonValueKind.True,
                        DemandDays = D(x["demandDays"]) ?? 0, TotalDays = D(x["totalDays"]) ?? 0, Fc = D(x["fc"]) ?? 0, CloseStock = D(x["closeStock"]) ?? 0,
                        Str = D(x["str"]), Status = S(x["status"])
                    }).ToList();
                list.Add(r);
            }
            return list;
        }

        private static double? D(JsonNode? n)
        {
            if (n is not JsonValue v) return null;
            var o = LedgerHelpers.JsValue(v);
            return o switch { double d => d, string s => Js.Number(s), bool b => b ? 1 : 0, _ => null };
        }

        private static string? S(JsonNode? n)
        {
            if (n is not JsonValue v) return null;
            var o = LedgerHelpers.JsValue(v);
            return o == null ? null : Js.ToJsString(o);
        }

        private static string Str(JsonNode? n) => S(n) ?? "";
    }
}
