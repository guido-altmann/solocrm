# ADR-003: Blazor Interactive Server mit MudBlazor

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Ein Nutzer, Betrieb auf eigenem Server, Fokus auf Produktivität und einfacher Bedienung.

## Entscheidung
Blazor Web App mit globalem Render-Modus **Interactive Server** (Identity-Seiten statisch per SSR). UI-Bibliothek: MudBlazor.

## Betrachtete Optionen
### A: Interactive Server (gewählt)
**Pro:** direkter Zugriff auf Handler ohne API-Umweg, schneller Start, kleiner Client. **Contra:** braucht eine stabile SignalR-Verbindung; Circuit-Lifetimes beachten.
### B: WebAssembly / Auto
**Pro:** offlinefähiger, entlastet den Server. **Contra:** doppelte API-Schicht für die UI, größerer Download, mehr Komplexität ohne Nutzen für einen Single-User.

## Konsequenzen
- Leichter: UI-Entwicklung, Debugging.
- Schwerer: DbContext-Lifetime → `IDbContextFactory` Pflicht; WebSockets hinter dem Proxy nötig.
- Später prüfen: Auto-Modus, falls mobile Nutzung über schwache Verbindungen relevant wird.
