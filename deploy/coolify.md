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

## Wo werden Befehle ausgeführt?

In dieser Anleitung gibt es zwei Arten von Befehlen:

- **Container-Terminal (Coolify-UI):** in der jeweiligen Ressource unter *Terminal*. Man ist bereits *im* Container, Befehle werden direkt eingegeben, **ohne** `docker exec …`. Beispiel im Postgres-Container:
  ```sh
  psql -U solocrm -d solocrm -c 'select user_name, email_confirmed from "AspNetUsers";'
  ```
- **Server-Shell:** per SSH auf dem Host, auf dem Coolify läuft (oder in Coolify unter *Servers → Terminal*). Hier wird ein Container per `docker exec <container> …` angesprochen. Nötig für alles, was Dateien zwischen Host und Container bewegt (`docker cp`). Den Container-Namen zeigt `docker ps`; er entspricht der UUID der Ressource in Coolify.

`solocrm` steht jeweils für den in der Postgres-Ressource konfigurierten Benutzer bzw. die Datenbank.

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

   Hier liegen die Data-Protection-Keys. Ohne dieses Volume ist man nach jedem Deploy ausgeloggt und Antiforgery-Tokens werden ungültig. Seit Iteration 5 sind damit auch die **Webhook-Secrets** verschlüsselt: Geht das Volume verloren, schlägt jede Zustellung mit „Secret nicht lesbar“ fehl, und die Secrets müssen in den Einstellungen neu erzeugt und in n8n eingetragen werden. Die Postgres-Backups sichern das Volume **nicht**; es muss separat gesichert werden (ADR-009).
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
   | `App__TimeZone` | `Europe/Berlin` | optional (Default `Europe/Berlin`); IANA-Zeitzone für „Heute“, „überfällig“ und angezeigte Uhrzeiten. Ein ungültiger Wert verhindert den Start |
   | `Webhooks__AllowedHttpHosts` | `n8n` | optional; kommagetrennte Hosts, an die Webhooks per `http` gehen dürfen (z. B. n8n im selben Docker-Netz). Alle anderen Ziele brauchen `https` |
   | `Outbox__PollingInterval` | `00:00:10` | optional; wie oft die Outbox auf neue Ereignisse geprüft wird. Weitere Werte: `Outbox__BatchSize` (20), `Outbox__LeaseDuration` (5 min), `Outbox__Retention` (30 Tage) |

   **Sonderzeichen in Werten:** Coolify reicht die Variablen über eine Docker-Compose-`.env`-Datei weiter. Dabei werden `$` (Variablen-Interpolation) und `\` (Escape-Zeichen, wird z. B. verdoppelt) verändert, Anführungszeichen (`"`, `'`, `` ` ``) und Leerzeichen am Rand können mit in den Wert geraten. Diese Zeichen in Passwörtern vermeiden oder die Variable als **Is Literal** markieren. Was tatsächlich ankommt, zeigt `printenv <Variable>` im Terminal des App-Containers (siehe unten).

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
- [ ] UI ist interaktiv: Hell-/Dunkelmodus-Umschalter reagiert (sonst siehe *Fehlersuche*)
- [ ] `/contacts`: Kontakt per Quick-Add (`N`) anlegen, anschließend über die Suche finden
- [ ] Response-Header enthält `Strict-Transport-Security`
- [ ] Kein Redirect-Loop und Auth-Cookie mit Flag `Secure` (Forwarded Headers greifen, die App erkennt HTTPS hinter Traefik)
- [ ] Container-Logs sind JSON und enthalten keine E-Mail-Adressen im Klartext

### Integration (ab Iteration 5)
- [ ] *Einstellungen → API-Keys*: Key erzeugen; `curl -H "X-Api-Key: scrm_…" https://crm.example.de/api/v1/stages` liefert `200`, ohne Header `401`
- [ ] `https://crm.example.de/scalar/v1` zeigt die API-Referenz nur nach dem Login (sonst Weiterleitung zum Login)
- [ ] *Einstellungen → Webhooks*: Webhook auf n8n anlegen, *Test senden* ist erfolgreich, die Signatur wird in n8n geprüft ([docs/n8n-integration.md](../docs/n8n-integration.md))
- [ ] Stage-Wechsel einer Anfrage erscheint im Versandprotokoll mit HTTP 2xx
- [ ] Ältere Outbox-Nachrichten aus Iteration 2–4 werden als verarbeitet markiert, aber nicht gesendet (Webhook erhält nur Ereignisse ab seiner Anlage)

### Redeploy-Test (persistente Data-Protection-Keys)

1. Eingeloggt bleiben, Browser-Tab offen lassen.
2. In Coolify **Redeploy** auslösen (oder einen Commit auf `main` pushen).
3. Nach dem Neustart die Seite neu laden: **Die Sitzung muss erhalten bleiben**, kein erneuter Login.
4. Optional im Container prüfen: `ls -l /app/keys` zeigt dieselbe `key-*.xml` wie vor dem Redeploy.

