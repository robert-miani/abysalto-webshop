namespace CartService.Infrastructure.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <summary>The longest error text that is stored, so a huge exception message cannot bloat the table.</summary>
    public const int MaxErrorLength = 2000;

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.Type).HasMaxLength(100).IsRequired();
        builder.Property(message => message.OrderingKey).HasMaxLength(100).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.OccurredAt).IsRequired();
        builder.Property(message => message.Attempts).HasDefaultValue(0);
        builder.Property(message => message.LastError).HasMaxLength(MaxErrorLength);

        // The relay only looks at messages that are not processed yet, oldest first.
        builder.HasIndex(message => message.OccurredAt)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL");
    }
}
