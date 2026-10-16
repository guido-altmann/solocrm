# ADR-014: Nextcloud-Synchronisation über CardDAV/CalDAV

**Status:** Accepted
**Datum:** 2026-10-16
**Entscheider:** Guido Altmann

## Kontext
Kontakte und Tasks sollen in Nextcloud und damit auf dem Handy (z. B. über DAVx5) verfügbar sein (Epic SOL-2, Konzept in SPEC Kap. 8). Die fachlichen Leitplanken sind bereits festgelegt: Kontakte gehen nur in eine Richtung (SoloCRM → Nextcloud), bei Tasks gibt es zusätzlich einen Rückkanal für Erledigen und Wiedereröffnen. SoloCRM ist Master; andere Änderungen in Nextcloud werden beim nächsten Abgleich überschrieben. Der Zugriff läuft über die Standardschnittstellen CardDAV (RFC 6352) und CalDAV (RFC 4791), nicht über die Nextcloud-AppAPI.

Randbedingungen: SoloCRM ist Single-User und hat einen eigenen `BackgroundService` für periodische Arbeit (ADR-008). Zugangsdaten werden per Data Protection verschlüsselt (ADR-004, ADR-013). Für ICS gibt es bereits `Ical.Net` (SOL-6). Neue Pakete laufen über Central Package Management (Regel 10). Die DSGVO-Löschung (US-20) muss auch Kopien erfassen, die SoloCRM selbst angelegt hat.

## Entscheidung
1. **DAV-Protokoll direkt über `HttpClient`**, ohne WebDAV-Paket. Benötigt werden `PROPFIND` (Discovery, ETags), `REPORT` (`sync-collection`, RFC 6578), `MKCOL` mit Body (RFC 5689), `MKCALENDAR`, `PUT` mit `If-Match`/`If-None-Match` und `DELETE`. Die XML-Bodies sind klein und fest; das Muster entspricht ADR-013 (Graph per `HttpClient`).
2. **Pakete:** `FolkerKinzel.VCards` (MIT, net10.0) schreibt die vCards, das vorhandene `Ical.Net` schreibt und liest `VTODO`. Es kommt also genau ein neues Paket hinzu.
3. **Abstraktion:** `IDavSyncTarget` (Arbeitstitel) in `Application/Abstractions`, die Implementierung in `Infrastructure/Nextcloud`. Rendern (Kontakt → vCard, Task → VTODO) und Abgleichlogik liegen in `Application/Features/Nextcloud`, damit sie ohne Server testbar sind. Die Implementierung verwendet nur Standard-DAV, deshalb funktionieren andere CardDAV/CalDAV-Server voraussichtlich auch, getestet wird aber nur Nextcloud.
4. **Umfang:**
   - Kontakte: alle nicht archivierten. Ein Kontakt wird remote gelöscht, wenn er archiviert oder gelöscht wird.
   - Tasks: alle offenen (auch ohne Fälligkeit) und die in den letzten 30 Tagen erledigten. Ältere erledigte und gelöschte Tasks werden remote gelöscht.
5. **Feldzuordnung:**
   - **vCard 3.0** (am breitesten unterstützt: iOS/macOS, DAVx5, Nextcloud): `UID`, `N`/`FN`, `EMAIL`, `TEL`, `ORG` (Name der Organisation), `TITLE`, `URL` (LinkedIn), `ADR` (Anschrift aus `Address`, Land aus `CountryCode`).
   - **VTODO:** `UID`, `SUMMARY`, `DUE` (ganztägig, `VALUE=DATE`), `STATUS` (`NEEDS-ACTION`/`COMPLETED`), `COMPLETED`, `URL`/`DESCRIPTION` mit Link zum Bezugsobjekt (Anfrage vor Kontakt vor Organisation, wie im ICS-Feed).
   - `UID` = `<Id>@solocrm`, Ressourcenname `<Id>.vcf` bzw. `<Id>.ics`.
