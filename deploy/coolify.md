# Deployment auf Coolify

Schritt-für-Schritt-Anleitung für den Betrieb von SoloCRM auf einem eigenen Server mit Coolify (v4, Traefik als Reverse Proxy).
Hintergrund und Entscheidungen: `docs/adr/0009-deployment-coolify.md`, `docs/SPEC.md` Kap. 7.6.

## Überblick

```
git push main ──► GitHub Actions "Build & Test" ──(grün)──► "Docker": Image → ghcr.io/guido-altmann/solocrm
                                                                   │
                                                                   └─► Coolify-Deploy-Webhook
Coolify: pull Image ──► Container-Start: entrypoint.sh ──► efbundle (Migrationen) ──► SoloCrm.Web (Port 8080)
```

- **Image-Build:** ausschließlich in GitHub Actions (`.github/workflows/docker.yml`), nur für Commits auf `main` mit grünem CI. Tags: `latest` und `sha-<7 Zeichen>`.
- **Migrationen:** laufen beim Containerstart über `deploy/entrypoint.sh`. Schlägt eine Migration fehl, startet die App nicht, der Healthcheck bleibt rot und Coolify behält den alten Container.

## Voraussetzungen

- Server in der EU mit installiertem Coolify, Domain mit DNS-A/AAAA-Record auf den Server (z. B. `crm.example.de`)
- S3-kompatibler Storage für Backups (Bucket, Access Key, Secret Key, Endpoint, Region)
- Mindestens ein erfolgreicher Lauf des Workflows **Docker**, damit das Image in GHCR existiert

## 1. GHCR-Package prüfen

Nach dem ersten Lauf von **Docker** unter *GitHub → Profil → Packages → solocrm → Package settings*:

- **Visibility** auf *Public* stellen (neue Packages sind standardmäßig privat, auch bei öffentlichem Repo). Dann braucht Coolify keine Registry-Credentials.
- Unter *Manage Actions access* muss das Repo `solocrm` Schreibrechte haben (wird beim ersten Push aus dem Workflow automatisch verknüpft).

## 2. S3-Storage in Coolify anlegen

*Storages → + Add*: Name (z. B. `backup-s3`), Endpoint, Bucket, Region, Access Key, Secret Key → **Validate Connection**.

## 3. Postgres-Ressource

1. *Projects → (Projekt anlegen, z. B. `solocrm`) → Environment `production` → + New → Database → PostgreSQL*
2. Image: `postgres:17-alpine` (gleiche Major-Version wie Dev und Testcontainers)
3. Konfiguration:
   - **Username / Password:** eigene Werte, Passwort generieren lassen
   - **Initial Database:** `solocrm`
   - **Make it publicly available:** *aus* (die App erreicht die DB über das interne Docker-Netz)
4. **Start**
5. Den Wert **Postgres URL (internal)** notieren, z. B. `postgres://solocrm:<pw>@<container-uuid>:5432/solocrm`. Daraus wird im nächsten Schritt der Npgsql-Connection-String.

### Backups

In der Datenbank-Ressource unter *Backups → + Add*:

- **Frequency:** `0 3 * * *` (täglich 03:00 Uhr Serverzeit)
- **Save to S3:** an, Storage `backup-s3`
- **Aufbewahrung:** S3 30 Tage (SPEC Kap. 6), lokal z. B. 7 Backups
- Datenbank: `solocrm`

Anschließend **Backup Now** auslösen und prüfen, dass die Datei im Bucket ankommt.

## 4. App-Ressource

