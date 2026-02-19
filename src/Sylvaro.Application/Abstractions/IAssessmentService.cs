using Normyx.Application.Compliance;

namespace Normyx.Application.Abstractions;

public interface IAssessmentService
{
    Task<AssessmentRunResult> RunAssessmentAsync(Guid tenantId, Guid versionId, Guid ranByUserId, CancellationToken cancellationToken = default);
}