6. **Sammlungen legt SoloCRM selbst an.** Discovery folgt dem Standard: `current-user-principal` → `addressbook-home-set` bzw. `calendar-home-set`. Darunter entstehen ein Adressbuch und eine Aufgabenliste (nur `VTODO`), beide mit dem Anzeigenamen „SoloCRM“. Der Href wird gespeichert; gibt es die Sammlung schon, etwa nach erneutem Verbinden, wird sie wiederverwendet. Fremde Einträge in diesen Sammlungen (`UID` ohne `@solocrm`) bleiben unangetastet.
7. **Zuordnungstabelle `nextcloud_sync_items`:** Entitätstyp, Entitäts-Id, Remote-Href, ETag und `ContentHash`. Sie hat **keinen Fremdschlüssel** auf Kontakt bzw. Task. So bleibt die Zeile nach einem harten Löschen erhalten, der nächste Abgleich löscht die Kopie in Nextcloud und entfernt erst danach die Zeile.
8. **Änderungserkennung ausgehend per Hash, nicht per `UpdatedAt`:** Jede Runde rendert alle Datensätze im Umfang und vergleicht den SHA-256 der Payload mit `ContentHash`. Gepusht wird nur bei Abweichung. Damit werden auch abgeleitete Änderungen erfasst, die `UpdatedAt` des Kontakts nicht berühren, z. B. eine umbenannte Organisation (`ORG`). Voraussetzung: Das Rendern ist deterministisch, also ohne Zeitstempel des Renderns (`DTSTAMP` = `UpdatedAt` des Datensatzes, kein `REV`).
9. **Rückkanal (nur Tasks) per `sync-token`:** Jede Runde läuft **zuerst eingehend**, dann ausgehend.
   - Eingehend werden die geänderten Hrefs über `REPORT sync-collection` mit dem gespeicherten Token geholt. Stimmt der ETag mit dem gespeicherten überein, war es der eigene `PUT`, und der Eintrag wird übersprungen.
   - Sonst wird `STATUS` gelesen: Ist er `COMPLETED` und der Task lokal offen, wird `CompleteTask` aufgerufen; im umgekehrten Fall `ReopenTask`. Beides läuft mit Audit und Domain Events. Alle übrigen Felder überschreibt die anschließende Ausgangsphase.
   - Wird der Token abgelehnt (`valid-sync-token`), fällt der Abgleich auf ein volles `PROPFIND` (Depth 1) mit ETag-Vergleich zurück.
   - Ein in Nextcloud gelöschter Eintrag wird beim nächsten Push neu angelegt (Master).
10. **Schreibkonflikte:** `PUT` geht mit `If-Match` auf den gespeicherten ETag (Neuanlage: `If-None-Match: *`).
    - Bei `412` wird ein Kontakt ohne Bedingung überschrieben (Master).
    - Ein Task wird bei `412` übersprungen, weil eine noch nicht verarbeitete Remote-Änderung vorliegt, etwa ein gerade abgehakter Task. Die nächste Eingangsphase verarbeitet sie.
    - Liefert der Server beim `PUT` keinen ETag, wird er per `PROPFIND` nachgeholt.
11. **Zugang:**
    - Server-URL, Benutzername und Nextcloud-App-Passwort werden in den Einstellungen eingetragen. Die Verbindung wird beim Speichern geprüft (Discovery).
    - Gespeichert wird in `nextcloud_connections`, das App-Passwort per Data Protection verschlüsselt, dazu Sammlungs-Hrefs, Sync-Token und Status des letzten Abgleichs.
    - Die URL muss `https` sein. Anmeldedaten werden nur an diesen Origin geschickt; Weiterleitungen auf andere Origins werden nicht verfolgt.
    - **Trennen** löscht Verbindung und Zuordnung. Die Sammlungen in Nextcloud bleiben bestehen; die Einstellungen weisen darauf hin.
12. **Betrieb:**
    - Ein periodischer `BackgroundService` (Intervall konfigurierbar, Default 15 Minuten) gleicht ab, dazu gibt es den Button „Jetzt abgleichen“. Ein Prozess-Gate verhindert parallele Läufe (wie bei Mail).
    - Bei `401` wird die Verbindung als „Zugang ungültig“ markiert und pausiert, bis neue Zugangsdaten gespeichert werden. Netzwerkfehler werden in der nächsten Runde wiederholt.
    - Logs enthalten nur Zähler, Status, HTTP-Codes und Ids, nie Namen, Adressen oder Inhalte.

