# Iteration 6 – DSGVO & Politur

**Ziel:** SoloCRM erfüllt Auskunfts- und Löschwünsche nachweisbar, das Konto ist per optionaler 2FA geschützt, und das MVP ist abgeschlossen: Kontoseiten auf Deutsch im App-Layout, Dokumentation, Screenshots und ADRs final.

**Stories:** US-19, US-20, US-21 AK2 (2FA)
**Referenzen:** `docs/SPEC.md` Kap. 2.2 (Löschen), 2.3 (Activity, TaskItem), 2.4 (AuditEntry), 3.3 S5/S6, 4 (US-19 – US-21), 6 (Sicherheit, Datenschutz, Wartbarkeit), 8 (DoD It. 6), 10 (offene Fragen); ADR-004, ADR-006, ADR-009, ADR-010

**Aus früheren Iterationen übernommen:**
- Activities werden beim harten Löschen eines Kontakts per `ON DELETE CASCADE` mitgelöscht, Tasks ebenfalls (It. 3, Vorbereitung für US-20); Anfragen behalten die Anfrage und verlieren nur `PrimaryContactId` (`SET NULL`).
- Der `AuditInterceptor` schreibt beim Löschen alle bisherigen Feldwerte in den Eintrag `Deleted` (ADR-006). Für US-20 ist das genau falsch: die personenbezogenen Daten landeten im Audit. Datenbank-Cascades sieht der Interceptor außerdem nicht; Audit-Einträge mitgelöschter Activities und Tasks (Betreff, Text, Titel) blieben unverändert stehen.
- Die Identity-Seiten unter `/Account/Manage` (Passwort, 2FA, Wiederherstellungscodes, Passkeys, E-Mail, „Personal Data“) stammen unverändert aus dem .NET-Template: englisch, Bootstrap-Markup, ohne QR-Code für die Authenticator-App. Der Login-Rate-Limiter deckt `LoginWith2fa` und `LoginWithRecoveryCode` bereits ab (It. 1).
- Outbox, Versandprotokoll und Logs enthalten nur Ids und Status (It. 5); Kontaktdaten stehen dort nicht.
- Das Volume `/app/keys` ist betriebskritisch (Login-Cookies, Webhook-Secrets), wird aber von den Postgres-Backups nicht erfasst (ADR-009, Konsequenzen); eine Sicherung ist noch nicht beschrieben.

**Bewusst nicht in dieser Iteration:**
- Löschen von Organisationen und Anfragen als DSGVO-Vorgang (Organisationen sind keine natürlichen Personen; Archivieren bleibt der Normalfall)
- Sperrliste gegen erneuten Import gelöschter Kontakte (wäre selbst ein personenbezogenes Datum; siehe Frage 4)
- Mehrere Benutzer, Rollen, OIDC (Single-User, SPEC 1.4)
- Löschung aus bestehenden Backups (laufen nach 30 Tagen aus; wird dokumentiert)
- Neue Fachfunktionen aus dem Backlog (Datenanreicherung, Dashboard, Nextcloud-Sync)

---

## Schritt 1 – DSGVO-Auskunft: Use Case (US-19)
- [ ] `ExportContactData(ContactId)`: JSON mit Stammdaten (inkl. Anschrift, Quelle, `ExtraFields`, Organisation, Tags), Activities und Tasks mit direktem Bezug, AuditEntries des Kontakts und seiner Activities/Tasks, Anfragen mit dem Kontakt als Ansprechpartner (Umfang: Frage 1)
- [ ] Stabiles, dokumentiertes Format (camelCase, ISO-Zeitstempel in UTC, Versionsfeld `formatVersion`), Zeitpunkt des Exports
- [ ] Archivierte Kontakte sind exportierbar

## Schritt 2 – DSGVO-Auskunft: Oberfläche (US-19)
- [ ] Aktion „Daten exportieren (DSGVO)“ in der Kontakt-Detailansicht; Download als `kontakt-<name>-<datum>.json` per JS-Interop (ohne Zwischenspeicherung auf dem Server)
- [ ] Optional REST-Endpunkt (Frage 5)

## Schritt 3 – DSGVO-Löschung: Use Case (US-20)
- [ ] `DeleteContactPermanently(ContactId, Confirmation)`: löscht den Kontakt, Activities und Tasks mit direktem Bezug (Umfang bei mehrfach verknüpften Activities: Frage 2), Tag-Zuordnungen; Anfragen bleiben, `PrimaryContactId` wird geleert
- [ ] Audit: Eintrag `Deleted` für den Kontakt **ohne** Feldwerte (US-20 AK2); vorhandene AuditEntries des Kontakts sowie der mitgelöschten Activities und Tasks werden anonymisiert (`Changes` geleert, Aktion bleibt); AuditEntries anderer Datensätze, die nur die Id des Kontakts enthalten (z. B. `PrimaryContactId` einer Anfrage), bleiben (Frage 3)
- [ ] Mitgelöschte Activities und Tasks explizit laden und löschen statt nur per Cascade, damit sie einheitlich behandelt werden; alles in einer Transaktion
- [ ] Domain Event `ContactDeleted` (nur Id) → Webhook `contact.deleted` (Frage 4)
- [ ] Keine personenbezogenen Daten in Logs; Log-Eintrag nur mit Id

