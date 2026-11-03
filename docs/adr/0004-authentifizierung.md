# ADR-004: ASP.NET Core Identity + API-Keys

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Genau ein Nutzer. Die UI braucht Login (optional 2FA), die API braucht maschinellen Zugriff (n8n).

## Entscheidung
- UI: ASP.NET Core Identity (Cookie) mit einem per Seed angelegten Admin-User aus Umgebungsvariablen; Registrierung deaktiviert; TOTP-2FA optional.
- API: eigener Authentication-Handler für `X-Api-Key`; Keys gehasht (SHA-256) gespeichert, Präfix zur Identifikation.

## Umsetzung (Iteration 5, 2026-10-01)
- **Key-Format** `scrm_<prefix>_<secret>`: 8 zufällige Zeichen als Präfix (im Klartext gespeichert, eindeutiger Index, Anzeige `scrm_ab12cd34_…`), 32 zufällige Bytes als Hex. Gespeichert wird SHA-256 über den ganzen Key; wegen der hohen Entropie ist kein langsamer Passwort-Hash nötig. Der Vergleich läuft in konstanter Zeit (`CryptographicOperations.FixedTimeEquals`). Falsch geformte Keys werden ohne DB-Zugriff abgelehnt; `LastUsedAt` wird höchstens minütlich geschrieben.
- **Getrennte Schemata:** Die API-Policy akzeptiert nur das Schema `ApiKey`. Ein im Browser angemeldeter Nutzer kann die API also nicht mit seinem Cookie aufrufen (kein CSRF über die API). Umgekehrt sind OpenAPI-Dokument und Scalar nur mit Cookie-Login erreichbar (Fallback-Policy).
- **Rate-Limit vor der Authentifizierung:** 60 Anfragen pro Minute, partitioniert nach dem Key-Präfix (ohne gültiges Format: nach Client-IP); seit SOL-34 zusätzlich 300 pro Client-IP. Dafür stehen `UseAuthentication`/`UseAuthorization` explizit hinter `UseRateLimiter`. So kosten Anfragen über dem Limit keinen DB-Zugriff.
- **Ein Handler für die Prüfung:** Der Authentication-Handler ruft den Use Case `AuthenticateApiKey` auf, dieselbe Schichtung wie überall (kein DbContext in `Web`). Ein Architekturtest sichert das für `Web/Endpoints` ab.
- Fehlende, ungültige und widerrufene Keys erhalten dieselbe Antwort (`401` mit Problem Details und `WWW-Authenticate: ApiKey header="X-Api-Key"`). Keys erscheinen nie im Log.

## Umsetzung (Iteration 6, 2026-10-02)
- **2FA per TOTP** über die Identity-Bordmittel (`AuthenticatorTokenProvider`, Wiederherstellungscodes). Die Einrichtung zeigt den QR-Code serverseitig als SVG (Paket `QRCoder`, ohne JavaScript) und den Schlüssel als Text; Aussteller in der App ist `SoloCRM`. Das Login-Rate-Limit (5 Versuche pro Minute und IP) gilt gemeinsam für Passwort, Authenticator-Code und Wiederherstellungscode.
- **Passkeys** (WebAuthn, Identity-Schema Version 3) bleiben als Alternative zum Passwort erhalten.
- **Entfernte Template-Seiten:** E-Mail ändern (die Adresse kommt aus `Admin__Email`), „Personal Data“ herunterladen/löschen (würde den einzigen Admin löschen), Passwort setzen sowie alle Seiten, die auf E-Mail-Versand angewiesen sind (Passwort vergessen/zurücksetzen, E-Mail bestätigen). Es gibt keinen E-Mail-Versand; die Seiten hätten nie funktioniert. Wiederherstellung bei vergessenem Passwort oder verlorener 2FA: über die Datenbank (`deploy/coolify.md`, Fehlersuche).
- **Kontoseiten** bleiben statisch gerendert (SSR), weil Anmelden und Passwortänderung das Cookie über den `HttpContext` schreiben. Sie nutzen das App-Theme und die MudBlazor-Komponenten, die reines HTML erzeugen (Layout, Buttons, Hinweise); Eingabefelder sind native Elemente im Stil der App, weil MudBlazor-Eingaben Interaktivität voraussetzen. Bootstrap ist entfernt. Texte und Identity-Fehlermeldungen sind deutsch (`GermanIdentityErrorDescriber`).

