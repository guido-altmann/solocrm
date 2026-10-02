# Datenschutz

Welche personenbezogenen Daten SoloCRM wo speichert, wie lange, und wie Auskunfts- und Löschwünsche (Art. 15 und 17 DSGVO) erfüllt werden. Grundlage: `docs/SPEC.md` Kap. 6 und US-19/US-20, ADR-006 (Audit), ADR-009 (Betrieb), ADR-010 (Outbox).

SoloCRM ist ein Single-User-System: Verantwortlicher und einziger Nutzer ist der Betreiber. Rechtsgrundlage für die Kontaktdaten ist in der Regel das berechtigte Interesse (Art. 6 Abs. 1 lit. f DSGVO) bzw. die Vertragsanbahnung (lit. b).

## Wo liegen welche Daten?

| Ort | Inhalt | Personenbezug | Aufbewahrung |
|---|---|---|---|
| Postgres: `contacts` | Stammdaten (Name, E-Mail, Telefon, Rolle, LinkedIn, Anschrift, Quelle, Zusatzfelder wie `HubSpotRecordId`) | ja | bis zur Löschung (Archivieren blendet nur aus) |
| Postgres: `activities`, `tasks` | Notizen, Anrufe, Meetings, E-Mails, Aufgaben mit Bezug zu Kontakt, Organisation oder Anfrage | ja (Freitext) | bis zur Löschung |
| Postgres: `opportunities` | Anfragen; `primary_contact_id` verweist auf den Ansprechpartner | nur über die Id | bis zur Löschung (Archivieren als Normalfall) |
| Postgres: `organizations` | Firmen (keine natürlichen Personen) | in der Regel nein | bis zur Löschung |
| Postgres: `contact_tags` | Tag-Zuordnungen | über den Kontakt | mit dem Kontakt |
| Postgres: `audit_entries` | Änderungsprotokoll mit alten und neuen Feldwerten (ADR-006) | ja | unbegrenzt; bei der DSGVO-Löschung anonymisiert (siehe unten) |
| Postgres: `outbox_messages` | Domain Events für Webhooks | nur Ids und Status, keine Inhalte | 30 Tage nach der Verarbeitung |
| Postgres: `webhook_deliveries` | Versandprotokoll (Status, Dauer, Fehlertext) | nein | 30 Tage |
| Postgres: `AspNetUsers`, `AspNetUserTokens`, Passkeys | Konto des Betreibers: E-Mail, Passwort-Hash, Authenticator-Schlüssel, Wiederherstellungscodes, Passkeys | Betreiber | solange das Konto besteht |
| Postgres-Backups (S3) | tägliche `pg_dump`-Sicherung der gesamten Datenbank | ja | 30 Tage, danach automatisch gelöscht |
| Volume `/app/keys` und dessen Sicherung | Data-Protection-Schlüssel (Cookies, verschlüsselte Webhook-Secrets) | nein | dauerhaft; Sicherung verschlüsselt (`deploy/coolify.md` Abschnitt 8) |
| Container-Logs (stdout, Coolify) | strukturierte Logs mit Request-Pfaden und Ids | nur Ids (z. B. `/contacts/{id}`), keine Bodies, keine E-Mail-Adressen | bis der Container ersetzt wird (Deploy) bzw. nach den Docker-Log-Einstellungen des Servers |
| Browser | Login-Cookie, ggf. „Diesem Browser vertrauen“ (2FA) | Betreiber | bis zur Abmeldung bzw. zum Ablauf |

Die DSGVO-Auskunft wird direkt im Browser erzeugt und heruntergeladen; auf dem Server wird keine Datei abgelegt.

Externe Empfänger: nur die selbst konfigurierten Webhook-Ziele (z. B. n8n). Sie erhalten ausschließlich Ids und Status (SPEC 5); Details holt n8n bei Bedarf über die REST-API.

## Auskunft (Art. 15, US-19)

1. Kontakt öffnen (auch archivierte Kontakte, Filter „Archivierte anzeigen“), Menü **⋮ → Daten exportieren (DSGVO)**.
2. Der Browser lädt `kontakt-<name>-<datum>.json` herunter. Alternativ per API: `GET /api/v1/contacts/{id}/export` (z. B. aus n8n).
3. Datei prüfen und der betroffenen Person auf sicherem Weg übermitteln.