1. Im selben Projekt/Environment: *+ New → Docker Image*
2. Image: `ghcr.io/guido-altmann/solocrm:latest`
3. **General**
   - **Domains:** `https://crm.example.de` (mit `https://`; Coolify holt dann automatisch ein Let's-Encrypt-Zertifikat und leitet HTTP auf HTTPS um)
   - **Ports Exposes:** `8080`
4. **Persistent Storage** → *+ Add → Volume Mount*
   - Name: `solocrm-keys`
   - Destination Path: `/app/keys`

   Hier liegen die Data-Protection-Keys. Ohne dieses Volume ist man nach jedem Deploy ausgeloggt und Antiforgery-Tokens werden ungültig.
   Ein benanntes Volume übernimmt beim ersten Start die Rechte aus dem Image (`app`, UID 1654). Bei einem **Bind Mount** (Host-Verzeichnis) muss das Verzeichnis vorher per `chown 1654:1654` beschreibbar gemacht werden.
5. **Healthcheck**
   - Enabled, Method `GET`, Scheme `http`, Host `localhost`, Port `8080`, Path `/health/ready`
   - Return Code `200`, Start Period `30` s (Migrationen laufen vor dem App-Start)
6. **Environment Variables** (alle als *Runtime*, Passwörter zusätzlich als *Secret/Locked*):

   | Variable | Wert | Hinweis |
   |---|---|---|
   | `ConnectionStrings__Crm` | `Host=<container-uuid>;Port=5432;Database=solocrm;Username=solocrm;Password=<pw>` | Npgsql-Format, **nicht** die `postgres://`-URL; Host = Container-Name aus der internen URL |
   | `Admin__Email` | eigene Adresse | nur relevant, solange noch kein User existiert |
   | `Admin__InitialPassword` | starkes Passwort | nach dem ersten Login ändern, danach kann die Variable entfernt werden |
   | `Serilog__MinimumLevel__Default` | `Information` | optional, zur Fehlersuche `Debug` |
   | `App__BaseUrl` | `https://crm.example.de` | für spätere absolute Links (Webhooks, Mails) |

   `ASPNETCORE_ENVIRONMENT` nicht setzen (Default `Production`: HSTS, keine Developer-Exception-Page).
   `ForwardedHeaders__KnownNetworks` ist per Default auf die privaten Netze (u. a. `10.0.0.0/8` des Coolify-Netzwerks) gesetzt und muss nur angepasst werden, wenn Traefik in einem anderen Netz läuft.
7. **Deploy**. In den Logs des Containers sollten zuerst `Applying database migrations...` und danach die JSON-Logs der App erscheinen.

WebSockets (für die Blazor-SignalR-Verbindung) leitet Traefik ohne weitere Konfiguration durch.

## 5. Automatisches Deployment per Webhook

1. In Coolify unter *Keys & Tokens → API Tokens* einen Token mit Berechtigung **deploy** erzeugen.
2. In der App-Ressource unter *Webhooks* die **Deploy Webhook**-URL kopieren.
3. Im GitHub-Repo unter *Settings → Secrets and variables → Actions* anlegen:
   - `COOLIFY_WEBHOOK_URL`: die Webhook-URL
   - `COOLIFY_API_TOKEN`: der Token

Ab jetzt stößt der Workflow **Docker** nach jedem erfolgreichen Push das Deployment an. Ohne diese Secrets wird nur das Image gepusht; das Deployment erfolgt dann manuell per *Redeploy* in Coolify.

## 6. Verifikation nach dem ersten Deployment

- [ ] `https://crm.example.de/health/ready` liefert `200` (ohne Login erreichbar)
- [ ] Aufruf per `http://` wird auf `https://` umgeleitet, Zertifikat gültig
- [ ] Login mit `Admin__Email` / `Admin__InitialPassword` funktioniert, danach Passwort ändern
- [ ] `/contacts`: Kontakt per Quick-Add (`N`) anlegen, anschließend über die Suche finden
- [ ] Response-Header enthält `Strict-Transport-Security`
- [ ] Kein Redirect-Loop und Auth-Cookie mit Flag `Secure` (Forwarded Headers greifen, die App erkennt HTTPS hinter Traefik)
- [ ] Container-Logs sind JSON und enthalten keine E-Mail-Adressen im Klartext

### Redeploy-Test (persistente Data-Protection-Keys)

1. Eingeloggt bleiben, Browser-Tab offen lassen.
2. In Coolify **Redeploy** auslösen (oder einen Commit auf `main` pushen).
3. Nach dem Neustart die Seite neu laden: **Die Sitzung muss erhalten bleiben**, kein erneuter Login.
4. Optional im Container prüfen: `ls -l /app/keys` zeigt dieselbe `key-*.xml` wie vor dem Redeploy.

## 7. Backup & Restore-Test

Einmalig durchspielen, damit der Restore-Weg nachweislich funktioniert. Coolify erzeugt Backups mit `pg_dump` im Custom-Format.

1. In der DB-Ressource **Backup Now**, danach die neueste Datei aus dem S3-Bucket (oder unter *Backups → Executions*) herunterladen.
2. Datei in den Postgres-Container kopieren und in eine separate Test-DB einspielen (auf dem Server):

   ```bash
   DB=<postgres-container-uuid>
   docker cp ./backup.dmp "$DB":/tmp/backup.dmp
   docker exec "$DB" createdb -U solocrm solocrm_restore_test
   docker exec "$DB" pg_restore -U solocrm -d solocrm_restore_test --no-owner --no-acl /tmp/backup.dmp
   ```

3. Inhalt prüfen:

   ```bash
   docker exec "$DB" psql -U solocrm -d solocrm_restore_test \
     -c 'select count(*) from contacts;' \
     -c 'select "MigrationId" from "__EFMigrationsHistory";'
   ```

   Die Anzahl der Kontakte und die Migrationen müssen der Produktionsdatenbank entsprechen.
4. Aufräumen:

   ```bash
   docker exec "$DB" dropdb -U solocrm solocrm_restore_test
   docker exec "$DB" rm /tmp/backup.dmp
   ```

Datum und Ergebnis des Restore-Tests in ADR-009 festhalten.

**Echter Restore (Notfall):** App-Ressource stoppen, `solocrm` droppen und neu anlegen, `pg_restore` wie oben in `solocrm` ausführen, App starten. `efbundle` spielt beim Start nur noch fehlende Migrationen ein.

## Rollback

In der App-Ressource das Image-Tag von `latest` auf ein früheres `sha-<commit>` ändern und neu deployen. Achtung: Migrationen werden nicht automatisch zurückgerollt. Enthält der fehlerhafte Stand eine Migration, die nicht abwärtskompatibel ist, ist der Restore aus dem Backup der sichere Weg.
