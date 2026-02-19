namespace Normyx.Application.Security;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string ComplianceOfficer = "ComplianceOfficer";
    public const string SecurityLead = "SecurityLead";
    public const string ProductOwner = "ProductOwner";
    public const string Auditor = "Auditor";

    public static readonly string[] All = [Admin, ComplianceOfficer, SecurityLead, ProductOwner, Auditor];
}
