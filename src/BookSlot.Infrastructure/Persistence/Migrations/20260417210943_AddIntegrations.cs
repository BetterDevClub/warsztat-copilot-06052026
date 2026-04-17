using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookSlot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_account_id = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    access_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    refresh_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    access_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scope = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_connections", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integration_connections_tenant_id_provider_staff_id",
                table: "integration_connections",
                columns: new[] { "tenant_id", "provider", "staff_id" },
                unique: true,
                filter: "is_active = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_connections");
        }
    }
}
