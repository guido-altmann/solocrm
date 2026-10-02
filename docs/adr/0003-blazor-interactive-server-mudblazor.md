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

## Erfahrungen (MVP, 2026-10-02)
- Interactive Server hat sich für den Single-User bewährt: Komponenten rufen die Handler direkt auf, `IDbContextFactory` pro Handler-Aufruf hat Circuit-Probleme verhindert.
- MudBlazor deckt alle Oberflächen ab; Inline-Editing, Command Palette und Drag & Drop entstanden mit eigenen Komponenten auf MudBlazor-Basis. Für die statisch gerenderten Kontoseiten (Identity) funktionieren nur die rein darstellenden MudBlazor-Komponenten; Eingaben sind dort native Felder im App-Stil (ADR-004, Umsetzung Iteration 6).
- Rolling Updates erfordern den WebSocket-only-Start (`wwwroot/js/blazor-start.js`, ADR-009).

