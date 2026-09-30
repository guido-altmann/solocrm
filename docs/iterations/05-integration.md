# Iteration 5 – Integration

**Ziel:** SoloCRM ist von außen automatisierbar. Domain Events erreichen n8n zuverlässig als signierte Webhooks, eine REST-API mit API-Key erlaubt Lesen und Schreiben (z. B. Lead-Eingang aus n8n), und der Kontaktbestand aus HubSpot lässt sich per CSV übernehmen.

**Stories:** US-16 – US-18
**Referenzen:** `docs/SPEC.md` Kap. 2.4 (WebhookSubscription, WebhookDelivery, ApiKey), 2.6 (Domain Events), 3.3 S6 (Einstellungen), 4 (US-16 – US-18), 5 (REST-API, Webhook-Payload), 6 (Sicherheit, Datenschutz, Performance), 7.4 (Outbox-Verarbeitung); ADR-008, ADR-009, ADR-010

**Aus früheren Iterationen übernommen:**
- Die Outbox wird seit Iteration 2 transaktional befüllt (`OutboxInterceptor`), aber noch nicht verarbeitet. In Produktion liegen daher bereits unverarbeitete Nachrichten (siehe Frage 3).
- ADR-008 (Hangfire vs. eigener `BackgroundService`) ist noch *Proposed* und wird hier entschieden (Frage 1).
- Events enthalten bewusst nur Ids und Status, keine personenbezogenen Inhalte (z. B. `ActivityLogged` ohne Betreff und Text).
- Rate-Limiting existiert bisher nur für den Login (`LoginRateLimiting`).

**Bewusst nicht in dieser Iteration:**
- Neue Event-Typen wie `contact.updated` oder `enrichment.requested` (SPEC 9.5: nur so bauen, dass sie trivial ergänzbar sind)
- Import von Organisationen, Anfragen oder Activities per CSV (US-16 betrifft Kontakte)
- OAuth, mehrere Benutzer oder Scopes je API-Key (Single-User, SPEC 1.4)
- Export nach CSV (DSGVO-Export folgt mit US-19 in It. 6)

---

## Schritt 1 – Outbox-Verarbeitung (ADR-008, ADR-010)
- [ ] Hintergrundprozess gemäß Frage 1: fällige Nachrichten (`ProcessedAt IS NULL AND NextAttemptAt <= now`) in Blöcken per `SELECT … FOR UPDATE SKIP LOCKED` holen (auch bei zwei parallel laufenden Containern während eines Rolling Updates korrekt, ADR-009)
- [ ] Je Nachricht: an alle aktiven, passenden Subscriptions zustellen; Nachricht gilt als verarbeitet, wenn jede Zustellung erfolgreich war oder endgültig aufgegeben wurde
- [ ] Retry mit exponentiellem Backoff, max. 6 Versuche (US-18 AK3); Zeitplan festlegen (z. B. 1 min, 5 min, 30 min, 2 h, 12 h)
- [ ] `TimeProvider` für alle Zeitpunkte; Intervall konfigurierbar
- [ ] Umgang mit Alt-Nachrichten und Nachrichten ohne passende Subscription (Frage 3)
- [ ] ADR-008 auf *Accepted* setzen (mit Begründung)

## Schritt 2 – Webhooks: Domäne und Verwaltung (US-18 AK1)
- [ ] Entitäten `WebhookSubscription` (Name, Url, Events, Secret, IsActive) und `WebhookDelivery` (SubscriptionId, OutboxMessageId, Versuch, StatusCode, DurationMs, Error, AttemptedAt) (SPEC 2.4)
- [ ] Secret wird generiert (kryptografisch zufällig), per Data Protection verschlüsselt gespeichert und einmalig im Klartext angezeigt; „Neu erzeugen“ möglich (ADR-010)
- [ ] URL-Validierung: nur `https` (Ausnahme `http` in Development, Frage 7)
- [ ] Event-Auswahl aus den Typen in SPEC 2.6 (öffentliche Namen wie `opportunity.stage_changed`)
- [ ] Use Cases `CreateWebhook`, `UpdateWebhook`, `RegenerateWebhookSecret`, `DeleteWebhook`, `GetWebhooks`, `GetWebhookDeliveries`
- [ ] Abschnitt „Webhooks“ in `/settings` mit Versandprotokoll je Subscription (US-18 AK3)
- [ ] Migration (additiv)

