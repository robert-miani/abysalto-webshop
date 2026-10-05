namespace CartService.Infrastructure.Idempotency;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    /// <summary>The longest idempotency key that a client may send.</summary>
    public const int MaxKeyLength = 128;

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        // The key is only unique per requester.
        builder.HasKey(record => new { record.Scope, record.Key });

        builder.Property(record => record.Scope).HasMaxLength(200);
        builder.Property(record => record.Key).HasMaxLength(MaxKeyLength);
        builder.Property(record => record.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(record => record.Status).HasMaxLength(20).IsRequired();
        builder.Property(record => record.ResponseBody).HasColumnType("text");
        builder.Property(record => record.CreatedAt).IsRequired();
        builder.Property(record => record.ExpiresAt).IsRequired();

        // A cleanup job removes records that expired.
        builder.HasIndex(record => record.ExpiresAt).HasDatabaseName("ix_idempotency_records_expires_at");
    }
}