## Härtung nach externem Audit (SOL-30, 2026-10-26)
- **Kontosperre:** nach 10 Fehlversuchen für 15 Minuten. Bewusst kurz, weil sich nur der einzige Admin sperren lässt und Entsperren sonst nur per DB geht (`deploy/coolify.md`, Fehlersuche). Passwort, Authenticator-Code und Wiederherstellungscode zählen gemeinsam. Identity prüft und zählt die Sperre bei Wiederherstellungscodes nicht selbst; das übernimmt `LoginWithRecoveryCode`. Die Sperre ergänzt das Rate-Limit pro IP gegen verteilte Versuche.
- **Passwortregeln:** mindestens 12 Zeichen und 4 verschiedene Zeichen, keine Pflicht-Zeichenklassen (NIST SP 800-63B). Gilt für neue Passwörter; das bestehende bleibt gültig.
- **Cookies:** Alle Identity-Cookies explizit `HttpOnly` und `SameSite=Lax`, außerhalb von Development immer `Secure`; in Development folgt `Secure` dem Schema, weil lokal und in den Integrationstests `http` läuft. Anmelde-Cookie 14 Tage, gleitend.
- **Rate-Limit auch für `PasskeyRequestOptions`:** Der anonyme Endpunkt ist pro IP auf 20 Anfragen pro Minute begrenzt, mit eigenem Budget (SOL-37). Ursprünglich teilte er das Login-Budget; da die Login-Seite die Optionen bei jedem Laden für das Passkey-Autofill abruft, landete man so schon nach zwei Fehleingaben bei 429.
- **Security-Events** (Warning): `Failed sign-in attempt.`, `User account locked out.`, `Sign-in rate limit exceeded`, ohne E-Mail-Adresse und ohne Client-IP (Logs enthalten nur Ids, `docs/datenschutz.md`).

## Backlog-Härtung nach externem Audit (SOL-34, 2026-11-02)
- **Erneute Passwortabfrage vor Faktoränderungen (F-03):** Passkeys verwalten, Authenticator-App einrichten oder zurücksetzen, 2FA deaktivieren und Wiederherstellungscodes erzeugen gehen nur, wenn das Passwort in den letzten 10 Minuten unter `/Account/Manage/ConfirmPassword` bestätigt wurde. Die Bestätigung ist ein eigenes Cookie (`HttpOnly`, `SameSite=Strict`, Pfad `/Account`, per Data Protection verschlüsselt, an die User-Id gebunden), das beim Abmelden gelöscht wird. Fehlversuche zählen zur Kontosperre, die Seite teilt das Login-Rate-Limit. Ein frischer Login zählt bewusst nicht als Bestätigung: Nach einem Passkey-Login kennt die Seite den Nutzer nicht, und das Ausstellungsdatum des Anmelde-Cookies wird bei gleitender Laufzeit und Stamp-Prüfung erneuert. Die Einrichtung der Authenticator-App ist mitgeschützt, weil sie den aktuellen Schlüssel anzeigt.
- **Nach jeder Faktoränderung** wird der Security-Stamp erneuert (andere Sitzungen werden bei der nächsten Prüfung abgemeldet), die eigene Sitzung per `RefreshSignInAsync` erhalten und das Security-Event `Sign-in factor changed: <Änderung>` (Warning, nur mit User-Id) geschrieben; Fehleingaben auf der Bestätigungsseite als `Failed password confirmation.`.
- **API-Limit zusätzlich pro Client-IP (F-04):** Ein globaler Limiter erlaubt 300 API-Anfragen pro Minute und IP; er läuft vor dem Limit pro Key-Präfix, beide müssen passen. Damit umgehen zufällige, wohlgeformte Präfixe das Limit nicht. Großzügig, weil mehrere Keys (n8n-Workflows) meist von derselben Adresse kommen. Die Login-Policies haben dafür eigene Ablehnungs-Handler; der globale gehört der API (Problem Details).

## Akzeptiertes Restrisiko nach externem Audit (SOL-35, 2026-11-03)
- **API-Keys ohne Ablaufdatum (F-06):** Ein Key gilt, bis er widerrufen wird; es gibt kein `ExpiresAt` und keine erzwungene Rotation. Bewusst so, weil es genau einen Client gibt (n8n) und ein abgelaufener Key dort nur Workflows stilllegt, ohne dass es jemand merkt. Ausgleich: Keys sind in den Einstellungen jederzeit widerrufbar und neu erzeugbar, `LastUsedAt` zeigt eine unerwartete Nutzung, der Key steht nur gehasht in der DB und nie im Log, und das Rate-Limit begrenzt den Schaden eines geleakten Keys. Neu prüfen, falls weitere Clients oder Nutzer dazukommen.

## Betrachtete Optionen
### A: Identity + API-Key (gewählt)
**Pro:** keine externe Abhängigkeit, lehrreich (eigener Auth-Handler). **Contra:** kein SSO.
### B: Externer OIDC-Provider (Keycloak, Entra ID)
**Pro:** SSO, zentrale Verwaltung. **Contra:** zusätzlicher Dienst, Overkill für einen Nutzer.

## Konsequenzen
- Später prüfen: OIDC, falls weitere Nutzer dazukommen.
