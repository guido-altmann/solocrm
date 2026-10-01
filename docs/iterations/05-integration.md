# Iteration 5 – Integration

**Ziel:** SoloCRM ist von außen automatisierbar. Domain Events erreichen n8n zuverlässig als signierte Webhooks, eine REST-API mit API-Key erlaubt Lesen und Schreiben (z. B. Lead-Eingang aus n8n), und der Kontaktbestand aus HubSpot lässt sich per CSV übernehmen.

**Stories:** US-16 – US-18, Erweiterung: Adressen und Organisationsimport (Entscheidungen 14–17)
**Referenzen:** `docs/SPEC.md` Kap. 2.4 (WebhookSubscription, WebhookDelivery, ApiKey), 2.6 (Domain Events), 3.3 S6 (Einstellungen), 4 (US-16 – US-18), 5 (REST-API, Webhook-Payload), 6 (Sicherheit, Datenschutz, Performance), 7.4 (Outbox-Verarbeitung); ADR-008, ADR-009, ADR-010

**Aus früheren Iterationen übernommen:**
- Die Outbox wird seit Iteration 2 transaktional befüllt (`OutboxInterceptor`), aber noch nicht verarbeitet. In Produktion liegen daher bereits unverarbeitete Nachrichten (siehe Entscheidung 3).
- ADR-008 ist entschieden: eigener `BackgroundService` (Entscheidung 1).
- Events enthalten bewusst nur Ids und Status, keine personenbezogenen Inhalte (z. B. `ActivityLogged` ohne Betreff und Text).
- Rate-Limiting existiert bisher nur für den Login (`LoginRateLimiting`).

**Bewusst nicht in dieser Iteration:**
- Neue Event-Typen wie `contact.updated` oder `enrichment.requested` (SPEC 9.5: nur so bauen, dass sie trivial ergänzbar sind)
- Import von Organisationen, Anfragen oder Activities per CSV (US-16 betrifft Kontakte)
- OAuth, mehrere Benutzer oder Scopes je API-Key (Single-User, SPEC 1.4)
- Export nach CSV (DSGVO-Export folgt mit US-19 in It. 6)

---

## Schritt 1 – Outbox-Verarbeitung (ADR-008, ADR-010)
- [x] `BackgroundService` mit `PeriodicTimer` (Entscheidung 1): fällige Nachrichten (`ProcessedAt IS NULL AND NextAttemptAt <= now`) in Blöcken per `SELECT … FOR UPDATE SKIP LOCKED` mit Lease beanspruchen (auch bei zwei parallel laufenden Containern während eines Rolling Updates korrekt, ADR-009)
- [x] Je Nachricht: an alle aktiven, passenden Subscriptions zustellen; Nachricht gilt als verarbeitet, wenn jede Zustellung erfolgreich war oder endgültig aufgegeben wurde
- [x] Retry mit exponentiellem Backoff, max. 6 Versuche (US-18 AK3); Zeitplan festlegen (1 min, 5 min, 30 min, 2 h, 12 h)
- [x] `TimeProvider` für alle Zeitpunkte; Intervall konfigurierbar
- [x] Alt-Nachrichten und Nachrichten ohne passende Subscription als verarbeitet markieren (Entscheidung 3)
- [x] ADR-008 mit den Erfahrungen ergänzen

## Schritt 2 – Webhooks: Domäne und Verwaltung (US-18 AK1)
- [x] Entitäten `WebhookSubscription` (Name, Url, Events, Secret, IsActive) und `WebhookDelivery` (SubscriptionId, EventId, EventType, Versuch, StatusCode, DurationMs, Error, AttemptedAt, Succeeded) (SPEC 2.4); `EventId` statt FK auf die Outbox, weil der Test-Ping keine Outbox-Nachricht hat
- [x] Secret wird generiert (kryptografisch zufällig), per Data Protection verschlüsselt gespeichert und einmalig im Klartext angezeigt; „Neu erzeugen“ möglich (ADR-010)
- [x] URL-Validierung: nur `https` (`http` nur für `Webhooks__AllowedHttpHosts`, Entscheidung 7)
- [x] Event-Auswahl aus den Typen in SPEC 2.6 (öffentliche Namen wie `opportunity.stage_changed`)
- [x] Use Cases `CreateWebhook`, `UpdateWebhook`, `RegenerateWebhookSecret`, `DeleteWebhook`, `GetWebhooks`, `GetWebhookDeliveries`
- [x] Abschnitt „Webhooks“ in `/settings` mit Versandprotokoll je Subscription (US-18 AK3)
- [x] Migration (additiv)

