# Iteration 1 – Walking Skeleton

**Ziel:** Ein minimales, aber vollständiges System läuft end-to-end: Code → CI → Container → Coolify → HTTPS → Postgres mit Backup. Fachlich gibt es nur Login und eine einfache Kontaktliste mit Quick-Add.

**Stories:** US-01 AK1–AK2 (Kontakt per Quick-Add; AK3 folgt in Iteration 2), US-21 AK1 (Login, Single-User)
**Referenzen:** `docs/SPEC.md` Kap. 2.2, 2.3 (Contact), 7; ADR-001 bis ADR-004, ADR-009

---

## Schritt 1 – Solution-Grundgerüst
- [x] `SoloCrm.sln` mit den Projekten gemäß SPEC 7.2 (src + tests) anlegen
- [x] `SoloCrm.Web` aus dem Template `blazor` mit `--interactivity Server --auth Individual` erzeugen und auf PostgreSQL umstellen (SQLite/SQL-Server-Reste entfernen)
- [x] `Directory.Build.props`: `net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `LangVersion latest`
- [x] `Directory.Packages.props` (Central Package Management) mit allen Paketen dieser Iteration
- [x] `.editorconfig`, `.gitignore`, `global.json` (SDK-Version pinnen)
- [x] Projektreferenzen gemäß Abhängigkeitsregel
- [x] MudBlazor einbinden (Provider, Theme mit Hell-/Dunkelmodus, `MainLayout` mit Navigation gemäß SPEC 3.4; Identity-Seiten dürfen vorerst im Template-Stil bleiben)

## Schritt 2 – Querschnitts-Bausteine
- [x] `Domain`: Basisklasse `Entity` (Id als UUIDv7, `CreatedAt`, `UpdatedAt`, Domain-Event-Liste)
- [x] `Application`: `Result<T>`, `ICommandHandler<,>` / `IQueryHandler<,>`, `ICrmDbContext`, Handler-Registrierung per Assembly-Scan
- [x] `Infrastructure`: `CrmDbContext` (inkl. Identity-Tabellen), `snake_case`-Naming, `TimestampInterceptor` (nutzt `TimeProvider`)
- [x] Serilog (JSON auf stdout), Health Checks `/health/live` und `/health/ready` (inkl. Postgres)
- [x] `UseForwardedHeaders` konfiguriert, Data Protection persistiert nach `/app/keys` (Pfad per Konfiguration)

## Schritt 3 – Auth
- [x] Selbstregistrierung deaktivieren (Seite und Endpoint entfernen)
- [x] Admin-Seed beim Start aus `Admin__Email` / `Admin__InitialPassword`, falls noch kein User existiert
- [x] Alle Seiten außer Login erfordern Authentifizierung (Fallback-Policy)
- [x] Rate-Limiting für Login-Formulare (5 POSTs/Minute pro IP, 429 mit `Retry-After`)

## Schritt 4 – Fachlicher Durchstich: Contact
- [x] Entität `Contact` (Felder gemäß SPEC 2.3, zunächst ohne `OrganizationId` und `Source`) + EF-Konfiguration
- [x] Use Cases `CreateContact` und `GetContacts` (Paging, einfache `ILIKE`-Suche auf Name/E-Mail)
- [x] Seite `/contacts` mit `MudDataGrid` (serverseitiges Paging) und Suchfeld
- [x] Quick-Add-Dialog (Button + Shortcut `N`) gemäß US-01
- [x] Erste Migration `InitialCreate`

## Schritt 5 – Tests
- [x] Unit-Tests für `CreateContact.Validator` und den Handler (Happy Path, fehlender Name)
- [x] Integrationstest mit Testcontainers-Postgres: Migration läuft, Kontakt wird angelegt, `CreatedAt` ist gesetzt
- [x] Architekturtest: Abhängigkeitsregel (Domain → nichts, Application → nur Domain)
- [x] bUnit-Test für den Quick-Add-Dialog (Validierungsfehler wird angezeigt)

## Schritt 6 – Container & lokale Umgebung
- [x] `deploy/docker-compose.dev.yml` mit Postgres (Volume, Port 5432) und optional Seq (Seq bewusst weggelassen: noch kein Serilog-Seq-Sink)
- [x] Multi-Stage-`Dockerfile` (restore mit Layer-Caching → publish → `aspnet:10.0`, non-root, Port 8080, `HEALTHCHECK`)
- [x] `efbundle` im Build erzeugen und ins Image kopieren; Startskript `entrypoint.sh`: Migration ausführen, dann App starten
- [x] Lokaler Test: `docker build` + Start gegen die Dev-DB

## Schritt 7 – CI/CD & Coolify
- [x] GitHub Actions: `build-test.yml` (restore, build, test inkl. Integrationstests) bei Push und PR
- [x] GitHub Actions: `docker.yml` (Image bauen und nach GHCR pushen bei Push auf `main`) – *falls Coolify-Build nicht gewählt wird*
- [x] `deploy/coolify.md`: Schritt-für-Schritt-Anleitung (Postgres-Ressource, App-Ressource, Env-Variablen, Volume `/app/keys`, Domain + HTTPS, Healthcheck-Pfad, Backup-Schedule nach S3)
- [ ] Deployment manuell durchführen (Guido) und Login + Quick-Add in Produktion verifizieren
- [ ] Redeploy testen: **Session bleibt erhalten** (Beweis für persistente Data-Protection-Keys)
- [ ] Backup auslösen und Restore in eine Test-DB einmal durchspielen

## Schritt 8 – Abschluss
- [ ] README (Kurzbeschreibung, Screenshot, Quickstart lokal, Links zu SPEC und ADRs)
- [ ] ADR-009 auf *Accepted* setzen, offene Punkte entschieden und dokumentiert
- [ ] Offene Fragen in SPEC Kap. 9 aktualisieren

## Definition of Done
- CI grün (Build + alle Tests)
- App erreichbar unter eigener Domain via HTTPS, Login funktioniert, Kontakte lassen sich anlegen und suchen
- Redeploy ohne Logout, Backup/Restore einmal verifiziert
- Keine Warnings, keine Secrets im Repo
