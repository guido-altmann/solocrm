# SoloCRM

Schlankes, selbst gehostetes CRM für Freelancer und Einzelunternehmer: Kontakte, Organisationen (Endkunden & Vermittler), Projektanfragen-Pipeline, Timeline und Follow-ups.

**Stack:** .NET 10 · Blazor (Interactive Server) · MudBlazor · PostgreSQL · EF Core · Docker/Coolify

> 🚧 In Entwicklung – siehe [Spezifikation](docs/SPEC.md) und [Iterationsplan](docs/SPEC.md#8-iterationsplan).

## Dokumentation
- [Spezifikation](docs/SPEC.md)
- [Architekturentscheidungen (ADRs)](docs/adr/README.md)
- [Aktuelle Iteration](docs/iterations/01-walking-skeleton.md)
- [Hinweise für Claude Code](CLAUDE.md)

## Quickstart (lokal)
```bash
docker compose -f deploy/docker-compose.dev.yml up -d
dotnet user-secrets set "Admin:Email" "you@example.com" --project src/SoloCrm.Web
dotnet user-secrets set "Admin:InitialPassword" "<secure>" --project src/SoloCrm.Web
dotnet watch --project src/SoloCrm.Web
```