## Schritt 3 – Webhook-Versand (US-18 AK2/AK3)
- [x] `HttpClient` über `IHttpClientFactory` mit Timeout (z. B. 10 s), ohne automatische Redirects
- [x] Payload gemäß SPEC 5 (`id`, `type`, `occurredAt`, `data`) nur mit Ids und Status (Entscheidung 2)
- [x] Signatur `X-SoloCrm-Signature: sha256=<hex>` über den Body (HMAC-SHA256 mit dem Secret); mitsignierter Zeitstempel `X-SoloCrm-Timestamp` gegen Replay (Entscheidung 6)
- [x] Erfolg = HTTP 2xx; jeder Versuch landet im Protokoll; Logs ohne Bodies und Secrets (SPEC 6)
- [x] „Test senden“ (Event `webhook.ping`) aus den Einstellungen
- [x] Aufräumen nach 30 Tagen: Protokoll und verarbeitete Outbox-Nachrichten (Entscheidung 8)

## Schritt 4 – API-Keys (US-17 AK1)
- [x] Entität `ApiKey` (Name, Prefix, KeyHash SHA-256, CreatedAt, LastUsedAt, RevokedAt) (SPEC 2.4)
- [x] Key-Format `scrm_<prefix>_<secret>` (8 Zeichen Prefix, 32 Byte hex); Klartext nur einmal bei der Erzeugung, gespeichert wird der Hash
- [x] Use Cases `CreateApiKey`, `RevokeApiKey`, `GetApiKeys`
- [x] Abschnitt „API-Keys“ in `/settings` (Liste mit Prefix, zuletzt genutzt, Widerrufen)
- [x] Migration (additiv)

## Schritt 5 – REST-API (US-17, SPEC 5)
- [x] Authentication-Handler für `X-Api-Key` (eigenes Schema, getrennt vom Cookie-Login); Vergleich in konstanter Zeit, `LastUsedAt` gedrosselt aktualisieren
- [x] Rate-Limit 60 Requests/Minute je Key (US-17 AK2), 429 mit `Retry-After`; partitioniert nach Key-Prefix und vor der Authentifizierung, damit Anfragen über dem Limit keinen DB-Zugriff kosten
- [x] Minimal-API-Endpoints unter `/api/v1` gemäß SPEC 5 in `Web/Endpoints/`; rufen dieselben Handler wie die UI
- [x] Fehler als RFC 9457 Problem Details; Validierungsfehler mit Feldnamen (camelCase)
- [x] `PATCH` als JSON Merge Patch (Entscheidung 4); `PATCH /opportunities/{id}` mit `stageId` löst den Stage-Wechsel aus
- [x] Listen mit `search`, `tag` (Name oder Id, mehrfach = ODER), `page` (ab 1), `pageSize` (dieselbe Suche wie die UI); neu `GetOpportunities`, `GetTask`, `FindOrganizationByName` (`organizationName` beim Anlegen eines Kontakts nutzt eine bestehende Organisation)
- [x] OpenAPI unter `/openapi/v1.json`, UI via Scalar (US-17 AK3), nur für den angemeldeten Nutzer (Entscheidung 5)
- [x] Architekturtest: Endpoints greifen nicht auf den DbContext zu

