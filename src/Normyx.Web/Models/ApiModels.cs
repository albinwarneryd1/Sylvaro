using System.Text.Json.Serialization;

namespace Normyx.Web.Models;

public record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public record DashboardTenantDto(
    [property: JsonPropertyName("systemCount")] int SystemCount,
    [property: JsonPropertyName("openActions")] int OpenActions,
    [property: JsonPropertyName("riskDistribution")] Dictionary<string, int> RiskDistribution);
public record DashboardSystemDto(
    Guid Id,
    string Name,
    string Status,
    int AssessmentsCount,
    List<DashboardScorePointDto> ScoreTrend);
public record DashboardScorePointDto(Guid Id, DateTimeOffset RanAt, int Score);

public record TenantDto(Guid Id, string Name, DateTimeOffset CreatedAt);
public record RoleDto(Guid Id, string Name);

public record UserDto(Guid Id, string Email, string DisplayName, DateTimeOffset CreatedAt, DateTimeOffset? DisabledAt, IEnumerable<string> Roles);

public record AiSystemListItem(Guid Id, string Name, string Description, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int VersionCount);

public record AiSystemDetailDto(Guid Id, string Name, string Description, string Status, Guid OwnerUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, VersionSummaryDto? LatestVersion);

public record VersionSummaryDto(Guid Id, int VersionNumber, string ChangeSummary, DateTimeOffset CreatedAt);

public record AssessmentRunResult(Guid AssessmentId, string AiActRiskClass, int ComplianceScore, int FindingsCount, int ActionsCount);
public record AssessmentListItem(Guid Id, DateTimeOffset RanAt, Guid RanByUserId, string LlmProvider, string RiskScoresJson);
public record AssessmentDiffDto(Guid FromVersionId, Guid ToVersionId, int ScoreDelta, bool AiActClassChanged);
public record QuestionnaireResponse(Guid VersionId, Dictionary<string, string> Answers, DateTimeOffset? UpdatedAt, Guid? UpdatedByUserId);

public record FindingDto(Guid Id, string Type, string Severity, string Title, string Description);

public record ActionItemDto(Guid Id, string Title, string Description, string Priority, string OwnerRole, string Status, string AcceptanceCriteria, DateTimeOffset? DueDate, Guid? SourceFindingId, Guid? ApprovedBy, DateTimeOffset? ApprovedAt);
public record ActionBoardDto(List<ActionItemDto> New, List<ActionItemDto> InProgress, List<ActionItemDto> Done, List<ActionItemDto> AcceptedRisk);
public record ActionReviewDto(Guid Id, Guid ActionItemId, Guid ReviewedByUserId, string Decision, string Comment, DateTimeOffset ReviewedAt);

public record ExportArtifactDto(Guid Id, string ExportType, string? MimeType, DateTimeOffset CreatedAt);
public record ExportListItemDto(Guid Id, string ExportType, string MimeType, DateTimeOffset CreatedAt, Guid CreatedByUserId);

public record DocumentUploadResponse(Guid Id, string FileName, DateTimeOffset UploadedAt);
public record DocumentListItemDto(Guid Id, string FileName, string MimeType, DateTimeOffset UploadedAt, Guid UploadedByUserId, string[] Tags, int ExcerptCount);
public record EvidenceExcerptDto(Guid Id, Guid DocumentId, string Title, string Text, string PageRef, Guid CreatedByUserId, DateTimeOffset CreatedAt);

public record ArchitectureResponse(List<ComponentDto> Components, List<DataFlowDto> Flows, List<DataStoreDto> Stores);
public record ComponentDto(Guid Id, Guid AiSystemVersionId, string Name, string Type, string Description, string TrustZone, bool IsExternal, string DataSensitivityLevel);
public record DataFlowDto(Guid Id, Guid AiSystemVersionId, Guid FromComponentId, Guid ToComponentId, string[] DataCategories, string Purpose, bool EncryptionInTransit, string Notes);
public record DataStoreDto(Guid Id, Guid AiSystemVersionId, Guid ComponentId, string StorageType, string Region, int RetentionDays, bool EncryptionAtRest, string AccessModel);

public record InventoryResponse(List<DataInventoryItemDto> DataItems, List<VendorDto> Vendors);
public record DataInventoryItemDto(Guid Id, Guid AiSystemVersionId, string DataCategory, bool ContainsPersonalData, bool SpecialCategory, string Source, string LawfulBasis, int RetentionDays, bool TransferOutsideEu, string Notes);
public record VendorDto(Guid Id, Guid AiSystemVersionId, string Name, string ServiceType, string Region, string[] SubProcessors, bool DpaInPlace, string Notes);

public record AuditLogDto(Guid Id, Guid? ActorUserId, string ActionType, string TargetType, Guid? TargetId, DateTimeOffset Timestamp, string BeforeJson, string AfterJson, string Ip, string UserAgent);

public record EvidenceMapResponse(List<EvidenceTargetDto> Actions, List<EvidenceTargetDto> Findings, List<EvidenceTargetDto> Controls);
public record EvidenceTargetDto(string TargetType, Guid TargetId, string Title, List<EvidenceLinkDto> Evidence);
public record EvidenceLinkDto(Guid Id, Guid EvidenceExcerptId, string Title, string PageRef);

public record EvidenceGapsResponse(int TotalGaps, List<EvidenceGapDto> ActionGaps, List<EvidenceGapDto> ControlGaps);
public record EvidenceGapDto(string TargetType, Guid TargetId, string Title, string SuggestedEvidence);

public record RagSearchResultDto(Guid ChunkId, string SourceType, Guid? DocumentId, string ChunkText, float Score, string[] Tags);

public record IntegrationWebhookDto(Guid Id, string Provider, string WebhookUrl, bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record PolicyPackSelectionDto(Guid Id, string Name, string Version, string Scope, bool Enabled);
public record SecuritySessionDto(Guid Id, string SessionRef, string Ip, string UserAgent, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public record ApiTokenDto(Guid Id, string Name, string Scope, string TokenPrefix, Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);
public record ApiTokenCreateResult(Guid Id, string Name, string Scope, string TokenPrefix, Guid CreatedByUserId, DateTimeOffset CreatedAt, string TokenValue);
