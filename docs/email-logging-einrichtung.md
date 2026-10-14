# E-Mail-Logging einrichten (Microsoft 365)

> **Stand: Entwurf zu ADR-013.** Die Funktion ist noch nicht umgesetzt. Namen der Umgebungsvariablen und die Rückkehr-Adresse werden bei der Umsetzung (SOL-13) bestätigt und diese Anleitung dann angepasst.

SoloCRM liest dein Microsoft-365-Postfach (Posteingang und Gesendet) **nur lesend** und legt zu bekannten Kontakten automatisch eine Activity an. Dazu brauchst du einmalig eine eigene App-Registrierung in Microsoft Entra ID. Es werden keine Daten an Dritte gesendet; die App spricht direkt mit Microsoft Graph.

## 1. App in Entra registrieren
1. Im [Entra Admin Center](https://entra.microsoft.com) → **Identität → Anwendungen → App-Registrierungen → Neue Registrierung**.
2. **Name:** z. B. „SoloCRM Mail“.
3. **Unterstützte Kontotypen:** „Nur Konten in diesem Organisationsverzeichnis“ (einzelner Mandant).
4. **Umleitungs-URI:** Plattform **Web**, Wert `https://<deine-domain>/mail/callback` (für lokale Tests zusätzlich `http://localhost:5000/mail/callback`; die Domain entspricht `App__BaseUrl`).
5. Registrieren. Auf der Übersicht stehen **Anwendungs-ID (Client)** und **Verzeichnis-ID (Mandant)**; beide brauchst du unten.

## 2. Berechtigungen
Unter **API-Berechtigungen → Berechtigung hinzufügen → Microsoft Graph → Delegierte Berechtigungen**:
- `Mail.Read`
- `offline_access`
- `User.Read` (Standard, für die Anzeige des verbundenen Kontos)

Mehr Berechtigungen vergibst du nicht. Es gibt keinen Schreibzugriff. Bei einem eigenen Mandanten kannst du über „Administratorzustimmung erteilen“ die Zustimmung gleich erledigen.

## 3. Client-Secret erzeugen
**Zertifikate & Geheimnisse → Neuer geheimer Clientschlüssel**. Den **Wert** sofort kopieren; er wird nur einmal angezeigt. Notiere dir das Ablaufdatum: Danach muss ein neuer Schlüssel erzeugt und in Coolify ausgetauscht werden.

## 4. Umgebungsvariablen setzen
In Coolify (bzw. lokal per `dotnet user-secrets`), niemals im Repo:

| Variable | Bedeutung | Default |
|---|---|---|
| `Mail__Graph__TenantId` | Verzeichnis-ID (Mandant) | – |
| `Mail__Graph__ClientId` | Anwendungs-ID (Client) | – |
| `Mail__Graph__ClientSecret` | Wert des Client-Secrets | – |
| `Mail__BackfillDays` | Beim ersten Abgleich nachgeladene Tage (`0` = nur ab Aktivierung) | `30` |
| `Mail__RaiseActivityLoggedEvents` | Ob importierte Mails das Event `activity.logged` (und damit Webhooks) auslösen. Aus, damit die Nachladung keine Flut an Webhook-Aufrufen verursacht | `false` |
| `Mail__PollingInterval` | Abstand der Abgleiche (mindestens 30 Sekunden) | `00:02:00` |
| `Mail__Enabled` | `false` schaltet den Hintergrund-Abgleich ab | `true` |

Ohne diese Variablen bleibt die Funktion abgeschaltet.

## 5. Postfach verbinden
1. In SoloCRM unter **Einstellungen → E-Mail** auf „Mit Microsoft verbinden“ klicken.
2. Bei Microsoft anmelden und die Berechtigungen bestätigen.
3. Nach der Rückkehr erscheint das verbundene Konto. Der erste Abgleich lädt die letzten `Mail__BackfillDays` Tage nach, danach laufen nur noch Änderungen.

Das Refresh-Token wird per Data Protection verschlüsselt gespeichert. Das Volume `/app/keys` muss deshalb erhalten bleiben (siehe `deploy/coolify.md`); geht es verloren, musst du das Postfach neu verbinden.

## Was passiert mit den Mails?
- Nur Mails, bei denen Absender oder ein Empfänger (An/Cc) die E-Mail-Adresse eines **nicht archivierten Kontakts** hat, werden gespeichert. Alles andere wird nicht übernommen.
- Gespeichert werden der Betreff, der Zeitpunkt und der Text als Klartext, **ohne zitierte Vorgängermails und Signatur und ohne Anhänge**.
- Pro Mail entsteht eine Activity vom Typ E-Mail mit dem Absender (eingehend) bzw. dem ersten bekannten Empfänger (ausgehend) als Hauptbezug.
- In Logs stehen nie Adressen, Betreff oder Text.
- Die Mails gehören zu den Daten des Kontakts: DSGVO-Auskunft und -Löschung (`docs/datenschutz.md`) erfassen sie.

## Trennen und Rotieren
- **Trennen:** Einstellungen → E-Mail → „Verbindung trennen“ löscht das gespeicherte Token. Bereits angelegte Activities bleiben bestehen. Zusätzlich kannst du in Entra unter Unternehmensanwendungen → SoloCRM Mail die Zustimmung widerrufen.
- **Secret erneuern:** neuen Schlüssel in Entra erzeugen, `Mail__Graph__ClientSecret` austauschen, neu deployen. Die Verbindung bleibt bestehen.

## Fehlersuche
| Symptom | Ursache |
|---|---|
| „Verbinden“ endet mit `AADSTS50011` | Umleitungs-URI in Entra stimmt nicht mit `App__BaseUrl` + `/mail/callback` überein |
| `AADSTS65001` / fehlende Zustimmung | Berechtigungen nicht bestätigt; Administratorzustimmung erteilen |
| Abgleich meldet „Token nicht lesbar“ | Data-Protection-Keys gewechselt: Postfach neu verbinden |
| Keine Mails erscheinen | Kein Kontakt mit passender E-Mail-Adresse, Kontakt archiviert oder Abgleich noch nicht gelaufen |
