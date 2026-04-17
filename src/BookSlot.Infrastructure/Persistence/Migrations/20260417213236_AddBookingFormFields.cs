using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookSlot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingFormFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "form_schema_json",
                table: "service_types",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_field_values_json",
                table: "bookings",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "form_schema_json",
                table: "service_types");

            migrationBuilder.DropColumn(
                name: "custom_field_values_json",
                table: "bookings");
        }
    }
}
