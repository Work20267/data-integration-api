using System.Data;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DataIntegration.Infrastructure.Transformation;

/// <summary>
/// Upserts canonical readings and their measurements.
/// </summary>
/// <remarks>
/// <para>
/// This is the idempotency mechanism. The live feed and the daily archive carry
/// the same observations, so writing has to update in place — a plain INSERT
/// would violate the ExternalKey unique index on the second source, and
/// per-row "select then insert or update" would be ~17,700 round trips.
/// </para>
/// <para>
/// So: bulk-load both levels into temp tables, then MERGE from them. Two round
/// trips of DDL, two of bulk copy, two of MERGE — six, regardless of row count.
/// The measurement MERGE joins through ExternalKey to pick up the identity
/// values the reading MERGE has just assigned, which avoids needing the ids
/// back in the application at all.
/// </para>
/// </remarks>
public class CanonicalWriter(DataIntegrationDbContext context) : ICanonicalWriter
{
    private const string CreateTempTables = """
        CREATE TABLE #Reading (
            ExternalKey     nvarchar(120)  NOT NULL PRIMARY KEY,
            SensorId        int            NOT NULL,
            SensorModel     nvarchar(50)   NOT NULL,
            LocationId      int            NULL,
            Latitude        decimal(9,6)   NULL,
            Longitude       decimal(9,6)   NULL,
            Country         nvarchar(10)   NULL,
            IsIndoor        bit            NULL,
            ObservedAtUtc   datetime2(3)   NOT NULL,
            LastSourceKey   nvarchar(50)   NOT NULL
        );

        CREATE TABLE #Measurement (
            ExternalKey     nvarchar(120)  NOT NULL,
            MetricCode      nvarchar(40)   NOT NULL,
            Value           decimal(18,4)  NOT NULL,
            PRIMARY KEY (ExternalKey, MetricCode)
        );
        """;

    // HOLDLOCK is not optional. Without it, MERGE can race another writer between
    // the match test and the insert and produce a duplicate-key violation on the
    // ExternalKey index. It serialises the key range for the duration.
    private const string MergeReadings = """
        MERGE CanonicalReading WITH (HOLDLOCK) AS target
        USING #Reading AS source
            ON target.ExternalKey = source.ExternalKey
        WHEN MATCHED THEN UPDATE SET
            target.SensorModel      = source.SensorModel,
            target.LocationId       = source.LocationId,
            target.Latitude         = source.Latitude,
            target.Longitude        = source.Longitude,
            target.Country          = source.Country,
            target.IsIndoor         = source.IsIndoor,
            target.ObservedAtUtc    = source.ObservedAtUtc,
            target.LastSourceKey    = source.LastSourceKey,
            target.LastUpdatedAtUtc = @Now
        WHEN NOT MATCHED THEN INSERT (
            ExternalKey, SensorId, SensorModel, LocationId, Latitude, Longitude,
            Country, IsIndoor, ObservedAtUtc, LastSourceKey,
            FirstSeenAtUtc, LastUpdatedAtUtc
        ) VALUES (
            source.ExternalKey, source.SensorId, source.SensorModel, source.LocationId,
            source.Latitude, source.Longitude, source.Country, source.IsIndoor,
            source.ObservedAtUtc, source.LastSourceKey, @Now, @Now
        );
        """;

    private const string MergeMeasurements = """
        MERGE CanonicalMeasurement WITH (HOLDLOCK) AS target
        USING (
            SELECT r.Id AS ReadingId, m.MetricCode, m.Value
            FROM #Measurement AS m
            INNER JOIN CanonicalReading AS r ON r.ExternalKey = m.ExternalKey
        ) AS source
            ON target.ReadingId = source.ReadingId
           AND target.MetricCode = source.MetricCode
        WHEN MATCHED THEN UPDATE SET
            target.Value = source.Value
        WHEN NOT MATCHED THEN INSERT (ReadingId, MetricCode, Value)
            VALUES (source.ReadingId, source.MetricCode, source.Value);
        """;

