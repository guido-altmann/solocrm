using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoloCrm.Domain.Webhooks;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("webhook_subscriptions");
        builder.Property(s => s.Name).HasMaxLength(WebhookSubscription.NameMaxLength);
        builder.Property(s => s.Url).HasMaxLength(WebhookSubscription.UrlMaxLength);
        builder.Property(s => s.ProtectedSecret);

        // Native text[] (SPEC 2.4); mapped through the backing field because the property is read-only.
        builder.Property(s => s.Events)
            .HasField("_events")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("text[]");
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.EventType).HasMaxLength(WebhookDelivery.EventTypeMaxLength);
        builder.Property(d => d.Error).HasMaxLength(WebhookDelivery.ErrorMaxLength);

        // No FK to outbox_messages: pings have no outbox message, and both are cleaned up independently.
        builder.HasOne<WebhookSubscription>().WithMany().HasForeignKey(d => d.SubscriptionId).OnDelete(DeleteBehavior.Cascade);

        // Delivery log per subscription (newest first) and the attempts per event during processing.
        builder.HasIndex(d => new { d.SubscriptionId, d.AttemptedAt });
        builder.HasIndex(d => d.EventId);
        builder.HasIndex(d => d.AttemptedAt);
    }
}