## Schritt 3 – Webhook-Versand (US-18 AK2/AK3)
- [ ] `HttpClient` über `IHttpClientFactory` mit Timeout (z. B. 10 s), ohne automatische Redirects
- [ ] Payload gemäß SPEC 5 (`id`, `type`, `occurredAt`, `data`) mit Inhalt gemäß Frage 2
- [ ] Signatur `X-SoloCrm-Signature: sha256=<hex>` über den Body (HMAC-SHA256 mit dem Secret); Zeitstempel gegen Replay (Frage 6)
- [ ] Erfolg = HTTP 2xx; jeder Versuch landet im Protokoll; Logs ohne Bodies und Secrets (SPEC 6)
- [ ] „Test senden“ (Event `webhook.ping`) aus den Einstellungen
- [ ] Aufbewahrung des Protokolls und verarbeiteter Outbox-Nachrichten (Frage 8)

## Schritt 4 – API-Keys (US-17 AK1)
- [ ] Entität `ApiKey` (Name, Prefix, KeyHash SHA-256, CreatedAt, LastUsedAt, RevokedAt) (SPEC 2.4)
- [ ] Key-Format z. B. `scrm_<prefix>_<secret>`; Klartext nur einmal bei der Erzeugung, gespeichert wird der Hash
- [ ] Use Cases `CreateApiKey`, `RevokeApiKey`, `GetApiKeys`
- [ ] Abschnitt „API-Keys“ in `/settings` (Liste mit Prefix, zuletzt genutzt, Widerrufen)
- [ ] Migration (additiv)

## Schritt 5 – REST-API (US-17, SPEC 5)
- [ ] Authentication-Handler für `X-Api-Key` (eigenes Schema, getrennt vom Cookie-Login); Vergleich in konstanter Zeit, `LastUsedAt` gedrosselt aktualisieren
- [ ] Rate-Limit 60 Requests/Minute je Key (US-17 AK2), 429 mit `Retry-After`
- [ ] Minimal-API-Endpoints unter `/api/v1` gemäß SPEC 5 in `Web/Endpoints/`; rufen dieselben Handler wie die UI
- [ ] Fehler als RFC 9457 Problem Details; Validierungsfehler mit Feldnamen (camelCase)
- [ ] `PATCH` als Teil-Update (Frage 4); `PATCH /opportunities/{id}` mit `stageId` löst den Stage-Wechsel aus
- [ ] Listen mit `search`, `tag`, `page`, `pageSize` (dieselbe Suche wie die UI)
- [ ] OpenAPI unter `/openapi/v1.json`, UI via Scalar (US-17 AK3), Zugriff gemäß Frage 5
- [ ] Architekturtest: Endpoints greifen nicht auf den DbContext zu

## Schritt 6 – CSV-Import: Use Case (US-16)
- [ ] Parsing mit CsvHelper: Trennzeichen (`,`/`;`) und Kodierung (UTF-8 mit/ohne BOM, Windows-1252) erkennen, Größenlimit (z. B. 5 MB / 10.000 Zeilen)
- [ ] Vorschau der ersten 10 Zeilen (AK1)
- [ ] Spalten-Mapping auf Kontaktfelder inkl. Organisation (Name → bestehende Organisation oder neu anlegen)
- [ ] Dubletten per E-Mail (case-insensitive), Option überspringen/aktualisieren (AK2); Dubletten innerhalb der Datei
- [ ] Ergebnisbericht: angelegt, aktualisiert, übersprungen, fehlerhaft mit Zeile und Grund (AK3)
- [ ] Mapping-Vorlage für den HubSpot-Kontaktexport (AK4, Frage 9)
- [ ] Ausführung synchron oder als Hintergrundjob (Frage 10); Audit und Events wie bei manueller Anlage (`ContactCreated` je Kontakt → ggf. viele Webhooks, Frage 11)

