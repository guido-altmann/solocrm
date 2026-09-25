# ADR-010: Transactional Outbox für Webhooks

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Externe Systeme (n8n) sollen zuverlässig über Ereignisse informiert werden. Ein direkter HTTP-Call im Handler ist unzuverlässig (Fehler nach Commit → Event verloren; Fehler vor Commit → Phantom-Event).

## Entscheidung
Domain Events werden per `OutboxInterceptor` in derselben Transaktion wie die fachliche Änderung als `OutboxMessage` gespeichert. Ein Hintergrundprozess liest unverarbeitete Nachrichten (`FOR UPDATE SKIP LOCKED`), versendet sie an aktive `WebhookSubscription`s (HMAC-SHA256-signiert) und protokolliert jeden Versuch. Retry mit exponentiellem Backoff, max. 6 Versuche. Zustellung: *at-least-once*; Empfänger deduplizieren über die Event-`id`.

## Betrachtete Optionen
### A: Transactional Outbox (gewählt)
**Pro:** konsistent, robust, klassisches Integrationsmuster. **Contra:** zusätzliche Tabelle und Hintergrundprozess.
### B: Direkter Versand im Handler
**Pro:** trivial. **Contra:** Datenverlust oder Inkonsistenz.

## Konsequenzen
- Webhook-Secrets werden per Data Protection verschlüsselt gespeichert (für die Signatur wieder benötigt, daher kein Hash).
