using Normyx.Domain.Enums;

namespace Normyx.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();
}

public class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? ExternalAuthRef { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisabledAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }

    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string CreatedIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User User { get; set; } = null!;
}

public class AiSystem
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid OwnerUserId { get; set; }
    public AiSystemStatus Status { get; set; } = AiSystemStatus.Draft;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<AiSystemVersion> Versions { get; set; } = new List<AiSystemVersion>();
}

public class AiSystemVersion
{
    public Guid Id { get; set; }
    public Guid AiSystemId { get; set; }
    public int VersionNumber { get; set; }
    public string ChangeSummary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedByUserId { get; set; }

    public AiSystem AiSystem { get; set; } = null!;
    public ICollection<Component> Components { get; set; } = new List<Component>();
    public ICollection<DataFlow> DataFlows { get; set; } = new List<DataFlow>();
    public ICollection<DataStore> DataStores { get; set; } = new List<DataStore>();
    public ICollection<DataInventoryItem> DataInventoryItems { get; set; } = new List<DataInventoryItem>();
    public ICollection<Vendor> Vendors { get; set; } = new List<Vendor>();
    public ICollection<Assessment> Assessments { get; set; } = new List<Assessment>();
    public ICollection<ActionItem> ActionItems { get; set; } = new List<ActionItem>();
    public ICollection<ControlInstance> ControlInstances { get; set; } = new List<ControlInstance>();
}

public class Component
{
    public Guid Id { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TrustZone { get; set; } = string.Empty;
    public bool IsExternal { get; set; }
    public string DataSensitivityLevel { get; set; } = string.Empty;

    public AiSystemVersion AiSystemVersion { get; set; } = null!;
}

public class DataFlow
{
    public Guid Id { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public Guid FromComponentId { get; set; }
    public Guid ToComponentId { get; set; }
    public string[] DataCategories { get; set; } = [];
    public string Purpose { get; set; } = string.Empty;
    public bool EncryptionInTransit { get; set; }
    public string Notes { get; set; } = string.Empty;

    public AiSystemVersion AiSystemVersion { get; set; } = null!;
}

public class DataStore
{
    public Guid Id { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public Guid ComponentId { get; set; }
    public string StorageType { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public int RetentionDays { get; set; }
    public bool EncryptionAtRest { get; set; }
    public string AccessModel { get; set; } = string.Empty;

    public AiSystemVersion AiSystemVersion { get; set; } = null!;
}

public class DataInventoryItem
{
    public Guid Id { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public string DataCategory { get; set; } = string.Empty;
    public bool ContainsPersonalData { get; set; }
    public bool SpecialCategory { get; set; }
    public string Source { get; set; } = string.Empty;
    public string LawfulBasis { get; set; } = string.Empty;
    public int RetentionDays { get; set; }
    public bool TransferOutsideEu { get; set; }
    public string Notes { get; set; } = string.Empty;

    public AiSystemVersion AiSystemVersion { get; set; } = null!;
}

public class Vendor
{
    public Guid Id { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string[] SubProcessors { get; set; } = [];
    public bool DpaInPlace { get; set; }
    public string Notes { get; set; } = string.Empty;

    public AiSystemVersion AiSystemVersion { get; set; } = null!;
}

public class Document
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string StorageRef { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public string[] Tags { get; set; } = [];

    public ICollection<EvidenceExcerpt> Excerpts { get; set; } = new List<EvidenceExcerpt>();
}

public class EvidenceExcerpt
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string PageRef { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Document Document { get; set; } = null!;
    public ICollection<EvidenceLink> EvidenceLinks { get; set; } = new List<EvidenceLink>();
}

public class EvidenceLink
{
    public Guid Id { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public Guid EvidenceExcerptId { get; set; }

    public EvidenceExcerpt EvidenceExcerpt { get; set; } = null!;
}

public class PolicyPack
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public PolicyScope Scope { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<PolicyRule> Rules { get; set; } = new List<PolicyRule>();
}

public class PolicyRule
{
    public Guid Id { get; set; }
    public Guid PolicyPackId { get; set; }
    public string RuleKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FindingSeverity Severity { get; set; }
    public string ConditionJson { get; set; } = "{}";
    public string[] OutputControlKeys { get; set; } = [];

    public PolicyPack PolicyPack { get; set; } = null!;
}

public class Control
{
    public Guid Id { get; set; }
    public string ControlKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OwnerRoleSuggestion { get; set; } = string.Empty;
    public string[] EvidenceRequired { get; set; } = [];
    public string[] References { get; set; } = [];
}

public class ControlInstance
{
    public Guid Id { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public string ControlKey { get; set; } = string.Empty;
    public ControlStatus Status { get; set; } = ControlStatus.Draft;
    public string Notes { get; set; } = string.Empty;
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public AiSystemVersion AiSystemVersion { get; set; } = null!;
}

public class ComplianceQuestionnaire
{
    public Guid Id { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AiSystemVersion AiSystemVersion { get; set; } = null!;
}

public class Assessment
{
    public Guid Id { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public Guid RanByUserId { get; set; }
    public DateTimeOffset RanAt { get; set; } = DateTimeOffset.UtcNow;
    public string LlmProvider { get; set; } = string.Empty;
    public string[] PolicyPackVersionRefs { get; set; } = [];
    public string SummaryJson { get; set; } = "{}";
    public string RiskScoresJson { get; set; } = "{}";

    public AiSystemVersion AiSystemVersion { get; set; } = null!;
    public ICollection<Finding> Findings { get; set; } = new List<Finding>();
}

public class Finding
{
    public Guid Id { get; set; }
    public Guid AssessmentId { get; set; }
    public string Type { get; set; } = string.Empty;
    public FindingSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid[] AffectedComponentIds { get; set; } = [];
    public Guid[] EvidenceLinks { get; set; } = [];

    public Assessment Assessment { get; set; } = null!;
}

public class ActionItem
{
    public Guid Id { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public Guid? SourceFindingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "P2";
    public string OwnerRole { get; set; } = string.Empty;
    public ActionStatus Status { get; set; } = ActionStatus.New;
    public string AcceptanceCriteria { get; set; } = string.Empty;
    public DateTimeOffset? DueDate { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public AiSystemVersion AiSystemVersion { get; set; } = null!;
}

public class ActionReview
{
    public Guid Id { get; set; }
    public Guid ActionItemId { get; set; }
    public Guid ReviewedByUserId { get; set; }
    public ReviewDecision Decision { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;

    public ActionItem ActionItem { get; set; } = null!;
}

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public string Ip { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}

public class ExportArtifact
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AiSystemVersionId { get; set; }
    public string ExportType { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/pdf";
    public string StorageRef { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TenantIntegration
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string? AuthHeader { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TenantPolicyPackSelection
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PolicyPackId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RagChunk
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? DocumentId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string ChunkText { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public float[] Embedding { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