    public async Task<int> UpsertAsync(
        IReadOnlyList<CanonicalReading> readings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readings);

        if (readings.Count == 0)
        {
            return 0;
        }

        var connection = (SqlConnection)context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // Local temp tables live on this connection, so every statement below must
        // use the same one — which is why the connection is opened explicitly and
        // handed to both Dapper and SqlBulkCopy.
        await connection.ExecuteAsync(new CommandDefinition(
            CreateTempTables, cancellationToken: cancellationToken));

        try
        {
            using var readingTable = BuildReadingTable(readings);
            using var measurementTable = BuildMeasurementTable(readings);

            await BulkCopyAsync(connection, "#Reading", readingTable, cancellationToken);
            await BulkCopyAsync(connection, "#Measurement", measurementTable, cancellationToken);

            var now = DateTime.UtcNow;

            // Readings first: the measurement MERGE joins to the rows it creates.
            await connection.ExecuteAsync(new CommandDefinition(
                MergeReadings, new { Now = now }, commandTimeout: 180,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                MergeMeasurements, commandTimeout: 180,
                cancellationToken: cancellationToken));

            return readings.Count;
        }
        finally
        {
            // Temp tables are connection-scoped, but EF pools the connection, so a
            // later call on the same one would hit "table already exists".
            await connection.ExecuteAsync(
                "DROP TABLE IF EXISTS #Reading; DROP TABLE IF EXISTS #Measurement;");
        }
    }

    private static async Task BulkCopyAsync(
        SqlConnection connection,
        string destination,
        DataTable table,
        CancellationToken cancellationToken)
    {
        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = destination,
            BatchSize = table.Rows.Count,
            BulkCopyTimeout = 180,
        };

        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    private static DataTable BuildReadingTable(IReadOnlyList<CanonicalReading> readings)
    {
        var table = new DataTable("#Reading");

        table.Columns.Add("ExternalKey", typeof(string));
        table.Columns.Add("SensorId", typeof(int));
        table.Columns.Add("SensorModel", typeof(string));
        table.Columns.Add("LocationId", typeof(int));
        table.Columns.Add("Latitude", typeof(decimal));
        table.Columns.Add("Longitude", typeof(decimal));
        table.Columns.Add("Country", typeof(string));
        table.Columns.Add("IsIndoor", typeof(bool));
        table.Columns.Add("ObservedAtUtc", typeof(DateTime));
        table.Columns.Add("LastSourceKey", typeof(string));

        // The temp table is keyed on ExternalKey, so a batch containing the same
        // reading twice — which the live feed does occasionally — must collapse
        // before the bulk copy rather than fail it.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reading in readings)
        {
            if (!seen.Add(reading.ExternalKey))
            {
                continue;
            }

            table.Rows.Add(
                reading.ExternalKey,
                reading.SensorId,
                reading.SensorModel,
                (object?)reading.LocationId ?? DBNull.Value,
                (object?)reading.Latitude ?? DBNull.Value,
                (object?)reading.Longitude ?? DBNull.Value,
                (object?)reading.Country ?? DBNull.Value,
                (object?)reading.IsIndoor ?? DBNull.Value,
                reading.ObservedAtUtc,
                reading.LastSourceKey);
        }

        return table;
    }

    private static DataTable BuildMeasurementTable(IReadOnlyList<CanonicalReading> readings)
    {
        var table = new DataTable("#Measurement");

        table.Columns.Add("ExternalKey", typeof(string));
        table.Columns.Add("MetricCode", typeof(string));
        table.Columns.Add("Value", typeof(decimal));

        var seen = new HashSet<(string, string)>();

        foreach (var reading in readings)
        {
            foreach (var measurement in reading.Measurements)
            {
                if (!seen.Add((reading.ExternalKey, measurement.MetricCode)))
                {
                    continue;
                }

                table.Rows.Add(reading.ExternalKey, measurement.MetricCode, measurement.Value);
            }
        }

        return table;
    }
}
