# ADR 003 — Liveness must not check the database

**Status:** accepted · **Date:** 2026-10-07

## Context

The obvious health check queries every dependency and reports one verdict. It is
also wrong here, for a specific reason.

Azure SQL Database on the serverless free offer is configured with
`autoPauseDelay: 60` — the database pauses after an hour idle, which is what
keeps it inside the free grant. Resuming takes roughly 30–60 seconds, observed
directly during setup:

```
az sql db show ... --query status
Resuming
```

App Service polls a health endpoint and recycles the instance when it fails. So
a `/health` that queried SQL would report unhealthy for the entire resume
window, and App Service would restart an application that is working perfectly —
while the restart itself triggers another cold start. A demo API that is idle
between demos would be in a restart loop precisely when someone opens it.

The CD smoke test has the same problem from the other direction: it would fail a
deployment that succeeded, because the database happened to be asleep.

## Decision

Two endpoints with different contracts.

| Endpoint | Checks | Consumed by |
|---|---|---|
| `/health` | Nothing. Returns `Healthy` if the process is serving requests. | App Service probe, CD smoke test |
| `/health/ready` | SQL connectivity, via the `ready` tag | Operators, dashboards |

Implemented with the health-check predicate:

```csharp
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
```

CD **reports** readiness after deploying but never gates on it — a resuming
database is not a failed deployment.

Separately, `EnableRetryOnFailure` is enabled on the EF provider. Transient
errors during a resume are normal operation for serverless, not an edge case.

## Consequences

**Good**

- The app is never recycled for a dependency being asleep.
- Deployments are not failed by database state unrelated to the deployment.
- The distinction is the standard liveness/readiness split, so it reads as
  familiar rather than clever.

**Costs**

- `/health` returning 200 says only that the process is up. A genuinely broken
  app that still serves HTTP looks healthy to App Service. Accepted: the
  alternative failure mode is worse and more frequent.
- Anyone monitoring the service has to know to watch `/health/ready`. Recorded
  in the runbook.
- First request after an idle period is slow regardless — around 43 seconds was
  measured on the F1 tier. Mitigation is operational, not architectural: warm
  the API before a demo. In the runbook.