## Betrachtete Optionen
### DAV-Zugriff
#### Option A: `HttpClient` mit eigenen DAV-Requests (gewählt)
**Pro:** Kein Paket; volle Kontrolle über `sync-collection`, `MKCALENDAR`, Precondition-Header; gleiches Muster wie ADR-013. **Contra:** XML-Parsing der Multistatus-Antworten (`207`) selbst schreiben und testen.
#### Option B: `WebDav.Client` (skazantsev, MIT, verbreitet)
**Pro:** Ausgereift für generisches WebDAV (`PROPFIND`, `PUT`, `DELETE`, `MKCOL`). **Contra:** Kein `REPORT`/`sync-collection`, kein `MKCALENDAR`, kein MKCOL mit Ressourcentyp. Der CalDAV/CardDAV-spezifische Teil müsste trotzdem über `HttpClient` laufen, das Paket nähme nur die einfachen Fälle ab und brächte eine zweite Abstraktion.
#### Option C: Fertige CardDAV/CalDAV-Clients (z. B. `BrandUp.CardDav.Client`)
**Pro:** Protokoll fertig. **Contra:** Kaum verbreitet (wenige tausend Downloads), nicht erkennbar gepflegt, meist nur eines der beiden Protokolle.

### vCard
#### Option A: `FolkerKinzel.VCards` (gewählt)
**Pro:** Aktiv gepflegt (8.2.0, Juli 2026), MIT, zielt auf net10.0, schreibt vCard 2.1/3.0/4.0 inkl. Escaping und Zeilenfaltung. **Contra:** Zusätzliche Abhängigkeit; umfangreiche API für einen kleinen Feldumfang.
#### Option B: `MixERP.Net.VCards`, `vCard.Net`
**Pro:** Verbreitet. **Contra:** MixERP seit 2017 ohne Release; `vCard.Net` seit 2024 ohne Release, nur netstandard.
#### Option C: Eigener vCard-3.0-Writer
**Pro:** Keine Abhängigkeit, ca. 100 Zeilen. **Contra:** Fehleranfällige Details (Escaping, Faltung nach 75 Oktetten ohne UTF-8-Zeichen zu zerteilen, `ADR`-Struktur), die das Paket bereits richtig löst.

### Änderungserkennung ausgehend
#### Option A: Hash der gerenderten Payload (gewählt)
**Pro:** Erfasst jede sichtbare Änderung, auch abgeleitete (`ORG`, Bezugslink); robust gegen Uhren und verpasste Läufe; einfach. **Contra:** Rendert jede Runde den gesamten Umfang (bei Single-User-Datenmengen vernachlässigbar).
#### Option B: `UpdatedAt` > Zeitpunkt des letzten Pushs
**Pro:** Rendert nur Geändertes. **Contra:** Übersieht abgeleitete Änderungen; braucht Sonderfälle für Umfangswechsel (Archivieren, 30-Tage-Grenze bei Tasks).
#### Option C: Domain Events aus der Outbox
**Pro:** Nahezu sofort. **Contra:** Nicht jede relevante Änderung hat ein Event (z. B. Organisationsname); koppelt den Sync an die Webhook-Infrastruktur. Als späterer Auslöser für einen vorgezogenen Lauf denkbar.

## Konsequenzen
- Leichter: Kontakte und Aufgaben sind auf jedem DAV-fähigen Gerät verfügbar, ohne Nextcloud-spezifische APIs. Abhaken auf dem Handy wirkt in SoloCRM inkl. Audit und Webhooks.
- Schwerer: Personenbezogene Daten liegen zusätzlich in Nextcloud. Die DSGVO-Löschung (US-20) erreicht die Kopie erst mit dem nächsten Abgleich, nach dem Trennen gar nicht mehr. `docs/datenschutz.md` muss Nextcloud als Speicherort und Empfänger aufnehmen.
- Schwerer: Multistatus-XML, Sync-Token-Fallback und ETag-Fälle müssen mit Integrationstests abgesichert werden, am besten gegen einen echten Nextcloud- oder Radicale-Container (Testcontainers), nicht nur gegen Mocks.
- Bei Umsetzung in die Spec nachziehen: `NextcloudConnection` und `NextcloudSyncItem` (2.4), Einstellungsabschnitt „Nextcloud“ (S6).
- Später prüfen: Vorgezogener Lauf nach relevanten Domain Events; Zwei-Wege-Sync für Kontakte; Löschen der Sammlungen beim Trennen als Option.
