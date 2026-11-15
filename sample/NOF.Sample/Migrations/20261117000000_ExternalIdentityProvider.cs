using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NOF.Sample.Migrations;

/// <summary>Detaches legacy identity-server tables from the application model without deleting existing identity data.</summary>
public partial class ExternalIdentityProvider : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keycloak owns new identities. Legacy OpenIddict tables stay untouched for an explicit archival decision.
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No physical schema or data was changed by this model-only transition.
    }
}
