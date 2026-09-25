# ADR-009: Deployment auf Coolify als Docker-Container

**Status:** Proposed · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Self-Hosting auf eigenem Server mit Coolify (Traefik als Reverse Proxy). Die Postgres-DB wird ebenfalls in Coolify betrieben.

## Entscheidung
- Multi-Stage-Dockerfile, Runtime `mcr.microsoft.com/dotnet/aspnet:10.0`, non-root, Port 8080.
- Data-Protection-Keys auf persistentem Volume `/app/keys`.
- `UseForwardedHeaders` für Traefik; WebSockets für SignalR.
- Migrationen über ein `efbundle` im Image, ausgeführt vor dem App-Start.
- Health Checks `/health/live` und `/health/ready`.
- Postgres-Backups über Coolify-Scheduler nach S3.

## Offene Punkte (im Walking Skeleton klären)
- Image-Build: GitHub Actions → GHCR **oder** Build durch Coolify aus dem Repo.
- Migrationen: Startskript (`efbundle && dotnet SoloCrm.Web.dll`) **oder** Coolify-Pre-Deployment-Command.

## Konsequenzen
- Status auf *Accepted* setzen, sobald der Walking Skeleton erfolgreich deployt und ein Restore getestet ist.
