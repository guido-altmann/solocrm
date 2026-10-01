# ADR-008: Hintergrundjobs – eigener BackgroundService statt Hangfire

**Status:** Accepted · **Datum:** 2026-09-25 (entschieden 2026-09-30) · **Entscheider:** Guido Altmann

## Kontext
Benötigt werden: Outbox-Verarbeitung, Webhook-Retries, CSV-Import und später Reminder und E-Mail-Sync.

## Entscheidung
~~Hangfire mit `Hangfire.PostgreSql`, Dashboard nur für den angemeldeten Nutzer unter `/jobs`.~~

**Revidiert 2026-09-30 (Iteration 5):** Option B – ein eigener `BackgroundService` mit `PeriodicTimer` verarbeitet die Outbox. Retry und Backoff stecken bereits in der Outbox (`Attempts`, `NextAttemptAt`), `FOR UPDATE SKIP LOCKED` verhindert Doppelverarbeitung auch bei parallel laufenden Containern (ADR-009). Der CSV-Import läuft synchron im Circuit, weitere Jobarten gibt es noch nicht. Hangfire wird erst eingeführt, wenn solche hinzukommen (z. B. Reminder, E-Mail-Sync).

## Umsetzung und Erfahrungen (Iteration 5, 2026-10-01)
- **Claim mit Lease statt offener Transaktion:** Ein einziges Statement `UPDATE outbox_messages SET next_attempt_at = now + Lease WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED LIMIT n) RETURNING id` beansprucht einen Block. Die Zeilensperre gilt nur für dieses Statement; während der HTTP-Aufrufe bleibt keine Transaktion offen. Ein zweiter Container überspringt gesperrte Zeilen und sieht beanspruchte Nachrichten bis zum Ablauf der Lease (Default 5 min) nicht. Stürzt ein Prozess ab, wird die Nachricht nach Ablauf der Lease erneut verarbeitet (at-least-once, wie in ADR-010 vorgesehen). Verworfen: Sperre über die gesamte Zustellung halten (lange Transaktionen, bis zu 10 s je Zustellung).
- **Versuche je Subscription:** `OutboxMessage.Attempts` zählt die Zustellrunden; welche Subscription bereits erfolgreich war bzw. wie oft sie es versucht hat, ergibt sich aus `webhook_deliveries` (Event-Id). Eine Wiederholung geht nur an die fehlgeschlagenen Ziele. Backoff nach Fehlversuch 1–5: 1 min, 5 min, 30 min, 2 h, 12 h; nach dem 6. Fehlversuch wird aufgegeben (`ProcessedAt` gesetzt, `LastError` bleibt).
- **Konfiguration** (Abschnitt `Outbox`): `PollingInterval` (10 s), `BatchSize` (20), `LeaseDuration` (5 min), `Retention` (30 Tage), `CleanupInterval` (1 Tag), `Enabled`. Jeder Tick arbeitet alle fälligen Blöcke ab; das Aufräumen läuft beim Start und danach täglich.
- **Tests:** Der Prozessor ist eine eigene Klasse (`OutboxProcessor`), die Tests direkt aufrufen; der `BackgroundService` ist nur die Zeitschleife. In `WebApplicationFactory`-Tests ist er abgeschaltet (`Outbox:Enabled=false`). Der Integrationstest mit zwei parallelen Prozessoren und 40 Nachrichten bestätigt: jede Nachricht genau einmal zugestellt.

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
