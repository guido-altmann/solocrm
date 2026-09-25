# ADR-004: ASP.NET Core Identity + API-Keys

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Genau ein Nutzer. Die UI braucht Login (optional 2FA), die API braucht maschinellen Zugriff (n8n).

## Entscheidung
- UI: ASP.NET Core Identity (Cookie) mit einem per Seed angelegten Admin-User aus Umgebungsvariablen; Registrierung deaktiviert; TOTP-2FA optional.
- API: eigener Authentication-Handler für `X-Api-Key`; Keys gehasht (SHA-256) gespeichert, Präfix zur Identifikation.

## Betrachtete Optionen
### A: Identity + API-Key (gewählt)
**Pro:** keine externe Abhängigkeit, lehrreich (eigener Auth-Handler). **Contra:** kein SSO.
### B: Externer OIDC-Provider (Keycloak, Entra ID)
**Pro:** SSO, zentrale Verwaltung. **Contra:** zusätzlicher Dienst, Overkill für einen Nutzer.

## Konsequenzen
- Später prüfen: OIDC, falls weitere Nutzer dazukommen.
