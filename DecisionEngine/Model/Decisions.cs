namespace DecisionEngine.Model
{
    /// <summary>Decision labels produced by the engines (exact strings — stored in request history).</summary>
    public static class Decisions
    {
        // Tool 1 (raise a new PO)
        public const string AutoAllow = "Auto Allow";
        public const string Allow = "Allow";
        public const string ReduceRecommended = "Recommend Reduced Qty";
        public const string Hold = "Hold — Don't Raise";
        public const string Block = "Block";
        public const string WatchList = "Watch List";
        public const string CannotValidate = "Cannot Validate";

        // Tool 2 (released PO assessment)
        public const string Retain = "Retain";
        public const string ReduceQty = "Reduce Qty";
        public const string Cancel = "Cancel";
    }
}
