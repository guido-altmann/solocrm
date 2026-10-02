# Iteration 6 – DSGVO & Politur

**Ziel:** SoloCRM erfüllt Auskunfts- und Löschwünsche nachweisbar, das Konto ist per optionaler 2FA geschützt, und das MVP ist abgeschlossen: Kontoseiten auf Deutsch im App-Layout, Dokumentation, Screenshots und ADRs final.

**Stories:** US-19, US-20, US-21 AK2 (2FA), Erweiterung: Eingangsdatum für Anfragen (Entscheidung 11)
**Referenzen:** `docs/SPEC.md` Kap. 2.2 (Löschen), 2.3 (Opportunity, Activity, TaskItem), 2.4 (AuditEntry), 2.6 (Domain Events), 3.3 S1/S2/S5/S6, 4 (US-06, US-19 – US-21), 5 (REST-API), 6 (Sicherheit, Datenschutz, Wartbarkeit), 8 (DoD It. 6, Backlog), 10 (offene Fragen); ADR-004, ADR-006, ADR-009, ADR-010

**Aus früheren Iterationen übernommen:**
- Activities werden beim harten Löschen eines Kontakts per `ON DELETE CASCADE` mitgelöscht, Tasks ebenfalls (It. 3, Vorbereitung für US-20); Anfragen behalten die Anfrage und verlieren nur `PrimaryContactId` (`SET NULL`).
- Der `AuditInterceptor` schreibt beim Löschen alle bisherigen Feldwerte in den Eintrag `Deleted` (ADR-006). Für US-20 ist das genau falsch: die personenbezogenen Daten landeten im Audit. Datenbank-Cascades sieht der Interceptor außerdem nicht; Audit-Einträge mitgelöschter Activities und Tasks (Betreff, Text, Titel) blieben unverändert stehen.
- Die Identity-Seiten unter `/Account/Manage` (Passwort, 2FA, Wiederherstellungscodes, Passkeys, E-Mail, „Personal Data“) stammen unverändert aus dem .NET-Template: englisch, Bootstrap-Markup, ohne QR-Code für die Authenticator-App. Der Login-Rate-Limiter deckt `LoginWith2fa` und `LoginWithRecoveryCode` bereits ab (It. 1).
- Outbox, Versandprotokoll und Logs enthalten nur Ids und Status (It. 5); Kontaktdaten stehen dort nicht.
- Anfragen kennen nur das technische `CreatedAt` (Interceptor, Regel 7). Es steuert die Sortierung der Anfrageliste, den Rückfall für „eingeschlafen“ auf „Heute“ und im Pipeline-Board (ohne Activity zählt die Anlage) und den Timeline-Eintrag „Angelegt“. Nachträglich erfasste Anfragen erscheinen dadurch zu neu und werden zu spät als eingeschlafen erkannt.
- Das Volume `/app/keys` ist betriebskritisch (Login-Cookies, Webhook-Secrets), wird aber von den Postgres-Backups nicht erfasst (ADR-009, Konsequenzen); eine Sicherung ist noch nicht beschrieben.

**Bewusst nicht in dieser Iteration:**
- Löschen von Organisationen und Anfragen als DSGVO-Vorgang (Organisationen sind keine natürlichen Personen; Archivieren bleibt der Normalfall)
- Sperrliste gegen erneuten Import gelöschter Kontakte (wäre selbst ein personenbezogenes Datum; Entscheidung 4)
- Löschen per REST-API (`DELETE /contacts/{id}`; Entscheidung 5)
- E-Mail-Adresse des Kontos ändern (kommt aus der Konfiguration; Entscheidung 8)
- Hartes Gate für die Testabdeckung in der CI (Entscheidung 9)
- Mehrere Benutzer, Rollen, OIDC (Single-User, SPEC 1.4)
- Löschung aus bestehenden Backups (laufen nach 30 Tagen aus; wird dokumentiert)
- Neue Fachfunktionen aus dem Backlog (Datenanreicherung, Dashboard, Nextcloud-Sync, Stundensatz-Varianten; Entscheidung 10)

---

