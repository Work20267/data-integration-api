using DataIntegration.Core.Entities;

namespace DataIntegration.Core.Transformation;

/// <summary>Either a canonical reading or the reason one could not be produced.</summary>
public sealed record TransformResult
{
    private TransformResult() { }

    public CanonicalReading? Reading { get; private init; }
    public TransformFailure? Failure { get; private init; }

    public bool Succeeded => Reading is not null;

    public static TransformResult Success(CanonicalReading reading) =>
        new() { Reading = reading };

    public static TransformResult Failed(string errorCode, string message) =>
        new() { Failure = new TransformFailure(errorCode, message) };
}
