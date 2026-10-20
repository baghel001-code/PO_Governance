namespace Domain.Entities.Base
{
    /// <summary>Base of the POGovApi request bodies (QPS <c>Domain/Entities/Base/BaseRequest</c>).</summary>
    public abstract class BaseRequest
    {
        /// <summary>QPS field; not used by PO Governance (the API takes the user from the session token).</summary>
        public int Inserted_Updated_By { get; set; } = 0;
    }
}
