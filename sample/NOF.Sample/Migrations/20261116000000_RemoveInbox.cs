using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NOF.Sample.Migrations;

/// <summary>Removes retired Inbox storage only after every legacy delivery has been resolved and backed up.</summary>
public partial class RemoveInbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE "NOFInboxMessage" IN ACCESS EXCLUSIVE MODE;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM "NOFInboxMessage" WHERE "Status" <> 1) THEN
                    RAISE EXCEPTION 'Resolve or explicitly export and replay pending/failed Inbox deliveries with the old application, and back up receipt history before removing Inbox.';
                END IF;
            END $$;
            """);
        migrationBuilder.DropTable(name: "NOFInboxMessage");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Restore the matching database backup and old binaries; removed Inbox payloads and deduplication history cannot be reconstructed.");
}
