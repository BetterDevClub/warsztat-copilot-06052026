using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookSlot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "booking_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_notes_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_booking_notes_booking_id",
                table: "booking_notes",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_booking_notes_tenant_id_booking_id_created_at",
                table: "booking_notes",
                columns: new[] { "tenant_id", "booking_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_notes");
        }
    }
}
