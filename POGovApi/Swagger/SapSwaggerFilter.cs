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
