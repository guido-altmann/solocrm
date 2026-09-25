# ADR-007: Suche mit PostgreSQL-Volltext und Trigram

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Command Palette und Listen brauchen schnelle, tippfehlertolerante Suche über Namen, E-Mails und Titel. Datenmenge: < 100k Datensätze.

## Entscheidung
Generierte `tsvector`-Spalten (Konfiguration `simple`, da überwiegend Eigennamen) mit GIN-Index plus `pg_trgm`-GIN-Index für Ähnlichkeitssuche. Ranking kombiniert `ts_rank` und `similarity`. Die Suche wird über eine Union-Query bzw. View über alle Objekttypen ausgeführt.

## Betrachtete Optionen
### A: Postgres-Bordmittel (gewählt)
**Pro:** kein Zusatzdienst, transaktional konsistent, lehrreich. **Contra:** weniger Features als dedizierte Suchmaschinen.
### B: Meilisearch / Elasticsearch
**Pro:** sehr gute Relevanz. **Contra:** zusätzlicher Dienst, Synchronisation nötig.

## Konsequenzen
- Später prüfen: semantische Suche über pgvector ergänzend.
