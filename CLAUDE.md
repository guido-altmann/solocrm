# CLAUDE.md – Arbeitsanweisungen für Claude Code

## Projekt
**SoloCRM**: ein schlankes, selbst gehostetes CRM für Freelancer (Eigenbedarf, Lern- und Referenzprojekt).
- Fachliche und technische Spezifikation: `docs/SPEC.md` (**vor jeder Feature-Arbeit lesen**)
- Architekturentscheidungen: `docs/adr/` (bindend; Abweichungen nur mit neuem ADR)
- Backlog und aktueller Auftrag: Jira-Projekt **SoloCRM** (Key `SOL`, Kanban), <https://guido-altmann-team.atlassian.net/browse/SOL>. Zugriff über den Atlassian-Connector.
- `docs/iterations/`: Historie der MVP-Iterationen 1–6 (abgeschlossen, nicht mehr fortführen)

## Sprache
- Code, Bezeichner, Commit-Messages, Code-Kommentare: **Englisch**
- UI-Texte: **Deutsch**
- Dokumentation in `docs/`: Deutsch
- Kommunikation mit mir: Deutsch

## Stack
.NET 10 · Blazor Web App (Interactive Server) · MudBlazor · ASP.NET Core Minimal APIs · EF Core 10 + Npgsql · PostgreSQL · ASP.NET Identity · Hangfire (Postgres) · FluentValidation · Serilog · xUnit · AwesomeAssertions · Testcontainers · bUnit

## Befehle
```bash
# Lokale DB starten
docker compose -f deploy/docker-compose.dev.yml up -d

# Build & Test
dotnet build
dotnet test
dotnet test --filter-not-trait "Category=Integration"   # schnell, ohne Docker (xunit.v3 / Microsoft.Testing.Platform)

# Migrationen (immer aus dem Repo-Root; dotnet-ef ist als lokales Tool gepinnt: einmalig `dotnet tool restore`)
dotnet ef migrations add <Name> -p src/SoloCrm.Infrastructure -s src/SoloCrm.Web -o Persistence/Migrations
dotnet ef database update     -p src/SoloCrm.Infrastructure -s src/SoloCrm.Web

# App starten
dotnet watch --project src/SoloCrm.Web

# Container lokal bauen
docker build -t solocrm:local .
```

## Architekturregeln (nicht verhandelbar)
1. **Schichten:** `Domain` hat keine Abhängigkeiten. `Application` kennt nur `Domain`. `Infrastructure` implementiert Interfaces aus `Application`. `Web` ist Composition Root. Ein Architekturtest prüft das.
2. **Vertical Slices:** ein Use Case = eine Datei in `Application/Features/<Feature>/` mit `Command`/`Query`, `Result`, `Validator`, `Handler` als nested types einer statischen Klasse.
3. **Kein MediatR, kein AutoMapper, kein Repository-Pattern.** Handler nutzen `ICrmDbContext` direkt. Das Mapping ist explizit (Records, Projektion per `Select`).
4. **Blazor-Komponenten greifen nie direkt auf den DbContext zu**, sondern immer über Handler. Handler verwenden `IDbContextFactory` (Circuit-Lifetime!).
5. **Fachliche Fehler** werden als `Result<T>` zurückgegeben, keine Exceptions. Exceptions nur für echte Ausnahmen.
6. **Domain Events** werden in Entitäten gesammelt und über den `OutboxInterceptor` transaktional persistiert, nie direkt aus Handlern versendet.
7. **Audit und Timestamps** kommen ausschließlich aus Interceptors. Handler setzen `CreatedAt`/`UpdatedAt` nie selbst.
8. **IDs** sind UUIDv7 (`Guid.CreateVersion7()`), gesetzt im Entitätskonstruktor.
9. **Zeit:** Nie `DateTime.Now`/`UtcNow` direkt; stattdessen `TimeProvider` injizieren (testbar).
10. Neue Pakete nur über `Directory.Packages.props` (Central Package Management). Vor dem Hinzufügen eines neuen Pakets kurz nachfragen.

## Code-Konventionen
- File-scoped Namespaces, Primary Constructors wo sinnvoll, `sealed` als Default für Klassen
- `Nullable` enable, `TreatWarningsAsErrors` true, `ImplicitUsings` enable
- Async überall mit `CancellationToken` bis zur DB
- Records für Commands, Queries und DTOs
- EF-Konfiguration per `IEntityTypeConfiguration<T>` in `Infrastructure/Persistence/Configurations/`
- Tabellen- und Spaltennamen in snake_case (`EFCore.NamingConventions`)
- Enums als string in der DB speichern
- Keine auskommentierten Code-Leichen; TODOs nur mit Verweis auf ein Ticket (`// TODO(SOL-12): …`)

## Tests
- Jeder Handler bekommt mindestens einen Test für den Happy Path und die Validierungsfehler.
- Domain-Logik (z. B. Stage-Wechsel, `EstimatedValue`) wird mit Unit-Tests ohne DB geprüft.
- Interceptors, Suche, Outbox und Migrationen werden mit **Integrationstests gegen Testcontainers-Postgres** geprüft (kein InMemory-Provider, kein SQLite).
- UI-Komponenten mit Logik bekommen bUnit-Tests.
- Namensschema: `Method_Scenario_ExpectedResult`
- Integrationstests mit `[Trait("Category","Integration")]` markieren.

## Arbeitsweise
1. Vor der Umsetzung eines Tickets: das Jira-Ticket und die dort verlinkten Abschnitte in `docs/SPEC.md` lesen und einen kurzen Plan nennen. Beim Start das Ticket auf „In Arbeit“ setzen.
2. In kleinen, lauffähigen Schritten arbeiten. Nach jedem Schritt `dotnet build` und die betroffenen Tests ausführen.
3. Weicht die Umsetzung von der Spec ab oder ist die Spec unklar: **nachfragen**, nicht raten. Ist eine Spec-Änderung nötig, `docs/SPEC.md` mit anpassen.
4. Bei neuen Architekturentscheidungen: einen ADR in `docs/adr/` vorschlagen (Vorlage: `docs/adr/0000-template.md`).
5. Nach Abschluss eines Tickets: es in Jira auf „Fertig“ setzen. Umsetzungsentscheidungen, die die Spec betreffen, in `docs/SPEC.md` nachziehen, alles andere als Kommentar ins Ticket.
6. Commits im Conventional-Commits-Format mit Jira-Key: `feat(sync): add ics feed for tasks (SOL-6)`. Die alten Story-IDs (`US-01` … `US-21`) bleiben für das MVP gültig.

## Sicherheit & Datenschutz
- Secrets niemals ins Repo; nur über Umgebungsvariablen bzw. `dotnet user-secrets` lokal.
- Keine personenbezogenen Daten in Logs (keine Bodies, E-Mail-Adressen maskieren).
- API-Keys und Webhook-Secrets nur gehasht bzw. verschlüsselt speichern (Secrets per Data Protection).