## Schritt 6 – CSV-Import: Use Case (US-16)
- [x] Parsing mit CsvHelper: Trennzeichen (`,`/`;`) und Kodierung (UTF-8 mit/ohne BOM, Windows-1252) erkennen, Größenlimit 5 MB / 10.000 Zeilen; Zeilennummern wie in der Tabellenkalkulation (Kopfzeile = 1)
- [x] Vorschau der ersten 10 Zeilen (AK1)
- [x] Spalten-Mapping je Zielfeld (Zielfeld → Quellspalte, mit Ersatzspalte) inkl. Organisation (Name → bestehende Organisation oder neu anlegen); Spalten per Index, da Kopfzeilen nicht eindeutig sein müssen
- [x] Dubletten per E-Mail (case-insensitive), ohne E-Mail per `HubSpotRecordId`; Option überspringen/aktualisieren (AK2); Dubletten innerhalb der Datei (spätere Zeile wird übersprungen); „aktualisieren“ überschreibt nur mit nicht leeren Werten
- [x] Ergebnisbericht: angelegt, aktualisiert, übersprungen, fehlerhaft mit Zeile und Grund (AK3)
- [x] Mapping-Vorlage für den HubSpot-Kontaktexport (AK4, Entscheidung 9) inkl. Werte-Mapping der Quelle und Zielfeld „Tag“ für `Lifecycle Stage`
- [x] Synchrone Ausführung in Blöcken zu 100 Zeilen mit Fortschritt (Entscheidung 10); Audit und `ContactCreated` je Kontakt wie bei manueller Anlage (Entscheidung 11)

## Schritt 7 – CSV-Import: Oberfläche (US-16)
- [x] Seite bzw. Abschnitt „Import“ in `/settings`: Upload → Vorschau → Mapping (Vorlage wählbar) → Import → Bericht

## Schritt 8 – Tests
- [x] Unit-Tests: Signatur, Backoff-Zeitplan, Key-Erzeugung und -Prüfung, CSV-Mapping
- [x] Handler-Tests (Happy Path + Validierungsfehler) für alle neuen Use Cases
- [x] Integrationstests: Outbox mit `SKIP LOCKED` (zwei parallele Verarbeiter, keine Doppelzustellung), Retry bis Aufgabe, Versand gegen einen lokalen Test-Empfänger inkl. Signaturprüfung
- [x] API-Tests mit `WebApplicationFactory`: ohne/mit falschem/widerrufenem Key, Rate-Limit, Problem Details, CRUD und Stage-Wechsel per `PATCH`, OpenAPI-Dokument abrufbar
- [x] Import: Vorschau, Mapping, Dubletten (überspringen/aktualisieren), Bericht mit Zeilennummern, HubSpot-Vorlage
- [x] bUnit: Secret/Key einmalig anzeigen, Import-Assistent
- [x] Ende-zu-Ende mit n8n (manuell): Webhook empfangen und Signatur prüfen, Kontakt per API anlegen
- [x] Browser-Smoke-Test (Playwright, Wegwerf-DB) mit lokalem Empfänger, der die Signatur wie im n8n-Beispiel prüft: API-Key und Secret nur einmal sichtbar, Ping, `contact.created` und `opportunity.stage_changed` signiert zugestellt, Versandprotokoll, HubSpot-Import mit Bericht; Logs ohne Keys, Secrets und E-Mails; Scalar ohne externe Anfragen
- [x] Zeitbudget-Tests laufen in einer eigenen, nicht parallelen Collection (sonst misst der Test die Last der übrigen Testcontainer)

## Schritt 9 – Abschluss
- [x] Migrationen erzeugt (`AddWebhooks`, `AddApiKeys`, `AddAddresses`, alle additiv) und per `efbundle` in Produktion ausgerollt (abwärtskompatibel, siehe ADR-009)
- [x] ADR-008 und ADR-010 mit den Erfahrungen ergänzen; API-Authentifizierung als Umsetzungsabschnitt in ADR-004 statt eines neuen ADR (die Entscheidung selbst stand dort schon)
- [x] `deploy/coolify.md`: neue Konfiguration, Hinweis auf das Volume `/app/keys` (Webhook-Secrets)
- [x] README-Stand, Beispiel-Workflow für n8n (Signaturprüfung, `docs/n8n-integration.md`) und Screenshot
- [x] SPEC nachziehen (Entscheidungen, Payload, API-Details) – v0.15

