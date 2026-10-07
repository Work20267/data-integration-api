using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataIntegration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CanonicalReading",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SensorId = table.Column<int>(type: "int", nullable: false),
                    SensorModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    IsIndoor = table.Column<bool>(type: "bit", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LastSourceKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalReading", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeadLetterRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngestBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RawRow = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReplayCount = table.Column<int>(type: "int", nullable: false),
                    LastReplayAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadLetterRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowsReceived = table.Column<int>(type: "int", nullable: false),
                    RowsCanonical = table.Column<int>(type: "int", nullable: false),
                    RowsDeadLettered = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestBatch", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceMapping",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SensorModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceField = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MetricCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceMapping", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncWatermark",
                columns: table => new
                {
                    SourceKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastObservedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastExternalKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncWatermark", x => x.SourceKey);
                });

            migrationBuilder.CreateTable(
                name: "CanonicalMeasurement",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReadingId = table.Column<long>(type: "bigint", nullable: false),
                    MetricCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalMeasurement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CanonicalMeasurement_CanonicalReading_ReadingId",
                        column: x => x.ReadingId,
                        principalTable: "CanonicalReading",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StagingRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngestBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SensorModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagingRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagingRecord_IngestBatch_IngestBatchId",
                        column: x => x.IngestBatchId,
                        principalTable: "IngestBatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SourceMapping",
                columns: new[] { "Id", "IsActive", "IsRequired", "MetricCode", "SensorModel", "SourceField", "SourceKey" },
                values: new object[,]
                {
                    { 1, true, false, "PM1", null, "P0", "json-live" },
                    { 2, true, false, "PM10", null, "P1", "json-live" },
                    { 3, true, false, "PM2_5", null, "P2", "json-live" },
                    { 4, true, false, "PM4", null, "P4", "json-live" },
                    { 5, true, false, "TEMPERATURE", null, "temperature", "json-live" },
                    { 6, true, false, "HUMIDITY", null, "humidity", "json-live" },
                    { 7, true, false, "PRESSURE", null, "pressure", "json-live" },
                    { 8, true, false, "PRESSURE_SEALEVEL", null, "pressure_at_sealevel", "json-live" },
                    { 9, true, false, "NOISE_LAEQ", null, "noise_LAeq", "json-live" },
                    { 10, true, false, "PM1", null, "P0", "csv-archive" },
                    { 11, true, false, "PM10", null, "P1", "csv-archive" },
                    { 12, true, false, "PM2_5", null, "P2", "csv-archive" },
                    { 13, true, false, "TEMPERATURE", null, "temperature", "csv-archive" },
                    { 14, true, false, "HUMIDITY", null, "humidity", "csv-archive" },
                    { 15, true, false, "PRESSURE", null, "pressure", "csv-archive" },
                    { 16, true, false, "PRESSURE_SEALEVEL", null, "pressure_sealevel", "csv-archive" },
                    { 17, true, false, "ALTITUDE", null, "altitude", "csv-archive" },
                    { 18, true, false, "NOISE_LAEQ", null, "noise_LAeq", "csv-archive" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalMeasurement_ReadingId_MetricCode",
                table: "CanonicalMeasurement",
                columns: new[] { "ReadingId", "MetricCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalReading_ExternalKey",
                table: "CanonicalReading",
                column: "ExternalKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalReading_ObservedAtUtc",
                table: "CanonicalReading",
                column: "ObservedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalReading_SensorId_ObservedAtUtc",
                table: "CanonicalReading",
                columns: new[] { "SensorId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterRecord_ErrorCode",
                table: "DeadLetterRecord",
                column: "ErrorCode");

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterRecord_IngestBatchId",
                table: "DeadLetterRecord",
                column: "IngestBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterRecord_Status_OccurredAtUtc",
                table: "DeadLetterRecord",
                columns: new[] { "Status", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestBatch_IdempotencyKey",
                table: "IngestBatch",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestBatch_SourceKey_StartedAtUtc",
                table: "IngestBatch",
                columns: new[] { "SourceKey", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceMapping_SourceKey_SensorModel_SourceField",
                table: "SourceMapping",
                columns: new[] { "SourceKey", "SensorModel", "SourceField" },
                unique: true,
                filter: "[SensorModel] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StagingRecord_IngestBatchId_Status",
                table: "StagingRecord",
                columns: new[] { "IngestBatchId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CanonicalMeasurement");

            migrationBuilder.DropTable(
                name: "DeadLetterRecord");

            migrationBuilder.DropTable(
                name: "SourceMapping");

            migrationBuilder.DropTable(
                name: "StagingRecord");

            migrationBuilder.DropTable(
                name: "SyncWatermark");

            migrationBuilder.DropTable(
                name: "CanonicalReading");

            migrationBuilder.DropTable(
                name: "IngestBatch");
        }
    }
}
