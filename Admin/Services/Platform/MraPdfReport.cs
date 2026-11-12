using System.Globalization;
using System.Text.Json.Nodes;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;
using PDFKit;

namespace Admin.Services.Platform
{
    /// <summary>
    /// The MRA PDF ("Download PDF" in the MRA Queues pop-up and on the SAP verdict-url page): the same content as the
    /// pop-up, drawn with the company PDFKit (QPS inspection report design: letterhead, gradient section bars, tables).
    /// </summary>
    public static class MraPdfReport
    {
        private static readonly string[] Mn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

        public static string FileName(string? mraNo, string? decisionId = null) =>
            !string.IsNullOrEmpty(mraNo) ? "MRA_" + mraNo + ".pdf" : "MRA_verdict_" + (decisionId ?? "decision") + ".pdf";

        /// <param name="x">The MRA; null when SAP has not linked the MRA number yet (decision and working only, with <paramref name="note"/>).</param>
        public static byte[] Build(MraRowDto? x, MraDecisionDetail? detail, IReadOnlyDictionary<string, string>? owners, string? logoPath, DateTimeOffset now,
                                   string? note = null)
        {
            var td = detail?.Decision ?? x?.ToolDecision;
            var w = detail?.Working;
            var mraNo = x?.No ?? (string.IsNullOrEmpty(detail?.MraNo) ? null : detail!.MraNo);
            using var pdf = new PdfBuilder(new PageSettings { MarginTop = 32, MarginBottom = 50 });
            pdf.AddLetterhead(new LetterheadInfo
            {
                CompanyName = "Vishal Mega Mart",
                Tagline = "PO Governance",
                LogoPath = logoPath != null && File.Exists(logoPath) ? logoPath : null,
                DateOfInspection = "Printed " + When(now),
                ReferenceNumber = mraNo != null ? "MRA " + mraNo : td?.DecisionId,
                DocumentTitle = "MRA – Decision, Working and History",
                FooterInfo = "VMM PO Governance · " + (mraNo != null ? "MRA " + mraNo : "decision " + td?.DecisionId) +
                             (x != null ? " · " + x.M.Ga + " " + x.M.Colour : td != null ? " · " + td.Gm + " " + td.Colour : "")
            });
            if (x != null)
            {
                pdf.AddText("MRA " + x.No + " · " + x.M.Ga + " " + x.M.Colour, TextStyle.SubHeading);
                pdf.AddText((x.C.Info?.Desc ?? x.M.GaDesc ?? "") + " · " + Nf(x.M.Qty) + " pcs · " + MraCapacity.DayLabel(x.M.Dd), TextStyle.Small);
            }
            else
            {
                pdf.AddText("MRA " + (mraNo ?? "not linked yet") + (td != null ? " · " + td.Gm + " " + td.Colour : ""), TextStyle.SubHeading);
                if (td != null) pdf.AddText("PO " + (td.PoNumber ?? "—") + " · " + NfD(td.OrderQty) + " pcs checked · delivery " + MonL(td.DeliveryMonth), TextStyle.Small);
            }
            if (!string.IsNullOrEmpty(note)) pdf.AddText(note, TextStyle.Small);

            // decision
            pdf.AddSectionBar("DECISION (THE TOOL'S CHECK OF THIS PO)");
            if (td != null)
            {
                pdf.AddChip(td.Decision, Chip(MraCapacity.DecisionGroup(td.Decision)), TextAlign.Left, TextStyle.SubHeading);
                pdf.AddSpacing(4);
                if (!string.IsNullOrEmpty(td.Reason)) pdf.AddText(td.Reason);
                KV(pdf, "Decision ID", td.DecisionId);
                KV(pdf, "Decided at", When(td.DecidedAt) + (td.Mode != null ? " · " + td.Mode : ""));
                if (!string.IsNullOrEmpty(td.PoNumber)) KV(pdf, "PO checked", td.PoNumber);
                if (!string.IsNullOrEmpty(td.RequestId)) KV(pdf, "Request ID", td.RequestId);
                if (!string.IsNullOrEmpty(td.DocumentNo)) KV(pdf, "Document (MRN) from SAP", td.DocumentNo);
                KV(pdf, "Qty checked", NfD(td.OrderQty) + (td.Verdict != null ? " · SAP verdict " + td.Verdict : ""));
                if (td.MaxPermissible != null) KV(pdf, "Max Permissible at the decision", NfD(td.MaxPermissible));
                KV(pdf, "Linked by", td.MatchedBy);
                if (td.ViewUrl != null) { pdf.AddSpacing(4); pdf.AddHyperlink("Open the PO verdict page", td.ViewUrl); }
            }
            else
            {
                pdf.AddText("No decision found for this MRA: SAP has not sent MRA " + mraNo + " as document_no to POST /documents for a checked PO.");
            }

            // working saved with the decision
            if (w != null) Working(pdf, w);
            else if (td != null) pdf.AddText("No working was saved with this decision (decisions before 7 Nov 2026 have none).", TextStyle.Small);

            if (x == null)
            {
                pdf.WithPageNumbers();
                return pdf.ToBytes();
            }
            var m = x.M; var c = x.C;

            // where it is now
            pdf.AddSpacing(6);
            pdf.AddSectionBar("WHERE IT IS NOW");
            KV(pdf, "Level", m.Level + " " + Lvl(m.Level));
            KV(pdf, "Waiting with", c.Queue.Length > 0 ? c.Queue + (c.Owner.Length > 0 && c.Owner != c.Queue ? " – " + c.Owner : "") : "—");
            KV(pdf, "Capacity", (c.Counted ? "Counted" : "Not counted") + " – " + c.Why);
            KV(pdf, "Qty", Nf(m.Qty) + (m.FileQty != null && m.FileQty != m.Qty ? " (morning file " + Nf(m.FileQty) + ")" : ""));
            KV(pdf, "Delivery", MraCapacity.DayLabel(m.Dd) + " · counts in " + MonL(c.Mon));
            if (!string.IsNullOrEmpty(m.PoNo)) KV(pdf, "PO", m.PoNo + " · " + PoStatusText(m.PoStatus) + (m.PoSrc == "live" ? " · live" : ""));
            KV(pdf, "Group", Group(c.Info));
            if (c.DhEmail.Length > 0) KV(pdf, "Division Head", Who(c.DhEmail, owners));
            if (c.ChEmail.Length > 0) KV(pdf, "Category Head", Who(c.ChEmail, owners));
            if (!string.IsNullOrEmpty(m.CreatedBy) || !string.IsNullOrEmpty(m.CreatedById))
                KV(pdf, "Created by", string.Join(" · ", new[] { m.CreatedBy, m.CreatedById }.Where(s => !string.IsNullOrEmpty(s))));
            if (!string.IsNullOrEmpty(m.Inspiration)) KV(pdf, "Inspiration", m.Inspiration);
            if (m.Lines > 0) KV(pdf, "Item lines", m.Lines.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(m.ActionBy)) KV(pdf, "Last change by (SAP)", m.ActionBy);
            if (!string.IsNullOrEmpty(m.Remark)) KV(pdf, "Last remark (SAP)", m.Remark);

            // level history
            pdf.AddSpacing(6);
            pdf.AddSectionBar("LEVEL CHANGES SINCE THE MORNING FILE");
            if (m.History.Count == 0) pdf.AddText("None – level from the morning file.", TextStyle.Small);
            else
                pdf.AddTable(new[] { Col("Time", 1.6), Col("Change", 1.9), Col("Qty", 0.7, TextAlign.Right), Col("By (SAP)", 1.4), Col("Remark", 1.3) },
                    m.History.Select(h => (IReadOnlyList<string>)new[]
                    {
                        When(h.At), (h.From != null ? "Level " + h.From + " → " : "created at ") + "Level " + h.To + " " + Lvl(h.To), Nf(h.Qty), h.By ?? "—", h.Remark ?? ""
                    }).ToList());

            pdf.WithPageNumbers();
            return pdf.ToBytes();
        }