## Schritt 10 – Adressen für Kontakte und Organisationen (Entscheidung 14)
- [x] Value Object `Address` (Straße, Adresszusatz, PLZ, Ort, Region, Land) mit fester Länderliste (ISO 3166-1 Alpha-2, deutsche und englische Namen); Unit-Tests
- [x] `Contact.Address` und `Organization.Address` als Complex Types (Pflicht, leere Anschrift = `Address.Empty`, weil ein nullable Complex Type ohne Pflichtfeld „leer“ und „nicht vorhanden“ nicht unterscheiden kann); `Organization.City` geht in `Address.City` auf (Spalte `city` bleibt, Suche unverändert); Migration additiv
- [x] Use Cases (`Create…`, `Update…`, `Get…`) und Validierung um die Adresse erweitert
- [x] Oberfläche: Adresse als Gruppe im Inline-Editing der Detailansichten, Land als Auswahl; die Quick-Add-Dialoge bleiben schlank (Organisation: „Ort“ = `Address.City`, übrige Adressteile bleiben beim Bearbeiten erhalten)
- [x] REST-API: flache Adressfelder (Validierungsfehler am jeweiligen Feld, z. B. `countryCode`) (`street`, `street2`, `postalCode`, `city`, `region`, `countryCode`), wie beim Preis
- [x] Kontakt-Import: Adress-Zielfelder, HubSpot-Vorlage (`Street Address`, `Postal Code`, `City`, `State/Region`, `Country/Region Code` mit Ersatz `Country/Region`); unbekanntes Land = fehlerhafte Zeile; „aktualisieren“ überschreibt nur Adressteile mit Wert

## Schritt 11 – Organisationsimport (Entscheidung 15)
- [x] Import-Assistent mit Zieltyp (Kontakte / Organisationen, aus den Spalten erkannt und umschaltbar); gemeinsamer Unterbau `ImportRun` für Parsing, Spaltenprüfung, Blöcke, Bericht und Tags
- [x] Zielfelder: Name, Typ, Website, Anschrift, Tag, HubSpot-ID (Telefon und LinkedIn-Seite hat das Organisationsmodell nicht, sie werden daher nicht übernommen); Dubletten per `HubSpotRecordId`, sonst Name (case-insensitive); überspringen/aktualisieren
- [x] HubSpot-Firmenvorlage inkl. Typ-Mapping; Spaltennamen aus den Eigenschaftsdefinitionen des HubSpot-Kontos (`Company name`, `Website URL` mit Ersatz `Company Domain Name`, `Type`, Adresse, `Country/Region Code` mit Ersatz `Country/Region`, `Lifecycle Stage` → Tag, `Record ID`)
- [x] Kopfzeile eines echten Firmenexports abgeglichen (2026-10-01, 117 Spalten): alle Vorlagenspalten vorhanden und eindeutig, Zieltyp und Vorlage werden erkannt; als Testfall `HubSpotCompanyExportTests` hinterlegt
- [x] Audit und `OrganizationCreated` je Organisation wie bei manueller Anlage
- [x] Fehlt die Hauptspalte einer Vorlage, wird die Ersatzspalte zur Hauptspalte (z. B. nur `Company Domain Name`)

## Schritt 12 – Verknüpfung Kontakt ↔ Organisation (Entscheidung 16)
- [x] Zielfeld „HubSpot-Firmen-ID“ im Kontakt-Import: verknüpft mit der Organisation gleicher `HubSpotRecordId`, sonst per Firmenname wie bisher; bei mehreren IDs (`;`-getrennt) zählt die erste; eine unbekannte ID ohne Namen verknüpft nichts
- [x] Spaltenname der Zuordnung im echten Kontaktexport bestätigt (die Vorlage sucht nacheinander `Associated Company IDs (Primary)`, `Primary Associated Company ID`, `Associated Company IDs`, `Associated Company ID`)
- [x] Tests: Firmen → Kontakte importieren, Verknüpfung per ID vor Name, nachträgliche Verknüpfung per „aktualisieren“, bestehende Verknüpfung bleibt ohne Firmenangabe erhalten; Browser-Smoke-Test des Ablaufs
- [x] SPEC und README nachziehen
- [x] Ende-zu-Ende mit echtem HubSpot-Export (Firmen und Kontakte)