## 7. Backup & Restore-Test

Einmalig durchspielen, damit der Restore-Weg nachweislich funktioniert. Coolify erzeugt Backups mit `pg_dump` im Custom-Format.

1. In der DB-Ressource **Backup Now**, danach die neueste Datei aus dem S3-Bucket (oder unter *Backups → Executions*) herunterladen.
2. Datei vom lokalen Rechner auf den Server kopieren (**lokales Terminal**):

   ```bash
   scp ./<datei>.dmp user@server:/tmp/backup.dmp
   ```

3. Datei in den Postgres-Container kopieren und in eine separate Test-DB einspielen (**Server-Shell**, siehe oben):

   ```bash
   DB=<postgres-container-uuid>
   docker cp /tmp/backup.dmp "$DB":/tmp/backup.dmp
   rm /tmp/backup.dmp
   docker exec "$DB" createdb -U solocrm solocrm_restore_test
   docker exec "$DB" pg_restore -U solocrm -d solocrm_restore_test --no-owner --no-acl /tmp/backup.dmp
   ```

4. Inhalt prüfen:

   ```bash
   docker exec "$DB" psql -U solocrm -d solocrm_restore_test \
     -c 'select count(*) from contacts;' \
     -c 'select "MigrationId" from "__EFMigrationsHistory";'
   ```

   Die Anzahl der Kontakte und die Migrationen müssen der Produktionsdatenbank entsprechen.
5. Aufräumen:

   ```bash
   docker exec "$DB" dropdb -U solocrm solocrm_restore_test
   docker exec "$DB" rm /tmp/backup.dmp
   ```

Datum und Ergebnis des Restore-Tests in ADR-009 festhalten.

**Echter Restore (Notfall):** App-Ressource stoppen, `solocrm` droppen und neu anlegen, `pg_restore` wie oben in `solocrm` ausführen, App starten. `efbundle` spielt beim Start nur noch fehlende Migrationen ein.

## 8. Sicherung von `/app/keys` (Data-Protection-Keys)

Das Volume `solocrm-keys` enthält den Schlüsselring von ASP.NET Core Data Protection. Damit werden Login-Cookies und Antiforgery-Tokens signiert und die **Webhook-Secrets** verschlüsselt. Die Postgres-Backups enthalten es nicht (ADR-009). Ohne Sicherung bedeutet ein verlorenes Volume: alle Sitzungen ungültig (harmlos, neu anmelden) und alle Webhook-Secrets unlesbar (in den Einstellungen neu erzeugen und in n8n eintragen).

Die Schlüsseldateien (`key-<guid>.xml`) liegen im Container **unverschlüsselt** auf der Platte. Eine Kopie ist so schützenswert wie ein Passwort: nur verschlüsselt und nicht im selben Bucket wie die Datenbank-Backups ablegen.

**Wann sichern?** Nach dem ersten Start und danach monatlich. Data Protection erzeugt etwa alle 90 Tage einen neuen Schlüssel; ältere bleiben zum Entschlüsseln erhalten, eine monatliche Sicherung erfasst also jeden neuen Schlüssel rechtzeitig.

### Sichern (Server-Shell)

```bash
# Name des Volumes ermitteln (Coolify stellt die Ressourcen-UUID voran)
docker volume ls | grep solocrm-keys
VOLUME=<name-aus-der-liste>

# Archiv erzeugen und mit einer Passphrase verschlüsseln
docker run --rm -v "$VOLUME":/keys:ro -v /root:/backup alpine \
  tar czf /backup/solocrm-keys.tar.gz -C /keys .
openssl enc -aes-256-cbc -pbkdf2 -salt -in /root/solocrm-keys.tar.gz -out /root/solocrm-keys-$(date +%F).tar.gz.enc
rm /root/solocrm-keys.tar.gz
```

Die `.enc`-Datei auf den lokalen Rechner holen (`scp user@server:/root/solocrm-keys-*.tar.gz.enc .`), z. B. im Passwortmanager neben der Passphrase ablegen und auf dem Server löschen.

### Wiederherstellen (Server-Shell)

1. App-Ressource in Coolify **stoppen**.
2. Archiv entschlüsseln und in das (leere oder neu angelegte) Volume entpacken; die Rechte müssen dem Benutzer `app` (UID 1654) gehören:

   ```bash
   openssl enc -d -aes-256-cbc -pbkdf2 -in /root/solocrm-keys-<datum>.tar.gz.enc -out /root/solocrm-keys.tar.gz
   docker run --rm -v "$VOLUME":/keys -v /root:/backup alpine \
     sh -c 'tar xzf /backup/solocrm-keys.tar.gz -C /keys && chown -R 1654:1654 /keys'
   rm /root/solocrm-keys.tar.gz
   ```

