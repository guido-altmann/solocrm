# ADR-013: E-Mail-Logging über Microsoft Graph (hinter `IMailSource`)

**Status:** Accepted
**Datum:** 2026-10-14
**Entscheider:** Guido Altmann

## Kontext
E-Mails mit bekannten Kontakten sollen automatisch als Activity (`ActivityType.Email`) in der Timeline erscheinen (SOL-13). Das Postfach liegt bei Microsoft 365. Gelesen werden Posteingang und Gesendet, mit vollem Text. SoloCRM ist Single-User, hat einen `BackgroundService` für Hintergrundjobs (ADR-008) und verschlüsselt Zugangsdaten per Data Protection (ADR-004, ADR-010). Anbieter wie Gmail oder ein IMAP-Server sollen später ergänzbar sein.

Randbedingungen: Mail-Inhalte sind personenbezogen und gehören nicht in Logs. Auskunft und Löschung (US-19/US-20) müssen die geloggten Mails miterfassen. Der Activity-Body ist Markdown ohne Roh-HTML.

## Entscheidung
1. **Microsoft Graph (REST über `HttpClient`)** als erste Quelle, **nur lesend** (`Mail.Read`, `offline_access`). Kein zusätzliches NuGet-Paket; Token-Flow und Delta-Abfragen sind überschaubar. (Regel 10: ein Paket wie `Microsoft.Graph` oder `Azure.Identity` nur nach Rückfrage.)
2. **Abstraktion `IMailSource`** in `Application`, Implementierung `GraphMailSource` in `Infrastructure`. Die Abstraktion liefert normalisierte Nachrichten (`ExternalId`, `InternetMessageId`, Richtung, Absender, Empfänger, Betreff, Zeitpunkt, Text) und einen Fortsetzungs-Token je Ordner. Weitere Quellen (IMAP, Gmail) sind später reine Zusatz-Implementierungen.
3. **Änderungserkennung per Delta-Query** (`/me/mailFolders/{inbox|sentitems}/messages/delta`) mit gespeichertem `deltaLink` je Ordner. Der Abgleich läuft im vorhandenen Hintergrunddienst-Muster (periodisch, konfigurierbar), nicht per Graph-Webhook (die App ist nicht zwingend öffentlich erreichbar, Single-Instance).
4. **Zuordnung nur über bekannte Adressen:** Eine Mail wird nur dann als Activity angelegt, wenn Absender oder ein Empfänger (To/Cc) per E-Mail-Adresse (case-insensitive) einem nicht archivierten Kontakt entspricht. Alle anderen Mails werden nicht gespeichert, auch nicht als Metadaten.
5. **Idempotenz:** Eine Tabelle `mail_messages` (Graph-Id, `InternetMessageId`, ActivityId, Ordner) verhindert Dubletten, z. B. wenn eine Mail in beiden Ordnern auftaucht oder erneut geliefert wird.
6. **Voller Text, als Klartext:** HTML-Bodies werden serverseitig in Klartext gewandelt (kein Roh-HTML im Activity-Body, passend zur Markdown-Regel); Anhänge werden nicht übernommen.
7. **Zugang:** Delegierte Anmeldung (Authorization Code mit PKCE) über eine Entra-App-Registrierung des Nutzers (Plattform „Web“, vertraulicher Client), ausgelöst in den Einstellungen. Das Refresh-Token wird per Data Protection verschlüsselt gespeichert. Client-Id, Tenant und Client-Secret kommen aus Umgebungsvariablen (`Mail__Graph__*`), nie ins Repo. Die Einrichtung beschreibt `docs/email-logging-einrichtung.md`.
8. **Startzeitpunkt:** Beim ersten Abgleich werden die Mails der letzten **30 Tage** nachgeladen (`Mail__BackfillDays`, Default 30; `0` = nur Mails ab Aktivierung). Danach läuft der Delta-Abgleich.
9. **Mehrere bekannte Kontakte in einer Mail:** Es entsteht **eine** Activity mit einem Hauptbezug: bei eingehenden Mails der Absender, bei ausgehenden der erste bekannte Empfänger (To vor Cc). Weitere bekannte Kontakte werden nicht verknüpft.
10. **Zitate und Signaturen:** Zitierte Vorgängermails und Signaturen werden beim Speichern entfernt (heuristisch: Klartext-Konvertierung, Abschneiden ab typischen Zitat-Markern wie „Von:“/„On … wrote:“/`>`-Zeilen). Ist die Heuristik unsicher, bleibt der Text vollständig erhalten, statt Inhalt zu verlieren.
11. **Logging:** In Logs stehen nur Zähler, Ordner, Status und technische Ids, nie Adressen, Betreff oder Text.

## Betrachtete Optionen
### Option A: Microsoft Graph (gewählt)
**Pro:** Von Microsoft für Exchange Online vorgesehen; Delta-Sync mit sauberem Token; Berechtigung auf `Mail.Read` begrenzbar; kein UID-/UIDVALIDITY-Handling. **Contra:** Nur Microsoft 365; Entra-App-Registrierung nötig; Token-Handling selbst zu bauen.

