using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MygtsMvc.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Indexes on Branch column — used by global query filter on every query
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Devices_Branch ON Devices (Branch);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Devices_Branch_StockStatus ON Devices (Branch, StockStatus);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Transactions_Branch ON Transactions (Branch);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Transactions_Branch_Date ON Transactions (Branch, Date);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Invoices_Branch ON Invoices (Branch);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Invoices_Branch_InvoiceNo ON Invoices (Branch, InvoiceNo);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_ManualReminders_Branch ON ManualReminders (Branch);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Config_Branch ON Config (Branch);");
            // Useful for inventory grouping by InvoiceId
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Devices_InvoiceId ON Devices (InvoiceId);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Devices_Branch;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Devices_Branch_StockStatus;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Transactions_Branch;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Transactions_Branch_Date;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Invoices_Branch;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Invoices_Branch_InvoiceNo;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_ManualReminders_Branch;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Config_Branch;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Devices_InvoiceId;");
        }
    }
}
