using System.Text.Json.Serialization;

namespace Application.Interfaces.V1
{
    /// <summary>
    /// The company HRMS employee API, called by POGovApi only (the portal never talks to HRMS). Two calls, as in QPS:
    /// <list type="bullet">
    /// <item><see cref="VerifyPasswordAsync"/> — API_MASTER task HRP_GET_EMP_LOGIN_API: checks an employee's HRMS password.
    /// POGovApi calls it only for employees the admin registered in this tool.</item>
    /// <item><see cref="GetEmployeeAsync"/> — API_MASTER task HRP_GET_EMP_DETAIL_CODE: an employee's details by code, used by
    /// the admin when registering a user.</item>
    /// </list>
    /// Never throws for HTTP, network or HRMS errors: they come back as <see cref="HrmsStatus.Unavailable"/>.
    /// </summary>
    public interface IHrmsClient
    {
        Task<HrmsResult> VerifyPasswordAsync(string empCode, string password, CancellationToken ct = default);
        Task<HrmsResult> GetEmployeeAsync(string empCode, CancellationToken ct = default);
    }

    public enum HrmsStatus
    {
        /// <summary>Password right (login) / employee found (lookup); <see cref="HrmsResult.Employee"/> is set.</summary>
        Ok,
        /// <summary>Wrong password, or HRMS answered about another employee code.</summary>
        Invalid,
        /// <summary>No such employee (lookup).</summary>
        NotFound,
        /// <summary>HRMS locked the account after too many wrong passwords (HRMS responseCode 104).</summary>
        LockedInHrms,
        /// <summary>No answer: HRMS not configured, down, time-out, unreadable answer. Not the user's fault.</summary>
        Unavailable
    }

    public sealed record HrmsResult(HrmsStatus Status, HrmsEmployee? Employee = null, string? Message = null)
    {
        public static HrmsResult Unavailable(string message) => new(HrmsStatus.Unavailable, null, message);
    }

    /// <summary>One employee of the HRMS answer (QPS Domain.Entities.Modals.Employee; same JSON names).</summary>
    public sealed class HrmsEmployee
    {
        [JsonPropertyName("empCode")] public string? EmpCode { get; set; }
        [JsonPropertyName("empName")] public string? EmpName { get; set; }
        [JsonPropertyName("empLocation")] public string? EmpLocation { get; set; }
        [JsonPropertyName("empLocCode")] public string? EmpLocCode { get; set; }
        [JsonPropertyName("empDeptName")] public string? EmpDeptName { get; set; }
        [JsonPropertyName("empDesig")] public string? EmpDesig { get; set; }
        [JsonPropertyName("empContactNo")] public string? EmpContactNo { get; set; }
        [JsonPropertyName("empMailID")] public string? EmpMailId { get; set; }
        [JsonPropertyName("empRptMng")] public string? EmpRptMng { get; set; }
        [JsonPropertyName("isResign")] public bool IsResign { get; set; }
        [JsonPropertyName("empGrade")] public int EmpGrade { get; set; }
        [JsonPropertyName("empGradeDesc")] public string? EmpGradeDesc { get; set; }
        /// <summary>true = active in HRMS.</summary>
        [JsonPropertyName("empStatus")] public bool EmpStatus { get; set; }

        /// <summary>Can work in the tool: active in HRMS and not resigned.</summary>
        [JsonIgnore] public bool IsWorking => EmpStatus && !IsResign;
    }

    /// <summary>API_MASTER task names of the HRMS calls (rows with Api_Scope = SERVER: never sent to the portal).</summary>
    public static class HrmsTasks
    {
        public const string Login = "HRP_GET_EMP_LOGIN_API";
        public const string EmployeeByCode = "HRP_GET_EMP_DETAIL_CODE";
    }
}
