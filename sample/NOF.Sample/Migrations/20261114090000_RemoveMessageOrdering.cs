using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NOF.Sample.Migrations;

/// <summary>Removes framework ordering after outstanding ordered work has been drained or explicitly migrated.</summary>
public partial class RemoveMessageOrdering : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE "NOFOutboxMessage", "NOFInboxMessage", "NOFOutboxOrderState", "NOFInboxOrderState" IN ACCESS EXCLUSIVE MODE;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM "NOFOutboxMessage"
                    WHERE "Status" <> 1 AND ("OrderKey" IS NOT NULL OR "Sequence" IS NOT NULL OR "CompletesOrderKey"))
                    OR EXISTS (SELECT 1 FROM "NOFInboxMessage"
                    WHERE "Status" <> 1 AND ("OrderKey" IS NOT NULL OR "Sequence" IS NOT NULL OR "CompletesOrderKey"))
                    OR EXISTS (SELECT 1 FROM "NOFInboxOrderState" WHERE "BlockedSequence" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Drain or explicitly migrate pending/failed ordered Outbox and Inbox work before removing NOF ordering.';
                END IF;
            END $$;
            """);
        migrationBuilder.DropTable(name: "NOFOutboxOrderState");
        migrationBuilder.DropTable(name: "NOFInboxOrderState");
        migrationBuilder.DropIndex(name: "IX_NOFOutboxMessage_OrderKey_Sequence", table: "NOFOutboxMessage");
        migrationBuilder.DropIndex(name: "IX_NOFInboxMessage_Route_OrderKey_Sequence", table: "NOFInboxMessage");
        foreach (var table in new[] { "NOFOutboxMessage", "NOFInboxMessage" })
        {
            migrationBuilder.DropColumn(name: "OrderKey", table: table);
            migrationBuilder.DropColumn(name: "Sequence", table: table);
            migrationBuilder.DropColumn(name: "CompletesOrderKey", table: table);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Restore the pre-upgrade backup and matching packages; ordering state cannot be reconstructed from generic headers.");
}
