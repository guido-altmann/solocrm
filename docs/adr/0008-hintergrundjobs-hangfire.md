# ADR-008: Hintergrundjobs mit Hangfire (PostgreSQL-Storage)

**Status:** Proposed · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Benötigt werden: Outbox-Verarbeitung, Webhook-Retries, CSV-Import und später Reminder und E-Mail-Sync.

## Entscheidung
Hangfire mit `Hangfire.PostgreSql`, Dashboard nur für den angemeldeten Nutzer unter `/jobs`.

## Betrachtete Optionen
### A: Hangfire (gewählt)
**Pro:** persistente Jobs, Retries, Dashboard, gleiche DB. **Contra:** zusätzliche Tabellen, etwas Magie.
### B: Eigener `BackgroundService` + `PeriodicTimer`
**Pro:** maximal transparent und lehrreich. **Contra:** Retry-, Scheduling- und Monitoring-Logik selbst bauen.
### C: Quartz.NET
**Pro:** mächtiges Scheduling. **Contra:** komplexere API, kein vergleichbares Dashboard.

## Konsequenzen
- Die Outbox-Verarbeitung kann aus Lerngründen zunächst als eigener `BackgroundService` gebaut werden (Option B) – Entscheidung in Iteration 5 finalisieren.
