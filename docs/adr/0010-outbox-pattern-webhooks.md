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

## Umsetzung und Erfahrungen (Iteration 5, 2026-10-01)
- **Payload:** `{ id, type, occurredAt, data }`. `id` ist die Id der Outbox-Nachricht und damit der Deduplizierungsschlüssel; `data` ist das gespeicherte Domain Event (nur Ids und Status). Öffentliche Event-Namen leitet `DomainEventNames` aus dem Typnamen ab (`OpportunityStageChanged` → `opportunity.stage_changed`). `WebhookEvents.All` listet die wählbaren Events; ein Test stellt sicher, dass jedes Domain Event darin vorkommt.
- **Signatur mit Zeitstempel:** HMAC-SHA256 über `<timestamp>.<body>`, Header `X-SoloCrm-Timestamp` und `X-SoloCrm-Signature: sha256=<hex>`, zusätzlich `X-SoloCrm-Event`. Empfänger prüfen Signatur und Alter (< 5 min). Die Prüfung ist mit einem unabhängig berechneten Referenzwert unit-getestet und wurde mit einem Node-Empfänger durchgespielt, der den Code aus `docs/n8n-integration.md` nutzt.
- **Versandprotokoll ohne FK auf die Outbox:** `WebhookDelivery` speichert `EventId` und `EventType` statt eines Fremdschlüssels. „Test senden“ (`webhook.ping`) geht direkt an den Empfänger, an der Outbox vorbei, und hat daher keine Outbox-Nachricht; außerdem werden Outbox und Protokoll unabhängig nach 30 Tagen aufgeräumt.
- **HTTP:** `IHttpClientFactory` mit 10 s Timeout und ohne Redirects (ein Redirect zählt als Fehler und wird nicht verfolgt). Erfolg ist nur HTTP 2xx. Im Log stehen Event-Typ, Event-Id, Status und Dauer, aber weder URL, Body noch Secret.
- **Secret-Verlust:** Ist das Data-Protection-Volume verloren, kann das Secret nicht entschlüsselt werden. Die Zustellung wird dann als fehlgeschlagen protokolliert („Secret nicht lesbar – bitte neu erzeugen“); der Prozessor läuft weiter.
- **Stolperstein:** Der Log-Platzhalter `{EventId}` kollidiert mit der gleichnamigen Eigenschaft von `Microsoft.Extensions.Logging`, die Event-Id ging im Log verloren. Der Platzhalter heißt deshalb `{WebhookEventId}`.

## Akzeptierte Restrisiken nach externem Audit (SOL-35, 2026-11-03)
- **Private Ziele erlaubt, keine IP- oder DNS-Prüfung (F-14):** Webhook-URLs dürfen auf private Adressen und `localhost` zeigen, weil n8n meist im selben internen Netz läuft (Iteration 5, Entscheidung 7). Eine SSRF-Sperre für private Netze würde genau diesen Hauptfall verhindern. Die URLs trägt nur der einzige Admin ein, der den Server ohnehin kontrolliert. Die Restfläche ist klein: SoloCRM schickt nur den signierten Event-Payload per `POST`, folgt keinen Redirects und zeigt im Versandprotokoll nur Statuscode, Dauer und Fehlerart, nie die Antwort des Ziels. Damit taugt ein Webhook nicht, um interne Dienste auszulesen.
- **Zielprüfung nur beim Speichern (F-23):** Die Regel aus `WebhookTargets` (`https`, `http` nur für `Webhooks__AllowedHttpHosts`) prüfen `CreateWebhook` und `UpdateWebhook`, nicht der Versand. Wird `Webhooks__AllowedHttpHosts` später verkleinert, gehen bestehende `http`-Ziele weiter. Akzeptiert, weil die Variable vom selben Admin stammt; wer einen Host aus der Liste nimmt, passt auch die Subscription an. Eine Prüfung beim Versand bliebe deshalb ohne Gewinn.

## Konsequenzen
- Webhook-Secrets werden per Data Protection verschlüsselt gespeichert (für die Signatur wieder benötigt, daher kein Hash).
- Das Volume `/app/keys` ist damit auch für die Integration betriebskritisch (ADR-009).