## Schritt 7 – CSV-Import: Oberfläche (US-16)
- [ ] Seite bzw. Abschnitt „Import“ in `/settings`: Upload → Vorschau → Mapping (Vorlage wählbar) → Import → Bericht
- [ ] Fehlerhafte Zeilen als CSV herunterladbar (optional, Frage 12)

## Schritt 8 – Tests
- [ ] Unit-Tests: Signatur, Backoff-Zeitplan, Key-Erzeugung und -Prüfung, CSV-Mapping
- [ ] Handler-Tests (Happy Path + Validierungsfehler) für alle neuen Use Cases
- [ ] Integrationstests: Outbox mit `SKIP LOCKED` (zwei parallele Verarbeiter, keine Doppelzustellung), Retry bis Aufgabe, Versand gegen einen lokalen Test-Empfänger inkl. Signaturprüfung
- [ ] API-Tests mit `WebApplicationFactory`: ohne/mit falschem/widerrufenem Key, Rate-Limit, Problem Details, CRUD und Stage-Wechsel per `PATCH`, OpenAPI-Dokument abrufbar
- [ ] Import: Vorschau, Mapping, Dubletten (überspringen/aktualisieren), Bericht mit Zeilennummern, HubSpot-Vorlage
- [ ] bUnit: Secret/Key einmalig anzeigen, Import-Assistent
- [ ] Ende-zu-Ende mit n8n (manuell): Webhook empfangen und Signatur prüfen, Kontakt per API anlegen

## Schritt 9 – Abschluss
- [ ] Migrationen erzeugt und per `efbundle` in Produktion ausgerollt (abwärtskompatibel, siehe ADR-009)
- [ ] ADR-008 und ADR-010 mit den Erfahrungen ergänzen; ggf. neuer ADR für API-Authentifizierung
- [ ] `deploy/coolify.md`: neue Konfiguration, Hinweis auf das Volume `/app/keys` (Webhook-Secrets)
- [ ] README-Stand, Beispiel-Workflow für n8n (Signaturprüfung) und Screenshot
- [ ] SPEC nachziehen (Entscheidungen, Payload, API-Details)

