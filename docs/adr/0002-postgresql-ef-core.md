# ADR-002: PostgreSQL mit EF Core (Npgsql)

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Die CRM-Domäne ist stark relational (Kontakt ↔ Organisation ↔ Anfrage ↔ Aktivität). Gleichzeitig werden flexible Zusatzfelder, Volltextsuche und perspektivisch Vektorsuche benötigt.

## Entscheidung
PostgreSQL mit EF Core 10 und Npgsql. Zusatzfelder als `jsonb`, Suche mit `tsvector` + `pg_trgm`, pgvector optional später.

## Betrachtete Optionen
### A: PostgreSQL (gewählt)
**Pro:** referentielle Integrität, JSONB, Volltext, Trigram, pgvector, ausgereiftes EF-Tooling, einfache Backups in Coolify. **Contra:** Schemamigrationen nötig.
### B: MongoDB
**Pro:** flexibles Schema. **Contra:** Beziehungen und Joins umständlich, keine referentielle Integrität, schwächeres EF-Core-Tooling.
### C: SQLite
**Pro:** trivialer Betrieb. **Contra:** Volltext, Trigram und Concurrency eingeschränkt; weniger Lerneffekt für produktionsnahe Setups.

## Konsequenzen
- Leichter: Abfragen über Beziehungen, Transaktionen (Audit/Outbox in einer Transaktion).
- Schwerer: Custom Properties brauchen JSONB-Handling.
- Später prüfen: pgvector für KI-Features.