## Entscheidungen (2026-09-30)
1. **Hintergrundprozess:** eigener `BackgroundService` mit `PeriodicTimer` für die Outbox (ADR-008 Option B); Retry und Backoff über `Attempts`/`NextAttemptAt` der Outbox. Hangfire erst, wenn weitere Jobarten hinzukommen.
2. **Payload:** nur Ids und Status wie in den Domain Events (SPEC 5); Details holt n8n per REST-API. Keine personenbezogenen Daten in Outbox und Versandprotokoll.
3. **Alt-Nachrichten:** Eine Subscription erhält nur Events, die nach ihrer Anlage aufgetreten sind; Nachrichten ohne passende Subscription werden sofort als verarbeitet markiert.
4. **PATCH:** JSON Merge Patch (RFC 7396): fehlendes Feld bleibt unverändert, `null` leert es. Der Endpoint lädt den aktuellen Stand, überschreibt die gesendeten Felder und ruft den bestehenden `Update…`-Handler auf.
5. **OpenAPI/Scalar:** nur für den angemeldeten Nutzer (Cookie), auch in Produktion.
6. **Signatur:** HMAC-SHA256 über `<timestamp>.<body>`; Header `X-SoloCrm-Timestamp` (Unix-Sekunden) und `X-SoloCrm-Signature: sha256=<hex>`. Empfänger prüfen Signatur und Alter (< 5 min).
7. **Webhook-Ziele:** `https`; `http` nur für explizit konfigurierte Hosts (`Webhooks__AllowedHttpHosts`). Private Adressen sind erlaubt (Single-User, n8n läuft oft intern).
8. **Aufbewahrung:** verarbeitete Outbox-Nachrichten und Versandprotokoll 30 Tage; tägliches Aufräumen durch denselben Hintergrundprozess.
9. **HubSpot-Vorlage** (Kopfzeile eines echten Exports vom 2026-10-01, ~300 Spalten, Komma-getrennt, durchgehend in Anführungszeichen):

   | Kontaktfeld | HubSpot-Spalte | Ersatz, wenn leer |
   |---|---|---|
   | FirstName | `First Name` | – |
   | LastName | `Last Name` | – |
   | Email | `Email` | `Work email` |
   | Phone | `Phone Number` | `Mobile Phone Number` |
   | JobTitle | `Job Title` | `lh_current_position` |
   | LinkedInUrl | `LinkedIn URL` | `lh_linkedin_profile_url` |
   | Organisation (Name) | `Company Name` | – (bestehende per Name, sonst neu mit Typ `Other`) |
   | Website der *neu angelegten* Organisation | `Website URL` | – |
   | Source | `Original Traffic Source` | Werte-Mapping, siehe A |
   | Tag | `Lifecycle Stage` | – (siehe B) |
   | ExtraFields `HubSpotRecordId` | `Record ID` | – |

   Nicht übernommen: ~~Adressen~~ (revidiert durch Entscheidung 14), übrige Lifecycle-/Lead-Daten, Zeitstempel (`Create Date` usw.; `CreatedAt` setzt ausschließlich der Interceptor), Marketing-Kennzahlen, `Associated … IDs` (außer der Firmen-ID, Entscheidung 16).

   **Technische Folgen:** Spaltennamen sind *nicht eindeutig* (`Billing Contact IDs` kommt dreimal vor) und enthalten maskierte Anführungszeichen (`Date entered ""Kunde …""`). Das Parsing arbeitet daher mit Spaltenindizes, nicht mit Namen; die Vorlage sucht die erste passende Spalte. Bei ~300 Spalten wird das Mapping je *Zielfeld* gewählt (Zielfeld → Quellspalte), nicht je Quellspalte, und die Vorschau zeigt nur die zugeordneten Spalten. Dubletten innerhalb von SoloCRM: zuerst per E-Mail (AK2), bei Kontakten ohne E-Mail per `HubSpotRecordId`, damit ein erneuter Import keine Dubletten erzeugt.

   **Bestätigt (2026-10-01):**
   - **A – Quelle:** HubSpot-Werte → `LeadSource`: `Referrals` → Empfehlung; `Organic Search`, `Paid Search`, `Direct Traffic`, `AI Referrals` → Website; `Social Media` und `Paid Social` werden ignoriert (Quelle bleibt leer, da ungenutzt); alles andere (`Offline Sources`, `Email Marketing`, `Other Campaigns`) → Sonstige. Interne Namen (`ORGANIC_SEARCH` …) werden ebenso erkannt.
   - **B – Lifecycle Stage als Tag:** Das Mapping bietet allgemein das Zielfeld „Tag“ an (Spaltenwert wird zum Tag, bestehende Tags werden case-insensitive wiederverwendet, neue reihum eingefärbt); die HubSpot-Vorlage ordnet `Lifecycle Stage` diesem Zielfeld zu.

