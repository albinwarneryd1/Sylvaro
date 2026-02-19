using Microsoft.EntityFrameworkCore;
using Normyx.Domain.Entities;

namespace Normyx.Infrastructure.Persistence;

public class NormyxDbContext(DbContextOptions<NormyxDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AiSystem> AiSystems => Set<AiSystem>();
    public DbSet<AiSystemVersion> AiSystemVersions => Set<AiSystemVersion>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<DataFlow> DataFlows => Set<DataFlow>();
    public DbSet<DataStore> DataStores => Set<DataStore>();
    public DbSet<DataInventoryItem> DataInventoryItems => Set<DataInventoryItem>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<EvidenceExcerpt> EvidenceExcerpts => Set<EvidenceExcerpt>();
    public DbSet<EvidenceLink> EvidenceLinks => Set<EvidenceLink>();
    public DbSet<PolicyPack> PolicyPacks => Set<PolicyPack>();
    public DbSet<PolicyRule> PolicyRules => Set<PolicyRule>();
    public DbSet<Control> Controls => Set<Control>();
    public DbSet<ControlInstance> ControlInstances => Set<ControlInstance>();
    public DbSet<ComplianceQuestionnaire> ComplianceQuestionnaires => Set<ComplianceQuestionnaire>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ExportArtifact> ExportArtifacts => Set<ExportArtifact>();
    public DbSet<TenantIntegration> TenantIntegrations => Set<TenantIntegration>();
    public DbSet<RagChunk> RagChunks => Set<RagChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        modelBuilder.Entity<UserRole>().HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId);
        modelBuilder.Entity<UserRole>().HasOne(x => x.Role).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId);

        modelBuilder.Entity<User>().HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
        modelBuilder.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<AiSystem>().HasIndex(x => new { x.TenantId, x.Name });
        modelBuilder.Entity<AiSystemVersion>().HasIndex(x => new { x.AiSystemId, x.VersionNumber }).IsUnique();

        modelBuilder.Entity<PolicyPack>().HasIndex(x => new { x.Name, x.Version, x.Scope }).IsUnique();
        modelBuilder.Entity<PolicyRule>().HasIndex(x => new { x.PolicyPackId, x.RuleKey }).IsUnique();
        modelBuilder.Entity<Control>().HasIndex(x => x.ControlKey).IsUnique();

        modelBuilder.Entity<AiSystem>().HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AiSystemVersion>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditLog>().HasIndex(x => new { x.TenantId, x.Timestamp });
        modelBuilder.Entity<TenantIntegration>().HasIndex(x => new { x.TenantId, x.Provider }).IsUnique();

        modelBuilder.Entity<RagChunk>()
            .Property(x => x.Embedding)
            .HasColumnType("real[]");
        modelBuilder.Entity<RagChunk>()
            .HasIndex(x => new { x.TenantId, x.SourceType });
        modelBuilder.Entity<RagChunk>()
            .HasIndex(x => new { x.TenantId, x.DocumentId });

        modelBuilder.Entity<Document>().HasMany(x => x.Excerpts).WithOne(x => x.Document).HasForeignKey(x => x.DocumentId);
        modelBuilder.Entity<EvidenceExcerpt>().HasMany(x => x.EvidenceLinks).WithOne(x => x.EvidenceExcerpt).HasForeignKey(x => x.EvidenceExcerptId);

        modelBuilder.Entity<Assessment>().HasMany(x => x.Findings).WithOne(x => x.Assessment).HasForeignKey(x => x.AssessmentId);
    }
}
