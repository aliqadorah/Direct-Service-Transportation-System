using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DirectService.Transportation.Infrastructure.Persistence;

/// <summary>
/// Foreign keys from Shared tables to core.Tenants, core.Companies and core.Users (platform design a.2): the Shared configurations cannot
/// see these entities, so the side that can configures them, with the same constraint names and delete behaviour as before. Moves to
/// Platform.Infrastructure with the anchor entities (Stage 2 session 5).
/// </summary>
internal static class CrossModuleRelationships
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        CompanySetting(modelBuilder.Entity<CompanySetting>());
        CustomFieldDefinition(modelBuilder.Entity<CustomFieldDefinition>());
        NumberSequence(modelBuilder.Entity<NumberSequence>());
        StandardFieldConfig(modelBuilder.Entity<StandardFieldConfig>());
    }

    private static void CompanySetting(EntityTypeBuilder<CompanySetting> builder)
    {
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_CompanySettings_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_CompanySettings_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_CompanySettings_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_CompanySettings_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_CompanySettings_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }

    private static void CustomFieldDefinition(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_CustomFieldDefinitions_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_CustomFieldDefinitions_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_CustomFieldDefinitions_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_CustomFieldDefinitions_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_CustomFieldDefinitions_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }

    private static void NumberSequence(EntityTypeBuilder<NumberSequence> builder)
    {
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_NumberSequences_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_NumberSequences_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_NumberSequences_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_NumberSequences_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_NumberSequences_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }

    private static void StandardFieldConfig(EntityTypeBuilder<StandardFieldConfig> builder)
    {
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_StandardFieldConfigs_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_StandardFieldConfigs_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_StandardFieldConfigs_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_StandardFieldConfigs_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_StandardFieldConfigs_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
