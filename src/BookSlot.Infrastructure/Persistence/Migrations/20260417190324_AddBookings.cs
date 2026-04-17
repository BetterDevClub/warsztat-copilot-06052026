using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookSlot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    guest_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    guest_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    guest_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    guest_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    internal_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancel_token = table.Column<Guid>(type: "uuid", nullable: false),
                    reschedule_token = table.Column<Guid>(type: "uuid", nullable: false),
                    rescheduled_from_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_cancel_token",
                table: "bookings",
                column: "cancel_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_reschedule_token",
                table: "bookings",
                column: "reschedule_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tenant_id_staff_id_start_utc_status",
                table: "bookings",
                columns: new[] { "tenant_id", "staff_id", "start_utc", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookings");
        }
    }
}
