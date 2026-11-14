using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NOF.Sample.Migrations;

/// <summary>Removes the unused legacy Command/Notification label without altering receipt identity or progress.</summary>
public partial class RemoveInboxClassification : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "MessageType", table: "NOFInboxMessage");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Restore the matching backup and packages; a legacy message classification cannot be inferred from opaque receipt routes.");
}
