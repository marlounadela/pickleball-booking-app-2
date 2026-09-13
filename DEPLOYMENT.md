# Picklebook — Vercel Deployment Guide

This project is **ASP.NET Core (Net 8, Razor Components — server-rendered interactive UI) + SQLite**.
It runs on Vercel as a **Container Image** (`Dockerfile.vercel`) and is served by Vercel Functions
(Fluid compute). Static hosting/routing presets do not apply — there is no JS build.

---

## Detected stack (auto-detected from the repository)

| Item             | Value                                                    |
|------------------|----------------------------------------------------------|
| Framework        | ASP.NET Core — Razor Components (InteractiveServer)      |
| Runtime          | .NET 8 (`net8.0`, package refs `8.0.30`)                 |
| Package manager  | NuGet (`.csproj` `PackageReference`)                     |
| Build command    | `dotnet build -c Release` (inside the container image)   |
| Output directory | `bin/Release/net8.0/` (baked into the container)          |
| Database         | SQLite via `Microsoft.EntityFrameworkCore.Sqlite`         |

---

## How the deployment is wired

1. **`Dockerfile.vercel`** — Vercel auto-detects this filename and treats the project as a Container
   Image (beta). The image is built with the **Linux** .NET SDK, then the slim `dotnet/runtime:8.0`
   image ships `bin/Release/net8.0/`. The process **must listen on `$PORT`** (Vercel injects it) —
   ASP.NET Core honors `PORT`, so the app binds correctly with no code changes.
2. **`vercel.json`** — only sets CDN caching + basic security headers. **No rewrites/redirects are
   configured**: the container catches all routes, including `/{yard-slug}` public sites,
   `/Account/*` auth, and the admin app. Adding SPA-style rewrites here would break routing.
3. **`appsettings.json`** — SQLite `DataSource` uses **forward slashes** (`Data/app.db`) so it works
   identically on Windows, macOS, and Linux.

Configuration precedence: environment variables > `appsettings.json`.

---

## Required environment variables (Vercel project settings)

Production boots in `Production` mode. On first boot it runs EF migrations, then seeds credentials.
**Demo credentials are never created in production** unless you opt in.

| Variable                | Required | Purpose                                                            |
|-------------------------|----------|--------------------------------------------------------------------|
| `SUPER_ADMIN_EMAIL`     | **Yes\*** | Email for the platform SuperAdmin created on first boot            |
Other (recommended) Vercel settings:

- **Root Directory:** `.`
- **Framework Preset:** leave as "Other" — do **not** select a JS framework (there is none).
- **Build/Output:** not used for container deploys; the image build is driven by `Dockerfile.vercel`.

---

## Important: SQLite storage on Vercel is EPHEMERAL

Vercel Functions (including Container Images) run on **ephemeral** filesystems. The SQLite database
file is recreated by migrations+seed on each fresh instance and **changes are lost** when the
instance cools down / on redeploys. The app, its features, business logic, and API behavior are fully
preserved — this is a storage caveat, not a code defect.

For real persistence, connect a managed SQLite-compatible store and point `DATABASE_URL` at it:

- Turso / libSQL (SQLite-compatible, HTTP — recommended).
- Any Postgres/MySQL provider requires refactoring `ApplicationDbContext` to the matching EF Core
  driver (business/domain code is database-agnostic).

---

## Deploying

1. `vercel login`
2. `vercel link`
3. Set secrets (dashboard or CLI): `SUPER_ADMIN_EMAIL`, `SUPER_ADMIN_PASSWORD`
4. `vercel --prod`

Or connect the GitHub repo in the Vercel dashboard (container images are built during the build step).

---

## Local verification

```bash
dotnet build -c Release          # Windows/macOS/Linux — must be 0 warnings / 0 errors
dotnet bin\Release\net8.0\Picklebook.dll   # (working dir = project root)
# then open http://localhost:5000
```

---

## Infrastructure / hosting notes

- `Program.cs` installs the forwarded-header trust filter in production so HTTPS redirection, HSTS,
  and Secure cookies work behind Vercel's TLS-terminating edge.
- The Docker image runs as a non-root user (`picklebook`) — the app dir is writable for the SQLite
  file. To persist across restarts use a volume (Vercel has no persistent volumes — see the SQLite
  note above).
- No secrets are committed. `appsettings.json` contains only non-secret defaults. SMTP credentials
  are read **only** from environment variables.
| `SUPER_ADMIN_PASSWORD`  | **Yes\*** | Strong password (≥8 chars, min. 1 digit — validated by Identity)   |
| `SEED_DEMO_DATA`        | No       | Set `true` to create demo users/yards **with well-known passwords**. Only for staging/demo! |
| `DATABASE_URL`          | No       | SQLite connection string override: `DataSource=/tmp/app.db;Cache=Shared` |
| `AppSettings:RootDomain`| No       | Tenant routing root (default `picklebook.com`). For a preview deploy set it to your `*.vercel.app` wildcard domain. |
| `EmailSettings:Host`    | No       | SMTP host — enables real emails (in-app notifications work without it). |
| `EmailSettings:Port`    | No       | SMTP port (default `587`)                                          |
| `EmailSettings:Username`| No       | SMTP username                                                       |
| `EmailSettings:Password`| No       | SMTP password                                                       |
| `EmailSettings:From`    | No       | From address (default `no-reply@picklebook.com`)                   |

**\*** Required only when `SEED_DEMO_DATA` is unset/false. If neither is provided the app fails fast at
startup (fail-fast — it logs an explicit message telling you what to set). No hard-coded admin
password is ever created on production.