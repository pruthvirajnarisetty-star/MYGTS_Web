using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MygtsMvc.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchBifurcation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TotalPayment",
                table: "Transactions",
                newName: "Branch");

            migrationBuilder.AddColumn<string>(
                name: "Branch",
                table: "ManualReminders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Branch",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Branch",
                table: "Devices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Branch",
                table: "Config",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Branch",
                table: "ManualReminders");

            migrationBuilder.DropColumn(
                name: "Branch",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Branch",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Branch",
                table: "Config");

            migrationBuilder.RenameColumn(
                name: "Branch",
                table: "Transactions",
                newName: "TotalPayment");
        }
    }
}
