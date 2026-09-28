# ADR-009: Deployment auf Coolify als Docker-Container

**Status:** Accepted · **Datum:** 2026-09-25 (akzeptiert 2026-09-28) · **Entscheider:** Guido Altmann

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
- **Rolling Updates (2026-09-28):** Während eines Redeploys laufen alter und neuer Container kurz parallel, Traefik verteilt ohne Sticky Sessions auf beide. Der Blazor-Client verbindet sich deshalb ohne SignalR-Negotiation direkt per WebSocket (`wwwroot/js/blazor-start.js`), sodass die Circuit-Verbindung aus genau einer Anfrage besteht. Verworfen: Sticky Sessions in Traefik (zusätzliche Coolify-Konfiguration außerhalb des Repos, hilft nicht, wenn der alte Container verschwindet); Fallback-Transports (SSE/Long Polling) entfallen dadurch bewusst, WebSockets sind über Traefik verfügbar.
- Betriebsanleitung: `deploy/coolify.md`.

## Verifikation (2026-09-28)
- Deployment über GitHub Actions → GHCR → Coolify läuft; HTTPS unter eigener Domain, Login und Quick-Add in Produktion geprüft.
- Redeploy ohne Logout (Data-Protection-Keys auf Volume `/app/keys` persistent), UI nach Rolling Update ohne manuellen Reload interaktiv.
- Backup über Coolify nach S3 erstellt und in eine separate Test-DB zurückgespielt (`pg_restore`); Kontakte und Migrationshistorie vollständig.

## Konsequenzen
- Leichter: Deployt werden nur getestete Images; Rollback durch Wechsel auf einen älteren `sha-<commit>`-Tag. Der Server braucht kein .NET SDK.
- Leichter: Migrationen laufen lokal (`docker run`) und in Produktion identisch; eine fehlgeschlagene Migration verhindert den Containerwechsel.
- Schwerer: Migrationen müssen abwärtskompatibel sein, weil während eines Rolling Updates alter und neuer Container kurz parallel gegen dieselbe DB laufen (Expand/Contract bei destruktiven Schemaänderungen).
- Schwerer: Ohne WebSockets funktioniert die UI nicht mehr (kein SSE/Long-Polling-Fallback); ein Proxy vor Traefik muss WebSockets durchreichen.
- Das Volume `/app/keys` ist betriebskritisch: Geht es verloren, werden alle Sessions ungültig und mit Data Protection verschlüsselte Secrets (ab It. 5) unlesbar. Die Postgres-Backups decken es nicht ab; es muss separat gesichert werden.
- Status *Accepted* seit 2026-09-28 nach erfolgreichem Deployment, Redeploy ohne Logout und verifiziertem Restore (siehe Verifikation).
