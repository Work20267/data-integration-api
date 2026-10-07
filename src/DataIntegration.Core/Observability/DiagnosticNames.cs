namespace DataIntegration.Core.Observability;

/// <summary>
/// Names shared by every telemetry producer. Declared once so an ActivitySource
/// created in Infrastructure and a meter registered in the API cannot drift apart
/// and silently stop correlating.
/// </summary>
public static class DiagnosticNames
{
    /// <summary>Root name for spans this application creates itself.</summary>
    public const string ActivitySource = "DataIntegration";

    /// <summary>Root name for metrics this application emits. Phase 7 adds instruments here.</summary>
    public const string Meter = "DataIntegration";

    /// <summary>Span and metric tag names, so dashboards can group consistently.</summary>
    public static class Tags
    {
        public const string SourceKey = "di.source_key";
        public const string BatchId = "di.batch_id";
        public const string SensorModel = "di.sensor_model";
        public const string ErrorCode = "di.error_code";
    }
}
