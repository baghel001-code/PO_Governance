using DecisionEngine.Sap;
using Domain.Entities.Sap;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// The mandatory fields and enums of the SAP contract (po-governance-sap-api.yaml). A failing /decisions request is
    /// answered 400 VALIDATION_FAILED with the first problem as message (e.g. "purchase_order.items[0].order_qty must be &gt; 0");
    /// a failing event is parked with it.
    /// </summary>
    public static class SapRequestValidator
    {
        /// <summary>Null when the decision request is valid, else the first problem.</summary>
        public static string? Validate(SapDecisionRequest? r)
        {
            if (r == null) return "The body must be a JSON object";
            if (string.IsNullOrWhiteSpace(r.RequestId)) return "request_id is required";
            if (r.RequestId.Length > 80) return "request_id must be at most 80 characters";
            if (string.IsNullOrWhiteSpace(r.Mode)) return "mode is required";
            if (!SapEnums.Modes.Contains(r.Mode)) return "mode must be one of " + string.Join(", ", SapEnums.Modes);
            if (string.IsNullOrWhiteSpace(r.SourceSystem)) return "source_system is required";
            if (string.IsNullOrWhiteSpace(r.OtbCheck)) return "otb_check is required";
            if (!SapEnums.OtbCheck.Contains(r.OtbCheck)) return "otb_check must be PASSED";
            if (string.IsNullOrWhiteSpace(r.DepthCheck)) return "depth_check is required";
            if (!SapEnums.DepthCheck.Contains(r.DepthCheck)) return "depth_check must be one of " + string.Join(", ", SapEnums.DepthCheck);
            if (r.RequestedAt != null && !SapJson.TryParseTimestamp(r.RequestedAt, out _))
                return "requested_at must be an ISO 8601 timestamp with offset, e.g. 2026-10-01T11:50:00+05:30";
            var po = r.PurchaseOrder;
            if (po == null) return "purchase_order is required";
            if (string.IsNullOrWhiteSpace(po.PoNumber)) return "purchase_order.po_number is required";
            if (string.IsNullOrWhiteSpace(po.BuyerUser)) return "purchase_order.buyer_user is required";
            if (po.Items == null || po.Items.Count == 0) return "purchase_order.items must contain at least one item";
            var seen = new HashSet<int>();
            for (var i = 0; i < po.Items.Count; i++)
            {
                var it = po.Items[i];
                var p = "purchase_order.items[" + i + "]";
                if (it == null) return p + " must be an object";
                if (it.PoItem == null) return p + ".po_item is required";
                if (!seen.Add(it.PoItem.Value)) return p + ".po_item " + it.PoItem + " appears more than once";
                if (string.IsNullOrWhiteSpace(it.Article)) return p + ".article is required";
                if (it.OrderQty == null) return p + ".order_qty is required";
                if (!(it.OrderQty > 0) || !double.IsFinite(it.OrderQty.Value)) return p + ".order_qty must be > 0";
                if (string.IsNullOrWhiteSpace(it.Uom)) return p + ".uom is required";
                if (it.DeliveryDate != null && !SapInputNormalizer.TryParseDate(it.DeliveryDate, out _))
                    return p + ".delivery_date must be a date YYYY-MM-DD";
            }
            return null;
        }

        /// <summary>Null when the event is valid, else the first problem (the event is then parked).</summary>
        public static string? Validate(SapEvent e)
        {
            if (string.IsNullOrWhiteSpace(e.MsgId)) return "msg_id is required";
            if (e.MsgId.Length > 80) return "msg_id must be at most 80 characters";
            if (e.Seq == null) return "seq is required";
            if (string.IsNullOrWhiteSpace(e.EventType)) return "event_type is required";
            if (!SapEnums.EventTypes.Contains(e.EventType)) return "event_type " + e.EventType + " is not one of " + string.Join(", ", SapEnums.EventTypes);
            if (string.IsNullOrWhiteSpace(e.OccurredAt)) return "occurred_at is required";
            if (!SapJson.TryParseTimestamp(e.OccurredAt, out _)) return "occurred_at must be an ISO 8601 timestamp with offset";
            var filled = (e.PurchaseOrder != null ? 1 : 0) + (e.GoodsMovement != null ? 1 : 0) + (e.GovernanceOutcome != null ? 1 : 0);
            if (filled > 1) return "exactly one of purchase_order, goods_movement or governance_outcome must be filled";

            if (SapEnums.PoEventTypes.Contains(e.EventType))
            {
                var po = e.PurchaseOrder;
                if (po == null) return e.EventType + " needs purchase_order (full PO state)";
                if (string.IsNullOrWhiteSpace(po.PoNumber)) return "purchase_order.po_number is required";
                if (po.ReleaseStatus != null && !SapEnums.ReleaseStatus.Contains(po.ReleaseStatus))
                    return "purchase_order.release_status must be one of " + string.Join(", ", SapEnums.ReleaseStatus);
                if (po.CreatedOn != null && !SapInputNormalizer.TryParseDate(po.CreatedOn, out _)) return "purchase_order.created_on must be a date YYYY-MM-DD";
                if (po.Items == null) return "purchase_order.items is required";
                for (var i = 0; i < po.Items.Count; i++)
                {
                    var it = po.Items[i];
                    var p = "purchase_order.items[" + i + "]";
                    if (it == null) return p + " must be an object";
                    if (it.PoItem == null) return p + ".po_item is required";
                    if (string.IsNullOrWhiteSpace(it.Article)) return p + ".article is required";
                    if (it.OrderQty == null) return p + ".order_qty is required";
                    if (it.OrderQty < 0) return p + ".order_qty must not be negative";
                    if (string.IsNullOrWhiteSpace(it.Uom)) return p + ".uom is required";
                    if (it.DeliveryDate != null && !SapInputNormalizer.TryParseDate(it.DeliveryDate, out _)) return p + ".delivery_date must be a date YYYY-MM-DD";
                }
            }
            else if (SapEnums.GoodsMovementEventTypes.Contains(e.EventType))
            {
                var g = e.GoodsMovement;
                if (g == null) return e.EventType + " needs goods_movement";
                if (string.IsNullOrWhiteSpace(g.MaterialDoc)) return "goods_movement.material_doc is required";
                if (g.DocYear == null) return "goods_movement.doc_year is required";
                if (g.DocItem == null) return "goods_movement.doc_item is required";
                if (string.IsNullOrWhiteSpace(g.MovementType)) return "goods_movement.movement_type is required";
                if (string.IsNullOrWhiteSpace(g.Article)) return "goods_movement.article is required";
                if (g.Quantity == null) return "goods_movement.quantity is required";
                if (g.Quantity < 0) return "goods_movement.quantity must be positive (the direction comes from debit_credit)";
                if (string.IsNullOrWhiteSpace(g.Uom)) return "goods_movement.uom is required";
                if (g.DebitCredit != null && !SapEnums.DebitCredit.Contains(g.DebitCredit)) return "goods_movement.debit_credit must be S or H";
                if (g.PostingDate != null && !SapInputNormalizer.TryParseDate(g.PostingDate, out _)) return "goods_movement.posting_date must be a date YYYY-MM-DD";
            }
            else
            {
                var o = e.GovernanceOutcome;
                if (o == null) return "GOV_OUTCOME needs governance_outcome";
                if (string.IsNullOrWhiteSpace(o.DecisionId)) return "governance_outcome.decision_id is required";
                if (string.IsNullOrWhiteSpace(o.PoNumber)) return "governance_outcome.po_number is required";
                if (string.IsNullOrWhiteSpace(o.Outcome)) return "governance_outcome.outcome is required";
                if (!SapEnums.Outcomes.Contains(o.Outcome)) return "governance_outcome.outcome must be one of " + string.Join(", ", SapEnums.Outcomes);
                if (string.IsNullOrWhiteSpace(o.ByUser)) return "governance_outcome.by_user is required";
                if (o.ByRole != null && !SapEnums.ByRoles.Contains(o.ByRole)) return "governance_outcome.by_role must be one of " + string.Join(", ", SapEnums.ByRoles);
                if (string.IsNullOrWhiteSpace(o.At)) return "governance_outcome.at is required";
                if (!SapJson.TryParseTimestamp(o.At, out _)) return "governance_outcome.at must be an ISO 8601 timestamp with offset";
                if (o.Note != null && o.Note.Length > 500) return "governance_outcome.note must be at most 500 characters";
            }
            return null;
        }
    }
}