10. **Import:** synchron im Blazor-Circuit mit Fortschrittsanzeige, Transaktion je Block (100 Zeilen), kein Hintergrundjob.
11. **Events beim Import:** jeder importierte Kontakt löst `ContactCreated` aus (konsistent mit der manuellen Anlage); die Outbox verarbeitet sie gestaffelt.
12. **Fehlerhafte Zeilen:** Anzeige im Bericht genügt; Download erst bei Bedarf.
13. **Neue Pakete:** `CsvHelper`, `Microsoft.AspNetCore.OpenApi` (erzeugt das OpenAPI-Dokument) und `Scalar.AspNetCore` (interaktive API-Referenz zum Dokument, US-17 AK3). Kein Hangfire.

## Entscheidungen (2026-10-01, Erweiterung)
14. **Adressen:** Kontakte und Organisationen erhalten je eine vollständige Anschrift als Value Object `Address` (Straße inkl. Hausnummer, Adresszusatz, PLZ, Ort, Bundesland/Region, Land) analog zu `Pricing` (ADR-011). Das Land wird als ISO-3166-1-Alpha-2-Code gespeichert und in der Oberfläche als deutscher Name zur Auswahl angeboten; die Namensliste ist fest im Code (unabhängig von ICU). Der Import erkennt Code, deutschen und englischen Namen. Entscheidung 9 wird insoweit revidiert: Adressen werden übernommen.
15. **Organisationsimport:** derselbe Assistent mit Zieltyp „Organisationen“ und einer HubSpot-Firmenvorlage. Dubletten per `HubSpotRecordId` (in `ExtraFields`), sonst per Name (case-insensitive). Typ-Mapping: `Prospect` → Endkunde, `Partner` → Partner, `Reseller` → Vermittler, alles andere → Sonstige. Abgleich mit einem echten Firmenexport (117 Spalten, Komma-getrennt, durchgehend in Anführungszeichen): Name `Company name`, Typ `Type`, Website `Website URL` (Ersatz `Company Domain Name`), Anschrift `Street Address`, `Street Address 2`, `Postal Code`, `City`, `State/Region`, `Country/Region Code` (Ersatz `Country/Region`), Tag `Lifecycle Stage`, HubSpot-ID `Record ID`. Nicht übernommen u. a. `Phone Number`, `LinkedIn Company Page`, `Industry`, `Description` (keine Felder im Modell).
16. **Verknüpfung:** Der Kontakt-Import verknüpft über die HubSpot-Firmen-ID aus der Zuordnungsspalte des Kontaktexports, ohne Treffer über den Firmennamen. Reihenfolge: erst Organisationen, dann Kontakte; bereits importierte Kontakte lassen sich per „aktualisieren“ nachträglich verknüpfen.
17. **Einplanung:** Die Erweiterung gehört zu Iteration 5 und geht mit ihr in Produktion.

## Definition of Done
- CI grün (Build + alle Tests); keine Warnings
- Ein Stage-Wechsel erreicht einen n8n-Webhook signiert und wird dort erfolgreich geprüft; Fehler werden mit Backoff wiederholt und protokolliert
- Kontakte lassen sich per REST-API mit API-Key anlegen, lesen und ändern; ohne gültigen Key gibt es 401, über 60 Requests/Minute 429
- OpenAPI-Dokument und Scalar-UI sind erreichbar
- Ein echter HubSpot-Export lässt sich mit Vorschau, Mapping und Dublettenprüfung importieren; der Bericht nennt Zeile und Grund jedes Fehlers
- Kontakte und Organisationen haben eine vollständige Anschrift; HubSpot-Firmen und -Kontakte lassen sich nacheinander importieren und sind danach verknüpft
- Keine Secrets oder personenbezogenen Daten in Logs; API-Keys nur gehasht, Webhook-Secrets verschlüsselt gespeichert
- In Produktion deployt, Migration ohne Datenverlust
