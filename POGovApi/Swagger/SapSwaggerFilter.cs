using Domain.Entities.Sap;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace POGovApi.Swagger
{
    /// <summary>
    /// Swagger for the SAP endpoints (<c>/api/sap/v1/*</c>). Their actions read the raw body (it is hashed for idempotency
    /// and parsed by the service, which answers INVALID_JSON itself), so Swagger cannot see a body parameter: this filter
    /// declares the request body (schema + examples) for POST decisions / events and replaces the portal's Bearer +
    /// session requirement with HTTP Basic (the SAP technical user). Documentation only — request handling is unchanged.
    /// </summary>
    public sealed class SapSwaggerFilter : IOperationFilter
    {
        public const string SchemeName = "SapBasic";

        private const string DecisionMinimal = """
            { "purchase_order": { "items": [
                { "generic_article": "000000001101000101", "colour": "WHT", "order_qty": 50000, "delivery_date": "2026-11-15" } ] } }
            """;

        private const string DecisionFull = """
            {
              "request_id": "WI000004711-4500099001-V1", "mode": "SHADOW", "source_system": "ECQ", "client": "300",
              "otb_check": "PASSED", "depth_check": "PASSED", "previous_decision_id": null, "requested_at": "2026-10-21T11:50:00+05:30",
              "purchase_order": {
                "po_number": "4500099001", "doc_type": "NB", "vendor": "0000201345", "purch_org": "1000", "purch_group": "B07",
                "buyer_user": "BUYER07", "buyer_email": "ravi.k@vishalwholesale.co.in", "division": "MENS", "currency": "INR",
                "items": [
                  { "po_item": 10, "article": "000000001101000101", "generic_article": "000000001101000101", "colour": "WHT",
                    "merch_category": "MC1021", "season": "AW26", "site": "DC01", "order_qty": 50000, "uom": "PC",
                    "net_value": 418800.00, "delivery_date": "2026-11-15" } ] }
            }
            """;

        private const string EventsExample = """
            {
              "source_system": "ECQ", "client": "300", "queue": "ZPOGOV_EVENTS", "sent_at": "2026-10-21T11:42:08+05:30",
              "events": [
                { "msg_id": "ECQ300-GR-5000123456-2026-0001", "seq": 45121, "event_type": "GR_POSTED",
                  "occurred_at": "2026-10-21T11:42:07+05:30", "sap_user": "DCUSER01",
                  "goods_movement": { "material_doc": "5000123456", "doc_year": 2026, "doc_item": 1, "movement_type": "101",
                    "debit_credit": "S", "posting_date": "2026-10-21", "article": "000000001101000101",
                    "generic_article": "000000001101000101", "colour": "WHT", "site": "DC01", "quantity": 240, "uom": "PC",
                    "po_number": "4500098765", "po_item": 10 } } ]
            }
            """;

        private const string DocumentExample = """
            { "request_id": "WI000004711-4500099001-V1", "document_no": "5000123456", "document_type": "MRN",
              "po_number": "4500099001", "document_date": "2026-10-26", "replace": false, "note": "" }
            """;

        private const string MraNew = """
            { "mra_no": "0000900201", "level": "First Level", "generic_article": "000000001112045675", "colour": "LGY", "qty": 3000,
              "delivery_date": "2026-12-20", "msg_id": "SAP-77812", "occurred_at": "2026-11-05T10:12:00+05:30" }
            """;

        private const string MraBatch = """
            { "messages": [
                { "mra_no": "900201", "level": "7", "msg_id": "SAP-77890", "occurred_at": "2026-11-05T11:02:00+05:30" },
                { "mra_no": "900202", "level": "final", "po_no": "4500099001", "po_status": "Released", "msg_id": "SAP-77891" },
                { "mra_no": "900203", "level": "1", "decision": "Auto Allow", "decision_reason": "Within Max Permissible",
                  "decision_id": "D-20261105-000089", "max_permissible": 6200, "msg_id": "SAP-77892" } ] }
            """;

        private const string PoExample = """
            { "po_no": "4500099001", "po_status": "Released", "mra_no": "900202", "qty": 2800, "generic_article": "000000001112045675",
              "colour": "LGY", "delivery_date": "2026-12-20", "msg_id": "SAP-PO-5512", "occurred_at": "2026-11-05T12:00:00+05:30" }
            """;

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath ?? "";
            if (!path.StartsWith("api/sap/", StringComparison.OrdinalIgnoreCase)) return;

            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new() { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = SchemeName } }] = Array.Empty<string>() }
            };

            if (path.EndsWith("/decisions", StringComparison.OrdinalIgnoreCase))
                operation.RequestBody = Body(context, typeof(SapDecisionRequest),
                    "PO to decide. Mandatory per item: generic_article, colour, order_qty, delivery_date; everything else is optional.",
                    ("minimal", "Only the four mandatory fields", DecisionMinimal), ("full", "Every field SAP may send", DecisionFull));
            else if (path.EndsWith("/documents", StringComparison.OrdinalIgnoreCase))
                operation.RequestBody = Body(context, typeof(SapDocumentRequest),
                    "Final SAP document (MRN) of a decided request_id. Mandatory: request_id, document_no; replace = true corrects a stored one.",
                    ("mrn", "MRN of a PO", DocumentExample));
            else if (path.EndsWith("/mra-events", StringComparison.OrdinalIgnoreCase))
                operation.RequestBody = Body(context, typeof(object),
                    "One MRA change or {\"messages\": [...]} (max 5,000). Required: mra_no, level (1-9, X or the description); a new MRA also " +
                    "generic_article, colour, qty, delivery_date. Optional: po_no, po_status, occurred_at, msg_id, decision fields.",
                    ("new", "A new MRA (first level)", MraNew), ("batch", "Self reject, final with released PO, decision", MraBatch));
            else if (path.EndsWith("/po-events", StringComparison.OrdinalIgnoreCase))
                operation.RequestBody = Body(context, typeof(object),
                    "One PO change or {\"messages\": [...]}. Required: po_no, po_status (Released / Unreleased / 1 / 2 / 3 / Closed); mra_no for a PO made from an MRA.",
                    ("released", "PO released", PoExample));
            else if (path.EndsWith("/events", StringComparison.OrdinalIgnoreCase))
                operation.RequestBody = Body(context, typeof(SapEventBatch), "1-500 events in ascending seq order.",
                    ("goods_receipt", "One goods receipt (GR_POSTED)", EventsExample));
        }

        private static OpenApiRequestBody Body(OperationFilterContext context, Type type, string description,
                                               params (string Key, string Summary, string Json)[] examples)
        {
            var media = new OpenApiMediaType { Schema = context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository) };
            foreach (var (key, summary, json) in examples)
                media.Examples[key] = new OpenApiExample { Summary = summary, Value = new OpenApiString(json, false, true) };
            return new OpenApiRequestBody { Required = true, Description = description, Content = { ["application/json"] = media } };
        }
    }
}
