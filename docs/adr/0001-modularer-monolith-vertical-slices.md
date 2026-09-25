# ADR-001: Modularer Monolith mit Vertical Slices

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Single-User-CRM, ein Entwickler, Betrieb als einzelner Container. Gleichzeitig ist es ein Referenzprojekt, deshalb soll die Struktur klar und erweiterbar sein.

## Entscheidung
Ein deploybarer Monolith mit vier Projekten (Domain, Application, Infrastructure, Web). Die Fachlogik ist in **Feature-Slices** organisiert (ein Use Case = eine Datei). Es gibt kein MediatR und keinen Repository-Layer.

## Betrachtete Optionen
### A: Modularer Monolith + Vertical Slices (gewählt)
**Pro:** geringe Komplexität, hohe Kohäsion je Feature, gut testbar, leicht zu erklären. **Contra:** Disziplin bei Schichtgrenzen nötig (Architekturtest).
### B: Klassische Clean Architecture mit Repositories/Services je Schicht
**Pro:** bekannt. **Contra:** viel Zeremonie, Logik eines Features über viele Dateien verstreut.
### C: Microservices
**Pro:** keiner für diesen Scope. **Contra:** massiver Betriebsaufwand.

## Konsequenzen
- Leichter: Features end-to-end verstehen und ändern.
- Schwerer: Nichts Wesentliches; Querschnitt über Interceptors lösen.
- Später prüfen: Aufteilung in Module mit eigenen DbContexts, falls der Scope stark wächst.