## Offene Fragen (vor dem Start zu klären)
1. **Hintergrundprozess:** Hangfire (ADR-008 Option A; neue Pakete `Hangfire.AspNetCore` und `Hangfire.PostgreSql`, Dashboard unter `/jobs`) oder ein eigener `BackgroundService` mit `PeriodicTimer` (Option B)? Retries und Backoff steckt ohnehin in der Outbox (`Attempts`, `NextAttemptAt`), Hangfire bräuchte man eher für den Import und spätere Jobs. *Vorschlag: eigener `BackgroundService` für die Outbox (lehrreich, keine Zusatztabellen); Hangfire erst, wenn weitere Jobarten hinzukommen. Der Import läuft synchron (Frage 10).*
2. **Payload-Inhalt:** Nur Ids wie in den Events (n8n holt Details per API nach) oder angereicherte Daten (z. B. Name und E-Mail des Kontakts)? *Vorschlag: nur Ids und Status wie in SPEC 5 („schlanke Events“). Das hält personenbezogene Daten aus der Outbox und aus den Protokollen fern; n8n ruft bei Bedarf `GET /api/v1/contacts/{id}` auf.*
3. **Alt-Nachrichten und Nachrichten ohne Subscription:** Seit Iteration 2 liegen unverarbeitete Nachrichten in der Outbox. Sollen sie bei der ersten Verarbeitung an neue Subscriptions gehen? *Vorschlag: nein. Eine Subscription erhält nur Events, die nach ihrer Anlage aufgetreten sind; Nachrichten ohne passende Subscription werden sofort als verarbeitet markiert.*
4. **PATCH-Semantik:** JSON Merge Patch (RFC 7396: fehlendes Feld = unverändert, `null` = leeren) oder JSON Patch (RFC 6902)? *Vorschlag: Merge Patch; umgesetzt, indem der Endpoint den aktuellen Stand lädt, die gesendeten Felder überschreibt und den bestehenden `Update…`-Handler aufruft (wie beim Inline-Editing).*
5. **OpenAPI/Scalar-Zugriff:** Öffentlich, nur für angemeldete Nutzer (Cookie) oder nur in Development? *Vorschlag: nur für den angemeldeten Nutzer, auch in Produktion. Neue Pakete: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`.*
6. **Replay-Schutz der Signatur:** Nur HMAC über den Body (wie in US-18 AK2) oder zusätzlich ein Zeitstempel-Header `X-SoloCrm-Timestamp`, der mitsigniert wird (`<timestamp>.<body>`, ähnlich Stripe)? *Vorschlag: mit Zeitstempel; die n8n-Vorlage prüft Signatur und Alter (z. B. < 5 min).*
7. **Webhook-Ziele:** Nur `https`, oder auch `http` für n8n im selben internen Netz (z. B. `http://n8n:5678`)? Sollen private IP-Bereiche erlaubt sein (SSRF-Schutz)? *Vorschlag: `https` und zusätzlich `http` nur für explizit konfigurierte Hosts (`Webhooks__AllowedHttpHosts`); private Adressen erlaubt, da Single-User und n8n oft intern läuft.*
8. **Aufbewahrung:** Wie lange bleiben verarbeitete Outbox-Nachrichten und das Versandprotokoll erhalten? *Vorschlag: 30 Tage, täglich aufgeräumt durch denselben Hintergrundprozess.*
9. **HubSpot-Vorlage:** Die Frage aus SPEC 10 („Welche HubSpot-Felder werden tatsächlich benötigt?“) ist noch offen. **Bitte eine Kopfzeile eines echten HubSpot-Kontaktexports bereitstellen** (nur die Spaltennamen, keine Daten); daraus entsteht die Vorlage. *Vorschlag: First Name, Last Name, Email, Phone Number, Job Title, Company Name, LinkedIn-URL, Original Source → Quelle.*
10. **Import synchron oder als Job:** Bei bis zu einigen tausend Zeilen dauert ein Import Sekunden. *Vorschlag: synchron im Blazor-Circuit mit Fortschrittsanzeige und einer Transaktion je Block (z. B. 100 Zeilen); kein Hintergrundjob.*
11. **Events beim Import:** Soll jeder importierte Kontakt ein `ContactCreated` auslösen (→ ggf. tausende Webhooks)? *Vorschlag: ja, konsistent mit der manuellen Anlage; die Outbox verarbeitet sie gestaffelt. Alternative wäre ein zusammenfassendes `contacts.imported`.*
12. **Fehlerhafte Zeilen herunterladen:** Nötig oder reicht die Anzeige im Bericht? *Vorschlag: Anzeige genügt; Download nur, wenn es beim echten HubSpot-Import stört.*
13. **Neue Pakete:** Je nach Antworten `CsvHelper` (SPEC 7.1), `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` und ggf. `Hangfire.AspNetCore`/`Hangfire.PostgreSql`. Einverstanden?

## Definition of Done
- CI grün (Build + alle Tests); keine Warnings
- Ein Stage-Wechsel erreicht einen n8n-Webhook signiert und wird dort erfolgreich geprüft; Fehler werden mit Backoff wiederholt und protokolliert
- Kontakte lassen sich per REST-API mit API-Key anlegen, lesen und ändern; ohne gültigen Key gibt es 401, über 60 Requests/Minute 429
- OpenAPI-Dokument und Scalar-UI sind erreichbar
- Ein echter HubSpot-Export lässt sich mit Vorschau, Mapping und Dublettenprüfung importieren; der Bericht nennt Zeile und Grund jedes Fehlers
- Keine Secrets oder personenbezogenen Daten in Logs; API-Keys nur gehasht, Webhook-Secrets verschlüsselt gespeichert
- In Produktion deployt, Migration ohne Datenverlust
