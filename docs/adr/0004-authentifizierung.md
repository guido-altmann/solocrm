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
- **Rate-Limit vor der Authentifizierung:** 60 Anfragen pro Minute, partitioniert nach dem Key-Präfix (ohne gültiges Format: nach Client-IP). Dafür stehen `UseAuthentication`/`UseAuthorization` explizit hinter `UseRateLimiter`. So kosten Anfragen über dem Limit keinen DB-Zugriff.
- **Ein Handler für die Prüfung:** Der Authentication-Handler ruft den Use Case `AuthenticateApiKey` auf, dieselbe Schichtung wie überall (kein DbContext in `Web`). Ein Architekturtest sichert das für `Web/Endpoints` ab.
- Fehlende, ungültige und widerrufene Keys erhalten dieselbe Antwort (`401` mit Problem Details und `WWW-Authenticate: ApiKey header="X-Api-Key"`). Keys erscheinen nie im Log.

## Umsetzung (Iteration 6, 2026-10-02)
- **2FA per TOTP** über die Identity-Bordmittel (`AuthenticatorTokenProvider`, Wiederherstellungscodes). Die Einrichtung zeigt den QR-Code serverseitig als SVG (Paket `QRCoder`, ohne JavaScript) und den Schlüssel als Text; Aussteller in der App ist `SoloCRM`. Das Login-Rate-Limit (5 Versuche pro Minute und IP) gilt gemeinsam für Passwort, Authenticator-Code und Wiederherstellungscode.
- **Passkeys** (WebAuthn, Identity-Schema Version 3) bleiben als Alternative zum Passwort erhalten.
- **Entfernte Template-Seiten:** E-Mail ändern (die Adresse kommt aus `Admin__Email`), „Personal Data“ herunterladen/löschen (würde den einzigen Admin löschen), Passwort setzen sowie alle Seiten, die auf E-Mail-Versand angewiesen sind (Passwort vergessen/zurücksetzen, E-Mail bestätigen). Es gibt keinen E-Mail-Versand; die Seiten hätten nie funktioniert. Wiederherstellung bei vergessenem Passwort oder verlorener 2FA: über die Datenbank (`deploy/coolify.md`, Fehlersuche).
- **Kontoseiten** bleiben statisch gerendert (SSR), weil Anmelden und Passwortänderung das Cookie über den `HttpContext` schreiben. Sie nutzen das App-Theme und die MudBlazor-Komponenten, die reines HTML erzeugen (Layout, Buttons, Hinweise); Eingabefelder sind native Elemente im Stil der App, weil MudBlazor-Eingaben Interaktivität voraussetzen. Bootstrap ist entfernt. Texte und Identity-Fehlermeldungen sind deutsch (`GermanIdentityErrorDescriber`).

## Betrachtete Optionen
### A: Identity + API-Key (gewählt)
**Pro:** keine externe Abhängigkeit, lehrreich (eigener Auth-Handler). **Contra:** kein SSO.
### B: Externer OIDC-Provider (Keycloak, Entra ID)
**Pro:** SSO, zentrale Verwaltung. **Contra:** zusätzlicher Dienst, Overkill für einen Nutzer.

## Konsequenzen
- Später prüfen: OIDC, falls weitere Nutzer dazukommen.
