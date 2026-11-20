using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MygtsMvc.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionSimCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionCost",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SimCost",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionCost",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SimCost",
                table: "Transactions");
        }
    }
}
