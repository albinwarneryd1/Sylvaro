namespace Veridion.Domain.Enums;

public enum AiSystemStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

public enum ControlStatus
{
    Draft = 0,
    PendingReview = 1,
    Approved = 2,
    Rejected = 3,
    NotApplicable = 4
}

public enum ActionStatus
{
    New = 0,
    InProgress = 1,
    Done = 2,
    AcceptedRisk = 3
}

public enum FindingSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum PolicyScope
{
    Gdpr = 0,
    Nis2 = 1,
    AiAct = 2
}