## Schritt 1 – DSGVO-Auskunft: Use Case (US-19)
- [x] `ExportContactData(ContactId)`: JSON mit Stammdaten (inkl. Anschrift, Quelle, `ExtraFields`, Organisation, Tags), Activities und Tasks mit direktem Bezug, AuditEntries des Kontakts und seiner Activities/Tasks, Anfragen mit dem Kontakt als Ansprechpartner mit Titel, Phase und Rolle des Kontakts; Activities dieser Anfragen nur, wenn sie den Kontakt direkt referenzieren (Entscheidung 1)
- [x] Stabiles, dokumentiertes Format (camelCase, ISO-Zeitstempel in UTC, Versionsfeld `formatVersion`), Zeitpunkt des Exports
- [x] Archivierte Kontakte sind exportierbar

## Schritt 2 – DSGVO-Auskunft: Oberfläche (US-19)
- [x] Aktion „Daten exportieren (DSGVO)“ in der Kontakt-Detailansicht; Download als `kontakt-<name>-<datum>.json` per JS-Interop (ohne Zwischenspeicherung auf dem Server)
- [x] REST-Endpunkt `GET /api/v1/contacts/{id}/export` (gleicher Handler, gleiches Format; Entscheidung 5); in OpenAPI dokumentiert

## Schritt 3 – DSGVO-Löschung: Use Case (US-20)
- [x] `DeleteContactPermanently(ContactId, Confirmation)`: löscht den Kontakt, Activities und Tasks mit direktem Bezug, auch wenn sie zusätzlich auf Organisation oder Anfrage verweisen (Entscheidung 2), Tag-Zuordnungen; Anfragen bleiben, `PrimaryContactId` wird geleert
- [x] Audit: Eintrag `Deleted` für den Kontakt **ohne** Feldwerte (US-20 AK2); vorhandene AuditEntries des Kontakts sowie der mitgelöschten Activities und Tasks werden anonymisiert (`Changes` geleert, Aktion bleibt); AuditEntries anderer Datensätze, die nur die Id des Kontakts enthalten (z. B. `PrimaryContactId` einer Anfrage), bleiben (Entscheidung 3)
- [x] Mitgelöschte Activities und Tasks explizit laden und löschen statt nur per Cascade, damit sie einheitlich behandelt werden; alles in einer Transaktion
- [x] Domain Event `ContactDeleted` (nur Id) → Webhook `contact.deleted`, in den Webhooks wählbar; SPEC 2.6 und 5 ergänzen (Entscheidung 4)
- [x] Keine personenbezogenen Daten in Logs; Log-Eintrag nur mit Id

## Schritt 4 – DSGVO-Löschung: Oberfläche (US-20)
- [x] Aktion „Endgültig löschen (DSGVO)“ in der Kontakt-Detailansicht, deutlich getrennt vom Archivieren
- [x] Bestätigungsdialog nennt die Folgen (Anzahl Activities und Tasks, betroffene Anfragen, keine Wiederherstellung, Backups laufen nach 30 Tagen aus) und verlangt die Eingabe des Namens; der Handler prüft die Bestätigung ebenfalls (Entscheidung 6)
- [x] Danach Weiterleitung zur Kontaktliste mit Hinweis; Timeline der Organisation und der Anfragen zeigt keine Inhalte des Kontakts mehr

## Schritt 5 – 2FA und Kontoseiten (US-21 AK2)
- [x] Kontoseiten auf Deutsch und im App-Layout (MudBlazor statt Template-Bootstrap); Abschnitt „Konto“ in `/settings` verlinkt Passwort ändern, 2FA und Passkeys (SPEC 3.3 S6)
- [x] TOTP einrichten mit QR-Code (`QRCoder`, serverseitig als SVG, über `Directory.Packages.props`; Entscheidung 7) und manuell eingebbarem Schlüssel; Wiederherstellungscodes anzeigen, neu erzeugen, 2FA deaktivieren, Authenticator zurücksetzen
- [x] Passwort ändern und Passkeys (hinzufügen, umbenennen, entfernen) auf Deutsch im App-Layout (Entscheidung 8)
- [x] Login mit 2FA, mit Wiederherstellungscode und mit Passkey auf Deutsch; Rate-Limit bleibt wirksam
- [x] Template-Seiten „E-Mail ändern“ und „Personal Data“ (herunterladen/löschen des Admin-Kontos) samt Navigation entfernen (Entscheidung 8); Test, dass sie nicht erreichbar sind

