using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NOF.Sample.Migrations;

/// <summary>Switches a drained legacy outbox to stable, versioned intent schemas.</summary>
public partial class GenericOutbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Never reinterpret an outstanding command/notification as a generic intent.
        migrationBuilder.Sql("""
            LOCK TABLE "NOFOutboxMessage" IN ACCESS EXCLUSIVE MODE;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM "NOFOutboxMessage" WHERE "Status" <> 1) THEN
                    RAISE EXCEPTION 'Drain or explicitly migrate all pending/failed legacy Outbox records before upgrading NOF.';
                END IF;
            END $$;
            """);
        migrationBuilder.DropColumn(name: "DispatchRoutes", table: "NOFOutboxMessage");
        migrationBuilder.DropColumn(name: "MessageType", table: "NOFOutboxMessage");
        migrationBuilder.AddColumn<string>(name: "MessageType", table: "NOFOutboxMessage",
            type: "character varying(512)", maxLength: 512, nullable: true);
        migrationBuilder.Sql("UPDATE \"NOFOutboxMessage\" SET \"MessageType\" = 'legacy.completed';");
        migrationBuilder.AlterColumn<string>(name: "MessageType", table: "NOFOutboxMessage",
            type: "character varying(512)", maxLength: 512, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(512)", oldMaxLength: 512, oldNullable: true);
        migrationBuilder.AddColumn<int>(name: "SchemaVersion", table: "NOFOutboxMessage",
            type: "integer", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<DateTime>(name: "NextAttemptAtUtc", table: "NOFOutboxMessage",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_NOFOutboxMessage_Status_NextAttemptAtUtc", table: "NOFOutboxMessage",
            columns: ["Status", "NextAttemptAtUtc"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Restore the pre-upgrade database backup and matching application packages; generic intents cannot be converted to legacy routes automatically.");
}