## Schritt 4 – DSGVO-Löschung: Oberfläche (US-20)
- [ ] Aktion „Endgültig löschen (DSGVO)“ in der Kontakt-Detailansicht, deutlich getrennt vom Archivieren
- [ ] Bestätigungsdialog nennt die Folgen (Anzahl Activities und Tasks, betroffene Anfragen, keine Wiederherstellung, Backups laufen nach 30 Tagen aus) und verlangt die Eingabe des Namens (Frage 6)
- [ ] Danach Weiterleitung zur Kontaktliste mit Hinweis; Timeline der Organisation und der Anfragen zeigt keine Inhalte des Kontakts mehr

## Schritt 5 – 2FA und Kontoseiten (US-21 AK2)
- [ ] Kontoseiten auf Deutsch und im App-Layout (MudBlazor statt Template-Bootstrap); Abschnitt „Konto“ in `/settings` verlinkt Passwort ändern und 2FA (SPEC 3.3 S6)
- [ ] TOTP einrichten mit QR-Code (Frage 7) und manuell eingebbarem Schlüssel; Wiederherstellungscodes anzeigen, neu erzeugen, 2FA deaktivieren, Authenticator zurücksetzen
- [ ] Login mit 2FA und mit Wiederherstellungscode auf Deutsch; Rate-Limit bleibt wirksam
- [ ] Nicht benötigte Template-Seiten entfernen oder sperren (E-Mail ändern, „Personal Data“ herunterladen/löschen des Admin-Kontos, ggf. Passkeys; Frage 8); Architektur- bzw. Autorisierungstest, dass sie nicht erreichbar sind

## Schritt 6 – Betrieb und Datenschutz-Doku
- [ ] Sicherung des Volumes `/app/keys` beschreiben und einmal testen (Wiederherstellung: Login ohne Neuanmeldung, Webhook-Secret lesbar) in `deploy/coolify.md`
- [ ] Datenschutz-Abschnitt (`docs/datenschutz.md`): welche Daten wo liegen (DB, Backups, Logs, Outbox, Versandprotokoll, `/app/keys`), Aufbewahrung, Ablauf von Auskunft und Löschung inkl. Löschung im Quellsystem (HubSpot, n8n)

## Schritt 7 – Tests
- [ ] Unit-Tests: Exportformat, Anonymisierung der Audit-Änderungen
- [ ] Handler-Tests (Happy Path + Validierungsfehler) für Export und Löschung, u. a. falsche Bestätigung, unbekannter Kontakt
- [ ] Integrationstests: Löschung entfernt Kontakt, Activities, Tasks, Tag-Zuordnungen; Anfragen bleiben ohne Ansprechpartner; keine Feldwerte des Kontakts mehr in `audit_entries` (Suche nach Name, E-Mail, Activity-Text über die ganze Tabelle); Timeline der Organisation ohne Inhalte; Outbox enthält `contact.deleted`
- [ ] Export enthält alle Activities, Tasks und AuditEntries; nichts von anderen Kontakten
- [ ] Web-Tests: Kontoseiten erfordern Login; entfernte Template-Seiten nicht erreichbar; Login mit 2FA (TOTP aus dem Test-Schlüssel berechnet) und mit Wiederherstellungscode
- [ ] bUnit: Löschdialog (Bestätigung per Name), Export-Aktion
- [ ] Testabdeckung Domain/Application messen (NFR ≥ 80 %, Frage 9)

## Schritt 8 – Abschluss MVP
- [ ] Migrationen (falls nötig) erzeugt und per `efbundle` in Produktion ausgerollt
- [ ] 2FA in Produktion aktiviert und Login mit Authenticator und Wiederherstellungscode geprüft
- [ ] ADRs final durchgehen (Status, Erfahrungen); ADR-006 um Löschen/Anonymisieren ergänzen
- [ ] SPEC: US-19/20/21 mit Umsetzungsdetails, Kap. 10 (offene Fragen zu Datenanreicherung und Stundensatz-Varianten) beantworten oder in den Backlog verschieben, Iterationsplan abschließen
- [ ] README: Stand MVP, Screenshots aktualisieren (u. a. Kontoseite mit 2FA, Löschdialog), Funktionsübersicht

