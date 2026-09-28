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

- **Image-Build (entschieden 2026-09-28):** GitHub Actions baut das Image nur für Commits auf `main` mit grünem CI (`docker.yml` per `workflow_run`) und pusht nach `ghcr.io/guido-altmann/solocrm` (Tags `latest`, `sha-<commit>`). Coolify zieht das öffentliche Image; das Deployment wird per Coolify-Deploy-Webhook angestoßen. Begründung: Nur getestete Images werden deployt, der Server muss keine SDK-Builds ausführen, Rollback per Image-Tag.
- **Migrationen (entschieden 2026-09-28):** `deploy/entrypoint.sh` führt `efbundle` vor dem App-Start im selben Container aus. Begründung: unabhängig von Coolify-Features, lokal mit `docker run` identisch testbar; schlägt eine Migration fehl, startet die App nicht und der Healthcheck verhindert den Wechsel auf den neuen Container.
- Betriebsanleitung: `deploy/coolify.md`.

## Konsequenzen
- Status auf *Accepted* setzen, sobald der Walking Skeleton erfolgreich deployt und ein Restore getestet ist.
