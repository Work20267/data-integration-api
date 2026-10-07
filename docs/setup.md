# Setup

Everything needed to get this project running on a new machine, for Windows,
macOS on Apple Silicon, and Linux.

Every command here has been run. Where a platform has a genuine obstacle — and
Apple Silicon has one — it is called out rather than glossed over.

---

## Pick your tier

| Tier | You want to | Install |
|---|---|---|
| **1** | Run it and poke at the API | Git + Docker |
| **2** | Change the code, run tests | + .NET 10 SDK |
| **3** | Deploy to Azure | + Azure CLI |

Tier 1 needs **no .NET SDK**. The API builds inside the container.

---

## Tier 1 · Run it

### Windows

```powershell
winget install Git.Git
winget install Docker.DockerDesktop
winget install GitHub.cli
```

Launch Docker Desktop and wait for the whale icon to stop animating (~1 min).

### macOS — Apple Silicon (M1 / M2 / M3)

```bash
# Homebrew first, if you do not have it
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

brew install git gh
brew install --cask docker
```

Open Docker Desktop from Applications, then **change two settings**:

1. **Settings → General → "Use Rosetta for x86_64/amd64 emulation on Apple
   Silicon"** — turn it **on**.
2. **Settings → Resources → Memory** — at least **4 GB**. SQL Server alone wants
   about 2 GB and will be killed silently below that.

> **Why.** Microsoft publishes **no arm64 image for SQL Server**. On Apple
> Silicon it runs emulated. `compose.yaml` pins `platform: linux/amd64` on that
> service so Docker uses emulation deliberately instead of failing with a
> confusing architecture error. Rosetta makes the emulation far quicker than the
> default QEMU path.
>
> The API itself builds **natively** on arm64 — .NET publishes arm64 images — so
> only the database is emulated.

### Linux

```bash
sudo apt install -y git
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker "$USER"   # then log out and back in
```

### Then, on any platform

The repository is **private**, so cloning needs authentication:

```bash
gh auth login          # choose the Work20267 account
git clone https://github.com/Work20267/data-integration-api
cd data-integration-api
docker compose up --build
```

First run takes **2–3 minutes** on Windows/Linux, and **5–10 minutes on Apple
Silicon** — the emulated SQL Server image is slow to start the first time. After
that, seconds.

### Verify

```bash
curl http://localhost:8080/health          # Healthy
curl http://localhost:8080/health/ready    # Healthy   ← the database is migrated
curl http://localhost:8080/api/v1/sync/status
```

`/health/ready` returning **Healthy** is the real check: it means the schema was
created and the API can query it. If `/health` passes but `/health/ready` does
not, the database is not ready yet — wait thirty seconds and retry.

Confirm the seed data arrived:

```bash
docker exec di-sql /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Local_Dev_P@ss1' -C -d DataIntegration -W \
  -Q "SELECT COUNT(*) AS tables FROM sys.tables; SELECT COUNT(*) AS rules FROM SourceMapping;"
```

Expect **8 tables** and **19 mapping rules**.

### Try it end to end

```bash
# Pulls ~17,700 live readings. Takes about 15 seconds.
curl -X POST -H "Idempotency-Key: demo-1" \
  http://localhost:8080/api/v1/ingest/json-live

# Same key again - recognised, nothing re-fetched. ~0.06s.
curl -X POST -H "Idempotency-Key: demo-1" \
  http://localhost:8080/api/v1/ingest/json-live

# Transform the batch (use the batchId from the first response)
curl -X POST http://localhost:8080/api/v1/transform/<batchId>

# What failed, and why
curl http://localhost:8080/api/v1/dead-letter/summary
```

The upstream is volunteer-funded. Run the ingest a handful of times, not in a
loop.

### Stopping

```bash
docker compose down        # stop, keep the data
docker compose down -v     # stop and delete the database volume
```

---

## Tier 2 · Work on the code

### Install the SDK

**Windows**

```powershell
winget install Microsoft.DotNet.SDK.10
dotnet tool install --global dotnet-ef
```

**macOS — Apple Silicon**

Use the official **Arm64** installer from
<https://dotnet.microsoft.com/download/dotnet/10.0> — pick *macOS / Arm64*, not
x64. Homebrew's `--cask dotnet-sdk` also works but tracks whatever is latest,
which may not satisfy the pin.

```bash
dotnet --version        # must be 10.0.4xx — see global.json
dotnet tool install --global dotnet-ef
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zshrc && source ~/.zshrc
```

**Linux**

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
export PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"
```

> `global.json` pins the SDK to the 10.0.4xx band, so another machine cannot
> silently build with a different compiler. If `dotnet --version` falls outside
> it, every command will refuse to run until the right SDK is installed — that
> is the pin working, not a fault.

### Run it

```bash
docker compose up sql -d        # database only; the API runs from source

