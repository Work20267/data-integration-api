using System.Data;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Ingestion;
using DataIntegration.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DataIntegration.Infrastructure.Ingestion;

/// <summary>
/// Bulk-loads raw rows into staging with <see cref="SqlBulkCopy"/>.
/// </summary>
/// <remarks>
/// This is the hot path from ADR 002. EF's SaveChanges issues a round trip per
/// entity, which at ~17,700 rows per ingest is unusable; SqlBulkCopy streams a
/// chunk in one operation.
///
/// The connection comes from the EF context on purpose. The connection string is
/// configured once, and in Azure it carries
/// Authentication=Active Directory Managed Identity — so the bulk path
/// authenticates passwordless exactly like the rest of the application, rather
/// than needing its own credential.
/// </remarks>
public class StagingWriter(DataIntegrationDbContext context) : IStagingWriter
{
    private const string TableName = "StagingRecord";

    public async Task<int> WriteChunkAsync(
        Guid batchId,
        IReadOnlyList<RawRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            return 0;
        }

        var connection = (SqlConnection)context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        using var table = BuildTable(batchId, rows);

        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = TableName,
            BatchSize = rows.Count,
            BulkCopyTimeout = 120,
        };

        // Mapped by name so a column reorder in a future migration cannot
        // silently shift values into the wrong columns.
        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table, cancellationToken);

        return rows.Count;
    }

    private static DataTable BuildTable(Guid batchId, IReadOnlyList<RawRow> rows)
    {
        var table = new DataTable(TableName);

        // Id is an identity column and is deliberately absent.
        table.Columns.Add("IngestBatchId", typeof(Guid));
        table.Columns.Add("SourceKey", typeof(string));
        table.Columns.Add("RawPayload", typeof(string));
        table.Columns.Add("SensorModel", typeof(string));
        table.Columns.Add("ReceivedAtUtc", typeof(DateTime));
        table.Columns.Add("Status", typeof(int));

        var receivedAt = DateTime.UtcNow;

        foreach (var row in rows)
        {
            table.Rows.Add(
                batchId,
                row.SourceKey,
                row.Payload,
                row.SensorModel ?? (object)DBNull.Value,
                receivedAt,
                (int)StagingStatus.Pending);
        }

        return table;
    }
}