### Option B: IMAP
**Pro:** Anbieterneutral. **Contra:** Exchange Online verlangt auch hier OAuth2 (XOAUTH2), also dieselbe App-Registrierung; zusätzliches Paket (z. B. MailKit); UID-/UIDVALIDITY-Verwaltung selbst; schwächere Änderungserkennung.

### Option C: Graph-Webhooks statt Polling
**Pro:** Nahezu Echtzeit. **Contra:** Öffentlicher HTTPS-Endpunkt, Subscription-Verlängerung, zusätzliche Fehlerfälle. Für einen Freelancer-Tagesablauf genügt Polling im Minutentakt.

### Zugang: App-only (Client Credentials) statt delegiert
**Pro:** Kein Refresh-Token, das ablaufen kann. **Contra:** Zugriff gilt ohne zusätzliche Einschränkung (RBAC for Applications) für alle Postfächer des Tenants. Für ein einzelnes persönliches Postfach ist delegiert die kleinere Rechtefläche. Bei Bedarf später prüfbar.

## Konsequenzen
- Leichter: Mails erscheinen ohne manuelle Erfassung in allen Timelines nach den Regeln aus SPEC 2.5; weitere Mail-Quellen sind Zusatz statt Umbau.
- Schwerer: Der volle Mail-Text liegt in der Datenbank. Damit wächst die Menge personenbezogener Daten, auch von Dritten in Cc.
- **Fachliche Folgen für die Spec (bei Umsetzung nachziehen):**
  - Activity braucht eine **Richtung** (eingehend/ausgehend) und die **Herkunft** (z. B. `Source = Graph`, Message-Id), damit die Timeline beides darstellen kann und die Auskunft die Herkunft nennt.
  - Der Eintrag in `docs/datenschutz.md` ergänzen: Mails als Dritterhebung (Art. 14), Auskunft und Löschung erfassen sie über die Activities des Kontakts; `mail_messages` muss kaskadierend mitgelöscht werden.
  - Prüfen, ob der `AuditInterceptor` den Body in `AuditEntry.Changes` schreibt (ADR-006); Mail-Texte dürfen dort nicht doppelt landen.
- Das Entfernen von Zitaten ist verlustbehaftet: Die Heuristik braucht Tests mit echten Mustern (Outlook deutsch/englisch, Apple Mail, Gmail) und darf im Zweifel nichts abschneiden.
- Später prüfen: Umstieg auf App-only oder Webhooks; Mails an mehrere bekannte Kontakte je Kontakt abbilden, falls der Hauptbezug im Alltag nicht reicht.

## Umsetzung (SOL-13, 2026-10-14)
- **Aufteilung:** `IMailSource`, `IncomingMail` und `IMailConnector` liegen in `Application/Abstractions`; `SyncMail`, `GetMailStatus`, `ConnectMail`, `DisconnectMail` und `MailTextCleaner` in `Application/Features/Mail`; `GraphMailSource`, `GraphAuthenticator` und `MailSyncBackgroundService` in `Infrastructure/Mail`. Ohne `Mail__Graph__*` ist eine Quelle registriert, die „kein Postfach verbunden“ meldet, weil der App-Start alle Handler validiert.
- **Zugang gespeichert in `mail_connections`** (nicht in `app_settings`): Provider, Konto, verschlüsseltes Refresh-Token, Zeitpunkt des Verbindens, Status des letzten Abgleichs. Der Zugriffstoken liegt nur im Speicher.
- **Anmeldung:** `/mail/connect` und `/mail/callback` (nur angemeldet). State und PKCE-Verifier liegen verschlüsselt (Data Protection) in einem HttpOnly-Cookie (Pfad `/mail`, 10 Minuten); die Rückkehr-Adresse stammt aus `App__BaseUrl`. Der Ausgang wird per Code (`connected`, `denied`, `expired`, `failed`) an die Einstellungen übergeben, nie als Freitext.
- **Fortsetzungs-Links** werden nur auf `https://graph.microsoft.com` verfolgt, damit der Zugriffstoken nie an eine fremde Adresse geht.
- **Ein Abgleich zugleich:** Ein Prozess-Gate verhindert, dass Hintergrunddienst und „Jetzt abgleichen“ dieselben Mails doppelt importieren.
- **Webhooks:** Importierte Mails lösen `ActivityLogged` nur aus, wenn `Mail__RaiseActivityLoggedEvents` gesetzt ist (Default aus).
- **Importierte Mails** sind nicht bearbeitbar (`Activity.ImportedNotEditable`); Löschen ist möglich. Da Graph auch geänderte Mails liefert (z. B. gelesen markiert), kann eine gelöschte Mail wieder auftauchen. Gegenmaßnahme bei Bedarf: Löschvermerke je Message-ID.
- **Offen bis zum ersten Praxistest:** der echte Austausch mit Microsoft (Berechtigungen, Zustimmung, Umleitungs-URI).

