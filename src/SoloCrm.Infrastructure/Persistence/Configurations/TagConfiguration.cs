using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        // citext makes the unique index case-insensitive (SPEC 2.3); the length is checked like contacts.email.
        builder.ToTable("tags", t => t.HasCheckConstraint("ck_tags_name_length", $"char_length(name) <= {Tag.NameMaxLength}"));
        builder.Property(t => t.Name).HasColumnType("citext");
        builder.HasIndex(t => t.Name).IsUnique();
        builder.Property(t => t.Color).HasMaxLength(7);
    }
}

/// <summary>Typed join tables (ADR-005); deleting a tag or a record removes its assignments.</summary>
internal sealed class ContactTagConfiguration : IEntityTypeConfiguration<ContactTag>
{
    public void Configure(EntityTypeBuilder<ContactTag> builder)
    {
        builder.ToTable("contact_tags");
        builder.HasKey(t => new { t.ContactId, t.TagId });
        builder.HasOne<Contact>().WithMany().HasForeignKey(t => t.ContactId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => t.TagId);
    }
}

internal sealed class OrganizationTagConfiguration : IEntityTypeConfiguration<OrganizationTag>
{
    public void Configure(EntityTypeBuilder<OrganizationTag> builder)
    {
        builder.ToTable("organization_tags");
        builder.HasKey(t => new { t.OrganizationId, t.TagId });
        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => t.TagId);
    }
}

internal sealed class OpportunityTagConfiguration : IEntityTypeConfiguration<OpportunityTag>
{
    public void Configure(EntityTypeBuilder<OpportunityTag> builder)
    {
        builder.ToTable("opportunity_tags");
        builder.HasKey(t => new { t.OpportunityId, t.TagId });
        builder.HasOne<Opportunity>().WithMany().HasForeignKey(t => t.OpportunityId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => t.TagId);
    }
}
