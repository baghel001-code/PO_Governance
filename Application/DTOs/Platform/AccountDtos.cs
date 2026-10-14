using System.Text.Json.Serialization;
using Domain.Entities.Store;

namespace Application.DTOs.Platform
{
    public sealed class LoginRequest
    {
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("password")] public string? Password { get; set; }
    }

    public sealed class LoginResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        /// <summary>Session token: send as "Authorization: Bearer &lt;token&gt;".</summary>
        [JsonPropertyName("token")] public string Token { get; set; } = "";
        [JsonPropertyName("mustChange")] public bool MustChange { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("user")] public UserPublicDto? User { get; set; }
        /// <summary>Idle timeout of the session in seconds (server.py cookie Max-Age).</summary>
        [JsonPropertyName("expiresIn")] public int ExpiresIn { get; set; }
    }

    public sealed class ChangePasswordRequest
    {
        [JsonPropertyName("old")] public string? Old { get; set; }
        [JsonPropertyName("new")] public string? New { get; set; }
    }

    /// <summary>accounts.py public(u) (+ server.py me_public / users list extras).</summary>
    public sealed class UserPublicDto
    {
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("roles")] public List<string> Roles { get; set; } = new();
        [JsonPropertyName("active")] public bool Active { get; set; }
        [JsonPropertyName("mustChange")] public bool MustChange { get; set; }
        [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
        [JsonPropertyName("createdBy")] public string? CreatedBy { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("lastLogin")] public string? LastLogin { get; set; }
        [JsonPropertyName("lockedUntil")] public double LockedUntil { get; set; }
        [JsonPropertyName("locked")] public bool Locked { get; set; }

        [JsonPropertyName("mcCount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? McCount { get; set; }
        [JsonPropertyName("roleLabels"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? RoleLabels { get; set; }
        [JsonPropertyName("restricted"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Restricted { get; set; }
        [JsonPropertyName("scopeKeys"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? ScopeKeys { get; set; }
    }

    public sealed class MeResponse
    {
        [JsonPropertyName("user")] public UserPublicDto? User { get; set; }
        [JsonPropertyName("passwordMinLength")] public int PasswordMinLength { get; set; }
    }

    public sealed record RoleDto([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("label")] string Label);

    public sealed class UsersListResponse
    {
        [JsonPropertyName("users")] public List<UserPublicDto> Users { get; set; } = new();
        [JsonPropertyName("roles")] public List<RoleDto> Roles { get; set; } = new();
        [JsonPropertyName("defaultPassword")] public string DefaultPassword { get; set; } = "";
    }

    public sealed class AddUserRequest
    {
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("roles")] public List<string>? Roles { get; set; }
    }

    public sealed class UserResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("user")] public UserPublicDto? User { get; set; }
    }

    public sealed class BulkUsersRequest
    {
        [JsonPropertyName("users")] public List<AddUserRequest>? Users { get; set; }
    }

    public sealed class BulkUsersResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("added")] public List<string?> Added { get; set; } = new();
        [JsonPropertyName("errors")] public List<string> Errors { get; set; } = new();
    }

    public sealed class ScrMappingRequest
    {
        [JsonPropertyName("mapping")] public ScrMapping? Mapping { get; set; }
    }

    /// <summary>accounts.py analyse_scr: one column holding emails.</summary>
    public sealed class ScrColumnInfo
    {
        [JsonPropertyName("column")] public string Column { get; set; } = "";
        /// <summary>"buyer" | "cathead" | "skip".</summary>
        [JsonPropertyName("guess")] public string Guess { get; set; } = "skip";
        [JsonPropertyName("nameColumn")] public string? NameColumn { get; set; }
        [JsonPropertyName("distinctEmails")] public int DistinctEmails { get; set; }
        [JsonPropertyName("example")] public string Example { get; set; } = "";
    }

    public sealed class ScrAnalysis
    {
        [JsonPropertyName("columns")] public List<ScrColumnInfo> Columns { get; set; } = new();
        [JsonPropertyName("brandCol")] public string? BrandCol { get; set; }
        [JsonPropertyName("mcCol")] public string? McCol { get; set; }
        [JsonPropertyName("mrpCol")] public string? MrpCol { get; set; }
    }

    /// <summary>One person found in the SCR products file (accounts.py scr_people value).</summary>
    public sealed class ScrPerson
    {
        public HashSet<string> Roles { get; } = new();
        public string Name { get; set; } = "";
        /// <summary>"brand|mc|mrp" keys (normalised with nkey) this person is mapped to.</summary>
        public HashSet<string> Keys { get; } = new();
        /// <summary>Keys where this person is the category head (approver).</summary>
        public HashSet<string> CatheadKeys { get; } = new();
    }

    /// <summary>accounts.py scope_index result: people + approvers per key (in SCR order).</summary>
    public sealed record ScopeIndex(IReadOnlyDictionary<string, ScrPerson> People, IReadOnlyDictionary<string, List<string>> Approvers)
    {
        public static readonly ScopeIndex Empty = new(new Dictionary<string, ScrPerson>(), new Dictionary<string, List<string>>());
        public IReadOnlyCollection<string> KeysOf(string email) =>
            People.TryGetValue(email, out var p) ? p.Keys : (IReadOnlyCollection<string>)Array.Empty<string>();
    }

    public sealed class ScrPersonDto
    {
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("roles")] public List<string> Roles { get; set; } = new();
        [JsonPropertyName("mcs")] public int Mcs { get; set; }
        [JsonPropertyName("exists")] public bool Exists { get; set; }
    }

    public sealed class ScrOverviewResponse
    {
        [JsonPropertyName("hasScr")] public bool HasScr { get; set; }
        [JsonPropertyName("scrRows")] public int ScrRows { get; set; }
        [JsonPropertyName("analysis")] public ScrAnalysis Analysis { get; set; } = new();
        [JsonPropertyName("mapping")] public ScrMapping Mapping { get; set; } = new();
        [JsonPropertyName("people")] public List<ScrPersonDto> People { get; set; } = new();
    }

    public sealed class ScrSyncResult
    {
        [JsonPropertyName("added")] public List<string> Added { get; set; } = new();
        [JsonPropertyName("updated")] public List<string> Updated { get; set; } = new();
        [JsonPropertyName("found")] public int Found { get; set; }
    }

    public sealed class ScrSyncResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("result")] public ScrSyncResult Result { get; set; } = new();
    }

    public sealed class OkResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        public static readonly OkResponse Instance = new();
    }
}