## Schritt 6 – Eingangsdatum für Anfragen (Entscheidung 11)
- [x] Neues Feld `Opportunity.ReceivedOn` (`DateOnly`, Pflicht, UI „Eingegangen am“); Default beim Anlegen: heute in `App__TimeZone`; nicht in der Zukunft (Validator); `CreatedAt`/`UpdatedAt` bleiben technisch und kommen weiter aus dem Interceptor (Regel 7)
- [x] `CreateOpportunity` und `UpdateOpportunity` nehmen das Datum an; Anlegen-Dialog mit Datumsfeld, Detailansicht inline editierbar; Änderungen landen im Audit wie andere Felder
- [x] Fachliche Verwendung umstellen: Sortierung der Anfrageliste (`ReceivedOn`, dann `CreatedAt`), Rückfall für „eingeschlafen“ auf „Heute“ und im Pipeline-Board ohne Activity (Beginn des Eingangstags in `App__TimeZone`), verwandte Anfragen in der Detailansicht
- [x] Timeline: der Eintrag „Angelegt“ bleibt am technischen Zeitpunkt und nennt zusätzlich „Eingegangen am …“, wenn das Datum vom Anlagetag abweicht
- [x] REST-API: `receivedOn` (ISO-Datum) in Antwort, `POST` (optional, Default heute) und `PATCH`; OpenAPI aktualisiert
- [x] Migration (additiv): Spalte `received_on` anlegen, aus `created_at` in `Europe/Berlin` befüllen, danach `NOT NULL`
- [x] SPEC: 2.3 (Feld), US-06 (neues AK), S1 (Definition „eingeschlafen“), 5 (API-Feld)

## Schritt 7 – Betrieb und Datenschutz-Doku
- [x] Sicherung des Volumes `/app/keys` beschreiben und einmal testen (Wiederherstellung: Login ohne Neuanmeldung, Webhook-Secret lesbar) in `deploy/coolify.md` (lokal mit dem Produktions-Image getestet, Entscheidung 18; der Test auf dem Server steht in der Checkliste von `deploy/coolify.md` Abschnitt 9)
- [x] Datenschutz-Abschnitt (`docs/datenschutz.md`): welche Daten wo liegen (DB, Backups, Logs, Outbox, Versandprotokoll, `/app/keys`), Aufbewahrung, Ablauf von Auskunft und Löschung inkl. Löschung im Quellsystem (HubSpot, n8n)

## Schritt 8 – Tests
- [x] Unit-Tests: Exportformat, Anonymisierung der Audit-Änderungen
- [x] Handler-Tests (Happy Path + Validierungsfehler) für Export und Löschung, u. a. falsche Bestätigung, unbekannter Kontakt
- [x] Integrationstests: Löschung entfernt Kontakt, Activities, Tasks, Tag-Zuordnungen; Anfragen bleiben ohne Ansprechpartner; keine Feldwerte des Kontakts mehr in `audit_entries` (Suche nach Name, E-Mail, Activity-Text über die ganze Tabelle); Timeline der Organisation ohne Inhalte; Outbox enthält `contact.deleted`
- [x] Export enthält alle Activities, Tasks und AuditEntries; nichts von anderen Kontakten
- [x] Web-Tests: Kontoseiten erfordern Login; entfernte Template-Seiten nicht erreichbar; Login mit 2FA (TOTP aus dem Test-Schlüssel berechnet) und mit Wiederherstellungscode; Passwort ändern; QR-Code enthält die `otpauth`-URI
- [x] API-Test: `GET /contacts/{id}/export` mit und ohne Key, unbekannte Id → 404
- [x] Eingangsdatum: Unit-/Handler-Tests (Default heute, Zukunft abgelehnt, Änderung auditiert), Integrationstests für Migration (Befüllung aus `created_at`), Sortierung und „eingeschlafen“ bei nachträglich erfasster Anfrage; API-Tests für `receivedOn`
- [x] bUnit: Löschdialog (Bestätigung per Name), Export-Aktion
- [x] Testabdeckung messen: Paket `Microsoft.Testing.Extensions.CodeCoverage` (über `Directory.Packages.props`), Bericht für Domain und Application als CI-Artefakt, Wert im README ausweisen; Lücken unter 80 % (NFR) schließen, soweit sinnvoll; kein hartes Gate (Entscheidung 9)
- [x] Browser-Smoke-Test (Playwright, Wegwerf-DB): 2FA einrichten per QR-Code, Login mit TOTP und Wiederherstellungscode, Export-Download, Löschdialog, nachträglich erfasste Anfrage

