using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MygtsMvc.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleDetailsToDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivationDate",
                table: "Recharges",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "ManualReminders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TargetAudience",
                table: "ManualReminders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CdacCertificate",
                table: "Devices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InformedDate",
                table: "Devices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VldtCertificate",
                table: "Devices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CompanyRechargeBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: false),
                    PoNumber = table.Column<string>(type: "TEXT", nullable: false),
                    PayReferenceNo = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    AdvanceAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CarriedForwardAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CarriedOutAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    TransferredFromBatchId = table.Column<string>(type: "TEXT", nullable: true),
                    TransferredFromBatchPo = table.Column<string>(type: "TEXT", nullable: true),
                    TransferredToBatchId = table.Column<string>(type: "TEXT", nullable: true),
                    TransferredToBatchPo = table.Column<string>(type: "TEXT", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    RemainingBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalDevices = table.Column<int>(type: "INTEGER", nullable: false),
                    RenewalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReactivationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyRechargeBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomDropdownOptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomDropdownOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataShareRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: false),
                    VehicleNo = table.Column<string>(type: "TEXT", nullable: false),
                    Imei = table.Column<string>(type: "TEXT", nullable: false),
                    Iccid = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceBranch = table.Column<string>(type: "TEXT", nullable: false),
                    SharedBy = table.Column<string>(type: "TEXT", nullable: false),
                    SharedByEmail = table.Column<string>(type: "TEXT", nullable: false),
                    SharedByRole = table.Column<string>(type: "TEXT", nullable: false),
                    SharedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Recipient = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataShareRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    BatchPrefix = table.Column<string>(type: "TEXT", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", nullable: false),
                    AltPhone = table.Column<string>(type: "TEXT", nullable: false),
                    ExtraPhones = table.Column<string>(type: "TEXT", nullable: false),
                    WhatsAppNumber = table.Column<string>(type: "TEXT", nullable: false),
                    WhatsAppSameAsPhone = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyRechargeItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: false),
                    BatchId = table.Column<string>(type: "TEXT", nullable: false),
                    SlNo = table.Column<int>(type: "INTEGER", nullable: false),
                    VehicleNo = table.Column<string>(type: "TEXT", nullable: false),
                    Imei = table.Column<string>(type: "TEXT", nullable: false),
                    Iccid = table.Column<string>(type: "TEXT", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", nullable: false),
                    PerDeviceAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyRechargeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyRechargeItems_CompanyRechargeBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "CompanyRechargeBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRechargeItems_BatchId",
                table: "CompanyRechargeItems",
                column: "BatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyRechargeItems");

            migrationBuilder.DropTable(
                name: "CustomDropdownOptions");

            migrationBuilder.DropTable(
                name: "DataShareRecords");

            migrationBuilder.DropTable(
                name: "Vendors");

            migrationBuilder.DropTable(
                name: "CompanyRechargeBatches");

            migrationBuilder.DropColumn(
                name: "ActivationDate",
                table: "Recharges");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "ManualReminders");

            migrationBuilder.DropColumn(
                name: "TargetAudience",
                table: "ManualReminders");

            migrationBuilder.DropColumn(
                name: "CdacCertificate",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "InformedDate",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "VldtCertificate",
                table: "Devices");
        }
    }
}