3. App **starten**.
4. Prüfen: Eine vor dem Verlust angemeldete Browser-Sitzung ist weiterhin angemeldet (kein erneuter Login), und *Einstellungen → Webhooks → Test senden* ist erfolgreich (das Secret ist lesbar).

**Getestet** (Iteration 6, lokal mit dem Produktions-Image): Webhook angelegt, Volume gesichert, gelöscht und aus dem Archiv wiederhergestellt; danach blieb die Sitzung erhalten und der Test-Ping wurde mit HTTP 200 zugestellt. Gegenprobe mit leerem Volume: Sitzung verloren. Der Test auf dem Server steht in der Checkliste unten.

## 9. Verifikation nach Iteration 6 (DSGVO & 2FA)

- [ ] *Konto → Zwei-Faktor → Authenticator-App einrichten*: QR-Code mit der App scannen, Code bestätigen, die Wiederherstellungscodes sicher ablegen
- [ ] Abmelden, Login mit Passwort und Authenticator-Code funktioniert
- [ ] Abmelden, Login mit einem Wiederherstellungscode funktioniert (der Code ist danach verbraucht; bei Bedarf neue Codes erzeugen)
- [ ] Optional: Passkey hinzufügen und damit anmelden
- [ ] Test-Kontakt anlegen, *Daten exportieren (DSGVO)* lädt eine JSON-Datei, *Endgültig löschen (DSGVO)* entfernt ihn; ein Webhook mit `contact.deleted` erhält nur die Id
- [ ] Sicherung von `/app/keys` wie in Abschnitt 8 angelegt und einmal wiederhergestellt (Sitzung bleibt, Webhook-Test erfolgreich)

## Fehlersuche

**Nach der Anmeldung wird kein Code abgefragt, obwohl 2FA eingerichtet ist**
- Bei der letzten Anmeldung war „Diesem Browser vertrauen“ aktiv. Unter *Konto → Zwei-Faktor → Diesen Browser vergessen* zurücksetzen.

**Authenticator-Code wird immer abgelehnt**
- Die Uhrzeit auf Smartphone und Server muss stimmen (TOTP verträgt nur wenige Sekunden Abweichung über das 30-s-Fenster hinaus). Auf dem Server `date -u` prüfen.
- Kein Zugriff mehr auf App und Wiederherstellungscodes: Im Postgres-Terminal `psql -U solocrm -d solocrm -c 'update "AspNetUsers" set two_factor_enabled = false;'` deaktiviert 2FA; danach in der App neu einrichten.

**Login meldet „Invalid login attempt“, obwohl der Admin laut Log angelegt wurde**
- Meist wurde das Passwort beim Weiterreichen verändert (siehe *Sonderzeichen in Werten*). Im Terminal des App-Containers `printenv Admin__InitialPassword` ausführen und genau diesen Wert zum Login verwenden, danach das Passwort in der App ändern.
- Steht im Log `Admin seed skipped: a user already exists`, wurden `Admin__*` ignoriert, weil schon ein Benutzer existiert (z. B. nach nachträglich geänderten Env-Werten). Neu anlegen lassen: im Postgres-Terminal `psql -U solocrm -d solocrm -c 'delete from "AspNetUsers";'`, dann die App neu starten. Nur solange keine weiteren Benutzerdaten existieren.

**Seiten werden angezeigt, aber kein Button reagiert**
- Die interaktive Blazor-Verbindung kommt nicht zustande. In den Browser-DevTools (Netzwerk) prüfen:
  - `_framework/blazor.web.<hash>.js` muss mit `200` laden (nicht `302` auf die Login-Seite; das deutete auf ein Image ohne Framework-Skript hin, der Docker-Build prüft das inzwischen).
  - `_blazor/negotiate` muss `200` liefern und die anschließende WebSocket-Verbindung (`_blazor?id=…`) mit `101` aufgebaut werden.
- Ein vorgeschalteter Proxy/CDN (z. B. Cloudflare) darf WebSockets nicht blockieren und Skripte nicht umschreiben (Rocket Loader aus). Da der Client ausschließlich WebSockets nutzt (ADR-009), gibt es keinen Fallback auf andere Transports.

**Nach einem Redeploy ist die Liste leer („Noch keine Kontakte vorhanden.“), erst F5 zeigt die Daten**
- Symptom einer Circuit-Verbindung, die während des Rolling Updates zwischen altem und neuem Container aufgeteilt wurde. Behoben durch den WebSocket-only-Start in `wwwroot/js/blazor-start.js`; tritt es erneut auf, im Seitenquelltext prüfen, dass `blazor.web.js` mit `autostart="false"` und direkt danach `js/blazor-start.js` eingebunden sind.

## Rollback

In der App-Ressource das Image-Tag von `latest` auf ein früheres `sha-<commit>` ändern und neu deployen. Achtung: Migrationen werden nicht automatisch zurückgerollt. Enthält der fehlerhafte Stand eine Migration, die nicht abwärtskompatibel ist, ist der Restore aus dem Backup der sichere Weg.
