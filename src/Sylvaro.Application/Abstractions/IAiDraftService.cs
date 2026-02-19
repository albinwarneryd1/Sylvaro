using Normyx.Application.Compliance;

namespace Normyx.Application.Abstractions;

public interface IAiDraftService
{
    Task<DraftActionPlanJson> GenerateActionPlanAsync(AssessmentSummary summary, IReadOnlyCollection<FindingDraft> findings, CancellationToken cancellationToken = default);
    Task<DraftDpiaJson> GenerateDpiaDraftAsync(AssessmentSummary summary, IReadOnlyCollection<FindingDraft> findings, CancellationToken cancellationToken = default);
}
