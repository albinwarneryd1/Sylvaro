using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Veridion.Domain.Entities;
using Veridion.Domain.Enums;

namespace Veridion.Infrastructure.Persistence;

public static class SeedData
{
    public static async Task EnsureSeedAsync(VeridionDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Tenants.AnyAsync(cancellationToken))
        {
            return;
        }

        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var aiSystemId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        var hasher = new PasswordHasher<User>();

        var roles = new[]
        {
            new Role { Id = Guid.NewGuid(), Name = "Admin" },
            new Role { Id = Guid.NewGuid(), Name = "ComplianceOfficer" },
            new Role { Id = Guid.NewGuid(), Name = "SecurityLead" },
            new Role { Id = Guid.NewGuid(), Name = "ProductOwner" },
            new Role { Id = Guid.NewGuid(), Name = "Auditor" }
        };

        var admin = new User
        {
            Id = adminId,
            TenantId = tenantId,
            Email = "admin@nordicfin.example",
            DisplayName = "NordicFin Admin",
            PasswordHash = string.Empty
        };
        admin.PasswordHash = hasher.HashPassword(admin, "ChangeMe123!");

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "NordicFin AB"
        };

        var aiSystem = new AiSystem
        {
            Id = aiSystemId,
            TenantId = tenantId,
            Name = "LoanAssist",
            Description = "Credit risk decision support with profiling signals",
            OwnerUserId = adminId,
            Status = AiSystemStatus.Active
        };

        var version = new AiSystemVersion
        {
            Id = versionId,
            AiSystemId = aiSystemId,
            VersionNumber = 1,
            ChangeSummary = "Initial baseline",
            CreatedByUserId = adminId
        };

        var components = new[]
        {
            new Component { Id = Guid.NewGuid(), AiSystemVersionId = versionId, Name = "Web", Type = "Frontend", Description = "Advisor portal", TrustZone = "DMZ", DataSensitivityLevel = "High" },
            new Component { Id = Guid.NewGuid(), AiSystemVersionId = versionId, Name = "API", Type = "Service", Description = "Decision API", TrustZone = "Internal", DataSensitivityLevel = "High" },
            new Component { Id = Guid.NewGuid(), AiSystemVersionId = versionId, Name = "DB", Type = "Database", Description = "Application storage", TrustZone = "Restricted", DataSensitivityLevel = "High" },
            new Component { Id = Guid.NewGuid(), AiSystemVersionId = versionId, Name = "LLM Gateway", Type = "Proxy", Description = "Provider abstraction", TrustZone = "Internal", IsExternal = true, DataSensitivityLevel = "Medium" },
            new Component { Id = Guid.NewGuid(), AiSystemVersionId = versionId, Name = "Logging", Type = "Telemetry", Description = "Central logs", TrustZone = "Internal", DataSensitivityLevel = "Medium" },
            new Component { Id = Guid.NewGuid(), AiSystemVersionId = versionId, Name = "Analytics", Type = "Warehouse", Description = "BI insights", TrustZone = "Restricted", DataSensitivityLevel = "Medium" }
        };

        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            AiSystemVersionId = versionId,
            Name = "LLM Provider",
            ServiceType = "Inference",
            Region = "US",
            SubProcessors = ["ProviderOps", "CloudHost"],
            DpaInPlace = false,
            Notes = "Cross-border transfer enabled"
        };

        var inventoryItems = new[]
        {
            new DataInventoryItem { Id = Guid.NewGuid(), AiSystemVersionId = versionId, DataCategory = "Personal Data", ContainsPersonalData = true, SpecialCategory = false, Source = "Customer form", LawfulBasis = "Contract", RetentionDays = 3650, TransferOutsideEu = true, Notes = "Long retention" },
            new DataInventoryItem { Id = Guid.NewGuid(), AiSystemVersionId = versionId, DataCategory = "Financial Data", ContainsPersonalData = true, SpecialCategory = false, Source = "Bank statement", LawfulBasis = "LegitimateInterest", RetentionDays = 3650, TransferOutsideEu = true, Notes = "Profiling features" }
        };

        dbContext.Tenants.Add(tenant);
        dbContext.Users.Add(admin);
        dbContext.Roles.AddRange(roles);
        dbContext.UserRoles.AddRange(roles.Select(role => new UserRole { UserId = adminId, RoleId = role.Id }));
        dbContext.AiSystems.Add(aiSystem);
        dbContext.AiSystemVersions.Add(version);
        dbContext.Components.AddRange(components);
        dbContext.Vendors.Add(vendor);
        dbContext.DataInventoryItems.AddRange(inventoryItems);

        dbContext.PolicyPacks.AddRange(
            new PolicyPack { Id = Guid.NewGuid(), Name = "GDPR Core", Version = "2026.1", Scope = PolicyScope.Gdpr },
            new PolicyPack { Id = Guid.NewGuid(), Name = "NIS2 Core", Version = "2026.1", Scope = PolicyScope.Nis2 },
            new PolicyPack { Id = Guid.NewGuid(), Name = "EU AI Act Core", Version = "2026.1", Scope = PolicyScope.AiAct }
        );

        dbContext.Controls.AddRange(
            new Control { Id = Guid.NewGuid(), ControlKey = "GDPR-LEGAL-BASIS", Title = "Lawful basis for processing", Description = "Document lawful basis per data category", OwnerRoleSuggestion = "ComplianceOfficer", EvidenceRequired = ["RoPA", "Policy"], References = ["GDPR Art.6"] },
            new Control { Id = Guid.NewGuid(), ControlKey = "NIS2-INCIDENT-PLAYBOOK", Title = "Incident response playbook", Description = "Maintain tested incident response procedures", OwnerRoleSuggestion = "SecurityLead", EvidenceRequired = ["Incident Plan"], References = ["NIS2 Art.21"] },
            new Control { Id = Guid.NewGuid(), ControlKey = "AIACT-RISK-MGMT", Title = "AI risk management", Description = "Maintain risk management process for AI lifecycle", OwnerRoleSuggestion = "ProductOwner", EvidenceRequired = ["Risk register"], References = ["EU AI Act Art.9"] }
        );

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