## Offene Fragen (vor dem Start zu klären)
1. **Umfang der Auskunft:** US-19 nennt Stammdaten, Activities, Tasks und AuditEntries. Sollen zusätzlich die Anfragen hinein, bei denen der Kontakt Ansprechpartner ist (Titel, Phase, Preis), und die Activities dieser Anfragen? *Vorschlag: Anfragen mit Titel, Phase und Rolle des Kontakts ja (sie sind Teil seiner Geschichte); deren Activities nein, sofern sie den Kontakt nicht direkt referenzieren.*
2. **Mehrfach verknüpfte Activities:** Eine Activity kann zugleich auf Kontakt, Organisation und Anfrage verweisen (z. B. Gesprächsnotiz zur Anfrage mit dem Kontakt). Löschen oder nur den Bezug zum Kontakt entfernen? *Vorschlag: löschen (US-20 AK1 „mit direktem Bezug“; der Text handelt in der Regel von der Person). Tasks ebenso.*
3. **Anonymisierung im Audit:** Reicht es, die Einträge des Kontakts und seiner mitgelöschten Activities/Tasks zu leeren, oder sollen auch Einträge anderer Datensätze, die die Kontakt-Id enthalten (`PrimaryContactId`), bereinigt werden? *Vorschlag: nur die eigenen und die der mitgelöschten Datensätze; eine Id ohne Datensatz ist nicht mehr personenbeziehbar. Der `Deleted`-Eintrag des Kontakts bleibt ohne Feldwerte als Nachweis der Löschung.*
4. **Event `contact.deleted`:** Soll das Löschen per Webhook gemeldet werden, damit n8n die Person auch in HubSpot oder anderen Systemen löschen kann? Ohne Sperrliste würde ein späterer HubSpot-Import den Kontakt sonst wieder anlegen. *Vorschlag: ja, neues Event mit nur der Id (`ContactDeleted`), wählbar in den Webhooks; keine Sperrliste.*
5. **Export per API:** Auskunft und Löschung auch über die REST-API (`GET /contacts/{id}/export`, `DELETE /contacts/{id}`)? *Vorschlag: Export ja (n8n kann Auskunftsanfragen automatisieren), Löschung nein (irreversibel, bewusst nur in der Oberfläche mit Bestätigung).*
6. **Bestätigung der Löschung:** Einfache Ja/Nein-Frage oder Eingabe des Kontaktnamens? *Vorschlag: Eingabe des Namens (wie bei GitHub), der Handler prüft die Bestätigung ebenfalls.*
7. **QR-Code für 2FA:** Das Template zeigt nur den Schlüssel. Optionen: neues Paket `QRCoder` (serverseitig SVG, kein JavaScript), eine JS-Bibliothek oder nur der Schlüssel. *Vorschlag: `QRCoder` (MIT, ohne Abhängigkeiten); Schlüssel zusätzlich als Text.*
8. **Template-Seiten:** E-Mail ändern, „Personal Data“ herunterladen/löschen (löscht den einzigen Admin) und Passkeys. *Vorschlag: E-Mail ändern und Personal Data entfernen (E-Mail kommt aus der Konfiguration, Löschen des Admins sperrt aus); Passkeys behalten und übersetzen, falls gewünscht, sonst entfernen. Frage dazu: Passkeys nutzen oder nur TOTP?*
9. **Testabdeckung:** Die NFR fordert ≥ 80 % für Domain und Application, gemessen wurde bisher nicht. Neues Paket `Microsoft.Testing.Extensions.CodeCoverage` (passt zu Microsoft.Testing.Platform) mit Bericht in der CI? *Vorschlag: ja, zunächst nur messen und im README ausweisen; ein hartes Gate erst, wenn der Wert stabil ist.*
10. **Offene Fragen aus SPEC 10:** Datenanreicherung (App oder n8n) und Stundensatz-Varianten. *Vorschlag: beide in den Backlog verschieben und im MVP nicht entscheiden.*

## Definition of Done
- CI grün (Build + alle Tests); keine Warnings
- Ein Kontakt lässt sich als JSON exportieren; der Export enthält alle Activities, Tasks und AuditEntries
- Ein Kontakt lässt sich nach Bestätigung endgültig löschen; danach findet sich in der Datenbank kein Feldwert mehr von ihm, der `Deleted`-Eintrag bleibt als Nachweis
- 2FA ist in Produktion aktiv; Login mit Authenticator und mit Wiederherstellungscode funktioniert; alle Kontoseiten sind deutsch
- Sicherung von `/app/keys` beschrieben und getestet; Datenschutz-Doku vorhanden
- README, Screenshots, SPEC und ADRs auf MVP-Stand
- In Produktion deployt