Inhalt (Format `formatVersion: 1`, camelCase, Zeitstempel in UTC als ISO 8601 mit `Z`, Datumswerte als `JJJJ-MM-TT`, Aufzählungen als Namen):

| Abschnitt | Inhalt |
|---|---|
| `formatVersion`, `exportedAt` | Formatversion und Zeitpunkt des Exports |
| `contact` | Stammdaten inkl. Anschrift, Quelle, Zusatzfelder, Organisation (Id, Name, Typ), Tags, `createdAt`/`updatedAt` |
| `activities` | alle Activities mit direktem Bezug zum Kontakt (Typ, Zeitpunkt, Betreff, Text, verknüpfte Organisation/Anfrage) |
| `tasks` | alle Aufgaben mit direktem Bezug (Titel, Fälligkeit, erledigt am) |
| `opportunities` | Anfragen mit dem Kontakt als Ansprechpartner: Titel, Phase, Status, Rolle (`primaryContact`) |
| `auditEntries` | Änderungsprotokoll des Kontakts sowie seiner Activities und Tasks, auch bereits gelöschter |

Activities einer Anfrage ohne direkten Bezug zum Kontakt gehören nicht dazu (`docs/iterations/06-dsgvo-und-politur.md`, Entscheidung 1; die folgenden Entscheidungen beziehen sich auf dieselbe Datei).

## Löschung (Art. 17, US-20)

1. Kontakt öffnen, Menü **⋮ → Endgültig löschen (DSGVO)**.
2. Der Dialog nennt die Folgen (Anzahl Activities und Aufgaben, betroffene Anfragen). Zur Bestätigung den Namen des Kontakts eingeben.
3. In einer Transaktion werden gelöscht bzw. geändert:
   - der Kontakt mit Tag-Zuordnungen,
   - alle Activities und Aufgaben mit direktem Bezug, auch wenn sie zusätzlich auf eine Organisation oder Anfrage verweisen (Entscheidung 2),
   - bei Anfragen mit dem Kontakt als Ansprechpartner wird der Bezug geleert; die Anfrage bleibt,
   - im Änderungsprotokoll werden alle Einträge des Kontakts und seiner Activities und Aufgaben (auch früher gelöschter) **anonymisiert**: die Feldwerte werden entfernt, Objekttyp, Id, Aktion und Zeitpunkt bleiben. Für den Kontakt wird ein Eintrag `Deleted` ohne Feldwerte als Nachweis geschrieben (Entscheidung 3),
   - das Ereignis `contact.deleted` (nur die Id) wird in die Outbox geschrieben.
4. Einträge anderer Datensätze, die nur die Id des Kontakts enthalten (z. B. `PrimaryContactId` im Protokoll einer Anfrage), bleiben; ohne Datensatz ist die Id nicht mehr personenbeziehbar.

Nicht automatisch gelöscht wird:

- **Backups:** Die Person bleibt bis zu 30 Tage in den täglichen Postgres-Backups enthalten und verschwindet mit deren Ablauf. Bei einem Restore innerhalb dieser Frist ist die Löschung erneut auszuführen (Datum der Löschung notieren).
- **Quellsysteme:** Ist die Person auch in HubSpot oder in n8n-Workflows gespeichert, dort ebenfalls löschen. Ein Webhook mit dem Ereignis `contact.deleted` kann das in n8n anstoßen. Ein späterer HubSpot-Import würde den Kontakt sonst wieder anlegen; eine Sperrliste gibt es bewusst nicht, weil sie selbst ein personenbezogenes Datum wäre (Entscheidung 4).
- **Logs:** enthalten nur Ids; der Löschvorgang wird mit Id und Anzahl der gelöschten Activities und Aufgaben protokolliert.

Die Löschung ist bewusst nur in der Oberfläche möglich, nicht über die REST-API (Entscheidung 5).

## Weitere Maßnahmen

- Hosting in der EU, keine externen Tracker, keine externen Schriftarten oder CDNs.
- Login mit optionaler Zwei-Faktor-Anmeldung (Authenticator-App) und Passkeys; Rate-Limit für Login, 2FA und Wiederherstellungscodes.
- API-Keys werden nur als Hash, Webhook-Secrets nur verschlüsselt gespeichert (ADR-010).
