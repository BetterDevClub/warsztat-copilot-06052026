using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookSlot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    dedup_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_tenant_id_created_at",
                table: "notification_logs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_tenant_id_dedup_key",
                table: "notification_logs",
                columns: new[] { "tenant_id", "dedup_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_logs");
        }
    }
}