dotnet restore
dotnet build
dotnet test
dotnet run --project src/DataIntegration.Api
```

`dotnet run` serves on the port in `launchSettings.json` (**5137**), not 8080.
To choose your own:

```bash
dotnet run --project src/DataIntegration.Api --no-launch-profile --urls http://localhost:5199
```

### Tests

```bash
dotnet test                                      # all 53
dotnet test tests/DataIntegration.UnitTests      # 47, no Docker needed, ~100ms
dotnet test tests/DataIntegration.IntegrationTests  # 6, needs Docker
```

The integration tests start their **own** SQL Server through Testcontainers —
separate from the compose one, so they cannot corrupt your development data.

On Apple Silicon these run emulated and take noticeably longer: budget around
three minutes against roughly one on amd64. They do pass.

### Migrations

```bash
export ConnectionStrings__Default="Server=localhost,1433;Initial Catalog=DataIntegration;User Id=sa;Password=Local_Dev_P@ss1;Encrypt=True;TrustServerCertificate=True;"

dotnet ef migrations add <Name> \
  --project src/DataIntegration.Infrastructure \
  --startup-project src/DataIntegration.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/DataIntegration.Infrastructure \
  --startup-project src/DataIntegration.Api
```

On Windows PowerShell use `$env:ConnectionStrings__Default = "..."` instead of
`export`.

The design-time factory reads that variable. Without it, it falls back to a
connection string that deliberately does not connect — enough to *scaffold* a
migration, not to apply one.

---

## Tier 3 · Deploy to Azure

```powershell
winget install Microsoft.AzureCLI      # Windows
```
```bash
brew install azure-cli                 # macOS
```
```bash
az bicep install
az login
```

```bash
# Preview. Creates nothing.
az deployment group what-if --resource-group rg-di-dev \
  --template-file infra/main.bicep --parameters infra/dev.bicepparam

# Apply.
az deployment group create --resource-group rg-di-dev \
  --template-file infra/main.bicep --parameters infra/dev.bicepparam
```

Normally you never run these by hand: pushing to `main` does it through GitHub
Actions. They are here for debugging a failed deployment.

---

## What does not travel between machines

Nothing sensitive is in the repository, by design — so a new machine
re-authenticates rather than copying anything:

| | |
|---|---|
| `gh auth login` | Per machine |
| `az login` | Per machine |
| Azure federated credentials | Server-side, machine-independent |
| Managed identity | Server-side, machine-independent |
| GitHub secrets | Server-side, machine-independent |

**You never copy a credential between machines.** That is the point of the
OIDC and managed-identity setup — see the [Infrastructure](../README.md#infrastructure)
section.

The local SA password in `compose.yaml` is a throwaway container credential, not
a secret.

---

## Troubleshooting

### `docker compose up` fails to build

Docker Desktop is not running, or the engine has not finished starting. On
Windows, `docker ps` returning
`failed to connect to the docker API at npipe:...` means exactly this.

### SQL Server container exits immediately on Apple Silicon

Almost always memory. SQL Server needs ~2 GB and is killed without a clear
message below that.

```
Settings → Resources → Memory → 4 GB or more
```

Then `docker compose down && docker compose up`.

### SQL Server is painfully slow on Apple Silicon

Rosetta is off. **Settings → General → "Use Rosetta for x86_64/amd64 emulation
on Apple Silicon"**. The difference is large.

### `/health` works but `/health/ready` returns 503

The database is not reachable yet. Locally that means SQL is still starting —
wait and retry. Against Azure it usually means the serverless database is
resuming from auto-pause, which takes 30–60 seconds and is **not** an incident.
See [ADR 003](adr/003-liveness-separate-from-readiness.md).

### `dotnet` refuses to run: SDK not found

`global.json` pins the SDK band and your installed SDK is outside it.

```bash
dotnet --list-sdks     # need a 10.0.4xx entry
```

On Apple Silicon this is usually the x64 installer having been used instead of
Arm64.

### Port already in use

```bash
docker compose down                       # a previous stack is still up
lsof -i :8080                             # macOS / Linux
netstat -ano | findstr :8080              # Windows
```

### `Login failed for user 'sa'` from `dotnet ef`

`ConnectionStrings__Default` is not set in that shell, so the design-time
fallback was used — and it is deliberately not a working credential. See
[Migrations](#migrations).

### An ingest returns 502 Upstream unavailable

The upstream rate-limited you, which the archive does under light load. Nothing
is corrupted. Wait, then retry. See the [runbook](runbook.md).

---

## Quick reference

```bash
docker compose up --build          # run everything
docker compose up sql -d           # database only
docker compose down -v             # stop and wipe
docker compose logs -f api         # follow the API log

dotnet test                        # 53 tests
dotnet build                       # zero warnings expected

curl localhost:8080/health
curl localhost:8080/health/ready
curl localhost:8080/api/v1/dead-letter/summary
curl localhost:8080/api/v1/sync/status
```

| Where | URL |
|---|---|
| Local API | <http://localhost:8080> |
| Local OpenAPI | <http://localhost:8080/openapi/v1.json> |
| Local SQL | `localhost,1433` · `sa` / `Local_Dev_P@ss1` |
| Deployed | <https://app-di-dev-yqeffct3kgtau.azurewebsites.net> |