## Schritt 9 – Abschluss MVP
- [x] Migration `AddOpportunityReceivedOn` erzeugt (additiv, mit Befüllung aus `created_at`)
- [ ] Per `efbundle` in Produktion ausgerollt (mit dem Deploy nach dem Push auf `main`)
- [ ] 2FA in Produktion aktiviert und Login mit Authenticator, Wiederherstellungscode und Passkey geprüft
- [x] ADRs final durchgehen (Status, Erfahrungen); ADR-006 um Löschen/Anonymisieren ergänzen
- [x] SPEC: US-19/20/21 mit Umsetzungsdetails, Kap. 10: Datenanreicherung (App oder n8n) und Stundensatz-Varianten in den Backlog (Kap. 8) verschieben (Entscheidung 10), Iterationsplan abschließen
- [x] README: Stand MVP, Screenshots aktualisieren (u. a. Kontoseite mit 2FA, Löschdialog), Funktionsübersicht, Testabdeckung

## Entscheidungen (2026-10-02)
1. **Umfang der Auskunft:** Anfragen, bei denen der Kontakt Ansprechpartner ist, gehören mit Titel, Phase und Rolle des Kontakts in den Export. Activities dieser Anfragen nur, wenn sie den Kontakt direkt referenzieren.
2. **Mehrfach verknüpfte Activities und Tasks:** werden mitgelöscht, sobald sie direkt auf den Kontakt verweisen, auch wenn sie zusätzlich auf Organisation oder Anfrage zeigen (US-20 AK1 „mit direktem Bezug“).
3. **Anonymisierung im Audit:** nur die Einträge des Kontakts und der mitgelöschten Activities/Tasks werden geleert. Einträge anderer Datensätze mit der Kontakt-Id bleiben (eine Id ohne Datensatz ist nicht mehr personenbeziehbar). Der `Deleted`-Eintrag des Kontakts bleibt ohne Feldwerte als Nachweis.
4. **Event `contact.deleted`:** neues Domain Event `ContactDeleted` mit nur der Id, in den Webhooks wählbar, damit n8n die Person in HubSpot und anderen Systemen löschen kann. Keine Sperrliste.
5. **API:** Export per `GET /api/v1/contacts/{id}/export` ja; Löschung per API nein (irreversibel, nur in der Oberfläche mit Bestätigung).
6. **Bestätigung der Löschung:** Eingabe des Kontaktnamens; der Handler prüft die Bestätigung ebenfalls.
7. **QR-Code:** neues Paket `QRCoder` (MIT, ohne Abhängigkeiten), serverseitig als SVG; der Schlüssel steht zusätzlich als Text da.
8. **Kontoseiten:** „E-Mail ändern“ und „Personal Data“ werden entfernt. Passwort ändern, TOTP (inkl. Wiederherstellungscodes) und Passkeys bleiben und werden übersetzt und ins App-Layout überführt.
9. **Testabdeckung:** neues Paket `Microsoft.Testing.Extensions.CodeCoverage`; in dieser Iteration messen, Bericht in der CI, Wert im README. Ein hartes Gate erst, wenn der Wert stabil ist.
10. **Offene Fragen aus SPEC 10:** Datenanreicherung und Stundensatz-Varianten wandern in den Backlog und werden im MVP nicht entschieden.
11. **Eingangsdatum für Anfragen (Erweiterung):** Anfragen werden teils nachträglich erfasst, deshalb muss das Datum beim Anlegen und Bearbeiten wählbar sein. Statt `CreatedAt` zu überschreiben (Regel 7, ADR-006: Audit und Timestamps nur aus Interceptors) bekommt die Anfrage das fachliche Feld `ReceivedOn` („Eingegangen am“, Datum ohne Uhrzeit, Default heute, nicht in der Zukunft). Alle fachlichen Auswertungen (Sortierung, „eingeschlafen“, Pipeline-Board) nutzen künftig `ReceivedOn`; `CreatedAt` bleibt der technische Zeitpunkt der Erfassung. Bestehende Anfragen erhalten ihr Anlagedatum. Kein neuer ADR nötig, da kein Architekturprinzip berührt wird.