        private static void Working(PdfBuilder pdf, JsonObject w)
        {
            pdf.AddSpacing(6);
            pdf.AddSectionBar("WORKING SAVED WITH THE DECISION");
            KV(pdf, "Decision", Str(w["decision"]) ?? "—");
            KV(pdf, "Article · colour · month", string.Join(" · ", new[] { Str(w["article"]), Str(w["colour"]), Str(w["deliveryMonth"]) }.Where(s => !string.IsNullOrEmpty(s))));
            KV(pdf, "Qty asked", NfD(Dbl(w["askedQty"])));
            if (Dbl(w["stockToday"]) is { } st) KV(pdf, "Stock today", NfD(st));
            if (Dbl(w["colourShare"]) is { } share) KV(pdf, "Colour share", (share * (share <= 1 ? 100 : 1)).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (Str(w["windowStart"]) is { } ws) KV(pdf, "Window", "from " + ws + ", " + Dbl(w["windowDays"])?.ToString("0", CultureInfo.InvariantCulture) + " days");
            if (Dbl(w["windowDailyRate"]) is { } rate) KV(pdf, "Daily sale in the window", NfD(rate) + "/day");
            if (Dbl(w["limitDays"]) is { } ld) KV(pdf, "Limit (days)", ld.ToString("0.00", CultureInfo.InvariantCulture));
            if (Dbl(w["stockNeededAtWindowStart"]) is { } need) KV(pdf, "Stock needed at window start", NfD(need));
            if (Dbl(w["stockAtWindowStart"]) is { } at) KV(pdf, "Stock at window start", NfD(at));
            if (w.ContainsKey("maxPermissible")) KV(pdf, "Max Permissible", NfD(Dbl(w["maxPermissible"])));

            if (w["monthByMonth"] is JsonArray mbm && mbm.Count > 0)
            {
                pdf.AddSpacing(6);
                pdf.AddSectionBar("MONTH BY MONTH");
                pdf.AddTable(new[] { Col("Month", 1), Col("Open", 1, TextAlign.Right), Col("Incoming", 1, TextAlign.Right), Col("Asked", 1, TextAlign.Right),
                                     Col("Plan sale", 1, TextAlign.Right), Col("Close", 1, TextAlign.Right) },
                    mbm.OfType<JsonObject>().Select(r => (IReadOnlyList<string>)new[]
                    {
                        Str(r["month"]) ?? "", NfD(Dbl(r["open"])), NfD(Dbl(r["incoming"])), r.ContainsKey("asked") ? NfD(Dbl(r["asked"])) : "—",
                        NfD(Dbl(r["planSale"])), NfD(Dbl(r["close"]))
                    }).ToList());
            }
            if (w["incomingParts"] is JsonObject ip)
            {
                var parts = new[] { "releasedPOs", "openMRAs", "approvedInToolToday" }.Select(k => ip[k] as JsonObject).ToArray();
                var months = parts.Where(p => p != null).SelectMany(p => p!.Select(kv => kv.Key)).Distinct().OrderBy(MonOrd).ToList();
                if (months.Count > 0)
                {
                    pdf.AddSectionBar("INCOMING BY MONTH");
                    pdf.AddTable(new[] { Col("Month", 1), Col("Released POs", 1, TextAlign.Right), Col("Open MRAs", 1, TextAlign.Right), Col("Approved in tool today", 1.4, TextAlign.Right) },
                        months.Select(k => (IReadOnlyList<string>)new[] { MonL(k) }.Concat(parts.Select(p => NfD(Dbl(p?[k]) ?? 0))).ToArray()).ToList());
                }
            }
            if (w["openMRAsCounted"] is JsonArray om && om.Count > 0)
                pdf.AddText("Open MRAs counted then: " + string.Join(" · ", om.OfType<JsonObject>().Take(10).Select(o => Str(o["type"]) + " " + Str(o["no"]) + " " + NfD(Dbl(o["qty"])))), TextStyle.Small);
            if (w["caution"] is JsonObject) pdf.AddText("A later-PO caution was shown with this decision.", TextStyle.Small);
        }

        /// <summary>Label + value row (PDFKit wrapped key-value) with a label column wide enough for the longest label.</summary>
        private static void KV(PdfBuilder pdf, string key, string? value) => pdf.AddWrappedKeyValue(key, string.IsNullOrEmpty(value) ? "—" : value, 175);

        private static TableColumn Col(string h, double weight, TextAlign align = TextAlign.Left) => new() { Header = h, Weight = weight, Align = align };
        private static ChipColor Chip(string group) => group switch
        {
            "Allow" => ChipColor.Success, "Reduce" or "Watch List" => ChipColor.Warning, "Hold" or "Block" => ChipColor.Error, _ => ChipColor.Default
        };
        private static string When(DateTimeOffset t) => t.ToOffset(Ist).ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture);
        private static string MonL(string? mk) { var p = (mk ?? "").Split('.'); return p.Length == 2 && int.TryParse(p[0], out var mo) && mo is >= 1 and <= 12 ? Mn[mo - 1] + " " + p[1] : mk ?? ""; }
        private static int MonOrd(string? mk) { var p = (mk ?? "").Split('.'); return p.Length == 2 && int.TryParse(p[0], out var mo) && int.TryParse(p[1], out var y) ? y * 12 + mo : 0; }
        private static string Nf(long? v) => v == null ? "—" : DecisionEngine.Common.Js.Fmt(v.Value);
        private static string NfD(double? v) => v == null ? "—" : DecisionEngine.Common.Js.Fmt(v.Value);
        private static string Who(string e, IReadOnlyDictionary<string, string>? owners) =>
            e.Contains("vacant", StringComparison.OrdinalIgnoreCase) ? "" : owners != null && owners.TryGetValue(e, out var n) ? n : e.Split('@')[0];
        private static string Lvl(string? l) => l != null && MraNorm.Levels.TryGetValue(l, out var d) ? d : "";
        private static string Group(MraArticleInfo? i) => i == null ? "(not in Article Master)" : string.Join(" · ", i.Brand, string.IsNullOrEmpty(i.McDesc) ? i.McCd : i.McDesc, "Rs " + i.Mrp, i.Flag);
        private static string PoStatusText(string? st) => st switch { "3" => "Released", "1" or "2" => "Unreleased", "C" => "Closed", "mixed" => "Mixed", _ => st ?? "" };
        private static string? Str(JsonNode? n) => n == null ? null : n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n.ToJsonString();
        private static double? Dbl(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
    }
}
