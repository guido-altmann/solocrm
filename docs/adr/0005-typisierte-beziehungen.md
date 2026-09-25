# ADR-005: Typisierte Beziehungen statt generischer Associations

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
HubSpot verknüpft beliebige Objekte generisch. Für ein Freelancer-CRM sind die Beziehungen bekannt und stabil (Endkunde, Vermittler, Ansprechpartner).

## Entscheidung
Beziehungen als explizite Fremdschlüssel (z. B. `ClientOrganizationId`, `AgencyOrganizationId`) und typisierte Join-Tabellen für Tags. Zusatzfelder als `ExtraFields` (jsonb).

## Betrachtete Optionen
### A: Typisierte FKs (gewählt)
**Pro:** referentielle Integrität, einfache LINQ-Abfragen, fachlich sprechend. **Contra:** neue Beziehungsart = Migration.
### B: Generische Association-Tabelle (SourceType/SourceId/TargetType/TargetId)
**Pro:** maximal flexibel. **Contra:** keine FK-Integrität, komplexe Abfragen, schwer zu verstehen.

## Konsequenzen
- Später prüfen: Custom Properties über eine `PropertyDefinition`-Tabelle + `ExtraFields`.
