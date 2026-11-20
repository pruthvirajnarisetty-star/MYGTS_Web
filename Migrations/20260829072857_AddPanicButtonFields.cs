using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MygtsMvc.Migrations
{
    /// <inheritdoc />
    public partial class AddPanicButtonFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PanicButtonCost",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PanicButtonQuantity",
                table: "Invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PanicButtonTotal",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PanicButtonCost",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PanicButtonQuantity",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PanicButtonTotal",
                table: "Invoices");
        }
    }
}
