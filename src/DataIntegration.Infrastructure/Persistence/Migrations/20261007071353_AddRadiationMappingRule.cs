using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataIntegration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRadiationMappingRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SourceMapping",
                columns: new[] { "Id", "IsActive", "IsRequired", "MetricCode", "SensorModel", "SourceField", "SourceKey" },
                values: new object[] { 19, true, false, "RADIATION_CPM", "Radiation Si22G", "counts_per_minute", "json-live" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SourceMapping",
                keyColumn: "Id",
                keyValue: 19);
        }
    }
}
