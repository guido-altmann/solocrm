using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts", t => t.HasCheckConstraint(
            "ck_contacts_name_required",
            "first_name IS NOT NULL OR last_name IS NOT NULL"));

        builder.Property(c => c.FirstName).HasMaxLength(Contact.FirstNameMaxLength);
        builder.Property(c => c.LastName).HasMaxLength(Contact.LastNameMaxLength);

        // citext makes equality and the unique index case-insensitive; its length is enforced
        // by the validator and a check constraint because citext has no type modifier.
        builder.Property(c => c.Email).HasColumnType("citext");
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_contacts_email_length",
            $"char_length(email) <= {Contact.EmailMaxLength}"));
        builder.HasIndex(c => c.Email).IsUnique();

        builder.Property(c => c.Phone).HasMaxLength(Contact.PhoneMaxLength);
        builder.Property(c => c.JobTitle).HasMaxLength(Contact.JobTitleMaxLength);
        builder.Property(c => c.LinkedInUrl).HasMaxLength(Contact.LinkedInUrlMaxLength);
        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(c => c.Organization)
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
