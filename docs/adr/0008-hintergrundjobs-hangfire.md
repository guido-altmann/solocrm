# ADR-008: Hintergrundjobs mit Hangfire (PostgreSQL-Storage)

**Status:** Accepted · **Datum:** 2026-09-25 (entschieden 2026-09-30) · **Entscheider:** Guido Altmann

## Kontext
Benötigt werden: Outbox-Verarbeitung, Webhook-Retries, CSV-Import und später Reminder und E-Mail-Sync.

## Entscheidung
~~Hangfire mit `Hangfire.PostgreSql`, Dashboard nur für den angemeldeten Nutzer unter `/jobs`.~~

**Revidiert 2026-09-30 (Iteration 5):** Option B – ein eigener `BackgroundService` mit `PeriodicTimer` verarbeitet die Outbox. Retry und Backoff stecken bereits in der Outbox (`Attempts`, `NextAttemptAt`), `FOR UPDATE SKIP LOCKED` verhindert Doppelverarbeitung auch bei parallel laufenden Containern (ADR-009). Der CSV-Import läuft synchron im Circuit, weitere Jobarten gibt es noch nicht. Hangfire wird erst eingeführt, wenn solche hinzukommen (z. B. Reminder, E-Mail-Sync).

## Betrachtete Optionen
### A: Hangfire
**Pro:** persistente Jobs, Retries, Dashboard, gleiche DB. **Contra:** zusätzliche Tabellen, etwas Magie.
### B: Eigener `BackgroundService` + `PeriodicTimer` (gewählt)
**Pro:** maximal transparent und lehrreich. **Contra:** Retry-, Scheduling- und Monitoring-Logik selbst bauen.
### C: Quartz.NET
**Pro:** mächtiges Scheduling. **Contra:** komplexere API, kein vergleichbares Dashboard.

## Konsequenzen
- Keine zusätzlichen Tabellen und kein Dashboard; Zustand und Fehler der Outbox sind über das Versandprotokoll in den Einstellungen sichtbar.
- Scheduling, Aufräumen (30 Tage) und Logging müssen selbst gebaut und getestet werden.
