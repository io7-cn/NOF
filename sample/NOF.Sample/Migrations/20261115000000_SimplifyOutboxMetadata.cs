using Microsoft.EntityFrameworkCore.Migrations;

namespace NOF.Sample.Migrations;

/// <summary>Infers the known sample message identities and removes obsolete schema-version metadata without deleting messages.</summary>
public partial class SimplifyOutboxMetadata : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE "NOFOutboxMessage" IN ACCESS EXCLUSIVE MODE;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM "NOFOutboxMessage"
                    WHERE "SchemaVersion" <> 1 AND "Status" <> 1) THEN
                    RAISE EXCEPTION 'Drain or explicitly migrate non-v1 pending/failed Outbox payloads before dropping schema versions.';
                END IF;
            END $$;
            UPDATE "NOFOutboxMessage"
            SET "MessageType" = CASE "MessageType"
                WHEN 'sample.send-email' THEN 'NOF.Sample.SendEmail'
                WHEN 'sample.order-created' THEN 'NOF.Sample.OrderCreated'
                END
            WHERE "MessageType" IN ('sample.send-email', 'sample.order-created') AND "SchemaVersion" = 1;
            """);
        migrationBuilder.DropColumn(name: "SchemaVersion", table: "NOFOutboxMessage");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Restore a matching database backup and application packages; dropped Outbox schema versions cannot be reconstructed.");
}