## Entscheidungen bei der Umsetzung (2026-10-02)
12. **Weitere Template-Seiten entfernt:** Neben „E-Mail ändern“ und „Personal Data“ auch „Passwort setzen“ (der Admin hat immer ein Passwort) und alle Seiten, die auf E-Mail-Versand angewiesen sind (Passwort vergessen/zurücksetzen, E-Mail bestätigen). SoloCRM versendet keine E-Mails, die Seiten konnten nie funktionieren; der No-op-Mailversand ist ebenfalls entfernt. Bei vergessenem Passwort oder verlorener 2FA hilft die Datenbank (`deploy/coolify.md`, Fehlersuche).
13. **Kontoseiten bleiben statisch gerendert:** Anmelden und Passwortänderung schreiben das Cookie über den `HttpContext`. Genutzt werden das App-Theme und die rein darstellenden MudBlazor-Komponenten (Layout, Buttons, Hinweise, Tabellen); Eingabefelder sind native Elemente im Stil der App (`AccountField`, `app.css`), weil MudBlazor-Eingaben Interaktivität brauchen. Bootstrap ist entfernt, Identity-Fehler sind deutsch (`GermanIdentityErrorDescriber`). Passwortregeln bleiben die Identity-Defaults.
14. **Löschung im Interceptor:** Ein Marker `IErasable` (nur `Contact`) macht das Löschen zur DSGVO-Löschung; die mitgelöschten Datensätze leitet der `AuditInterceptor` aus den kaskadierenden Fremdschlüsseln des EF-Modells ab. Auch früher gelöschte Activities und Tasks des Kontakts werden anonymisiert; gefunden werden sie per jsonb-Containment über ihren `ContactId`-Eintrag im Audit (ADR-006). Der Export nutzt dieselbe Suche.
15. **Exportformat:** Aufzählungen in camelCase (`call`, `primaryContact`), Zeitstempel in UTC mit Millisekunden und `Z`, Umlaute unmaskiert, eingerückt. Dokumentiert in `docs/datenschutz.md`.
16. **Eingangsdatum:** `UpdateOpportunity` lässt das Datum unverändert, wenn keins übergeben wird; im `PATCH` ist `receivedOn: null` ein Validierungsfehler (Pflichtfeld). Ohne Activity zählt für „eingeschlafen“ und das Pipeline-Board der Beginn des Eingangstags in `App__TimeZone`.
17. **Testabdeckung:** Jedes Testprojekt schreibt einen Cobertura-Bericht; `tests/coverage-summary.py` führt sie zusammen (eine Zeile gilt als abgedeckt, wenn ein Projekt sie abdeckt), ohne zusätzliches Tool. Unter Instrumentierung referenziert `SoloCrm.Domain.dll` zusätzlich `netstandard`; der Architekturtest lässt das zu. Stand: Domain 99,6 %, Application 98,5 %.
18. **Sicherung `/app/keys`:** verschlüsseltes `tar`-Archiv (`openssl enc`), monatlich und nach dem ersten Start; Restore lokal mit dem Produktions-Image getestet (Sitzung und Webhook-Secret gültig, Gegenprobe mit leerem Volume: Sitzung verloren).

## Definition of Done
- CI grün (Build + alle Tests); keine Warnings
- Ein Kontakt lässt sich als JSON exportieren; der Export enthält alle Activities, Tasks und AuditEntries
- Ein Kontakt lässt sich nach Bestätigung endgültig löschen; danach findet sich in der Datenbank kein Feldwert mehr von ihm, der `Deleted`-Eintrag bleibt als Nachweis
- 2FA ist in Produktion aktiv; Login mit Authenticator und mit Wiederherstellungscode funktioniert; alle Kontoseiten sind deutsch
- Sicherung von `/app/keys` beschrieben und getestet; Datenschutz-Doku vorhanden
- Das Eingangsdatum einer Anfrage lässt sich beim Anlegen, Bearbeiten und per API setzen; Liste, „Heute“ und Pipeline-Board richten sich danach
- Testabdeckung für Domain und Application gemessen und im README ausgewiesen
- README, Screenshots, SPEC und ADRs auf MVP-Stand
- In Produktion deployt
