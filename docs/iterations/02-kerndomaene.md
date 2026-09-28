# Iteration 2 – Kerndomäne

**Ziel:** Das fachliche Herz steht: Organisationen, Kontakte mit Firmenzuordnung und Projektanfragen mit Preismodell, Laufzeit und berechnetem Wert. Die Anfragen laufen über ein Pipeline-Board mit Drag & Drop. Jede Änderung wird auditiert, und Archivieren ersetzt das Löschen.

**Stories:** US-01 AK3, US-02 – US-09
**Referenzen:** `docs/SPEC.md` Kap. 2.2–2.6, 3.3 (S2–S4, S6), 4, 7.3–7.4; ADR-005, ADR-006, ADR-010, ADR-011

**Bewusst nicht in dieser Iteration** (Abhängigkeit von späteren Iterationen):
- Filter nach Tag (US-04 AK2) → It. 4, zusammen mit Tags (US-15)
- Anzeige „Phase: Beworben → Im Gespräch“ in der Timeline (US-07 AK2): In It. 2 wird nur der AuditEntry geschrieben, die Anzeige folgt mit der Timeline in It. 3
- „Tage seit letzter Activity“ auf Pipeline-Karten → It. 3 (Activities)
- Detailansichten mit Inline-Editing (S5, US-14): In It. 2 wird über Dialoge bearbeitet, S5 folgt in It. 3/4
- Volltext- und Trigram-Suche → It. 4; die Listen suchen weiter per `ILIKE`
- Oberfläche für Preis-Defaults in `/settings` → It. 3, zusammen mit dem Schwellwert „eingeschlafen“ (US-12 AK2); in It. 2 gelten die Defaults aus `IAppSettings`

---

## Schritt 1 – Querschnitt: Audit, Archivierung, Einstellungen
- [ ] `AuditEntry` (SPEC 2.4) + EF-Konfiguration (`changes` als jsonb)
- [ ] `AuditInterceptor` (ADR-006): `Created`/`Updated`/`Deleted`/`Archived` mit Feld-Diffs; Complex Types (`Pricing`, `Duration`) als einzelne Felder diffen; läuft in derselben Transaktion wie die Änderung
- [ ] Integrationstest: Anlage eines Kontakts schreibt einen AuditEntry `Created` (**US-01 AK3**)
- [ ] Archivierung als gemeinsamer Baustein: `IsArchived` plus `Archive()`/`Restore()` für Contact, Organization und Opportunity; der Audit-Interceptor schreibt dafür `Archived` statt `Updated`
- [ ] `ExtraFields` (jsonb, `Dictionary<string,string>`) für Contact, Organization und Opportunity, vorerst ohne UI
- [ ] `AppSetting` + `IAppSettings` (typisiertes Lesen mit Defaults: `HoursPerDay` = 8, `RetainerValuationMonths` = 12, `DefaultCurrency` = EUR, `DefaultPricingModel` = Hourly)
- [ ] `OutboxMessage` (SPEC 2.4) + `OutboxInterceptor` (ADR-010): Domain Events aller Entitäten transaktional in die Outbox schreiben, danach `ClearDomainEvents()`; die Verarbeitung und die Webhooks folgen in It. 5

## Schritt 2 – Organization (US-03, US-04, US-05)
- [ ] Entität `Organization` (SPEC 2.3) inkl. `OrganizationType` (als string gespeichert), Website-Validierung und Normalisierung (Vorbereitung auf Enrichment, SPEC 9.5); Event `OrganizationCreated`
- [ ] Use Cases `CreateOrganization`, `UpdateOrganization`, `GetOrganizations` (Paging Default 50, Suche, Filter Typ, Sortierung, „Archivierte anzeigen“), `ArchiveOrganization`/`RestoreOrganization`
- [ ] `SearchOrganizations` für Autocomplete (optional mit bevorzugtem Typ, siehe US-06 AK2)
- [ ] Seite `/organizations` (analog `/contacts`) mit Typ-Filter, Quick-Add (`N` auf dieser Seite legt eine Organisation an) und Bearbeiten-Dialog

## Schritt 3 – Contact erweitern (US-02, US-04, US-05)
- [ ] `Contact` um `OrganizationId` (FK, `ON DELETE SET NULL`) und `Source` (`LeadSource`) erweitern
- [ ] `UpdateContact` mit Bearbeiten-Dialog; Organisations-Autocomplete mit „Neu anlegen: <Eingabe>“ (legt die Organisation inline mit Typ `Other` an, in derselben Transaktion)
- [ ] `GetContacts`: Filter Organisation und Quelle, Sortierung, „Archivierte anzeigen“; Spalte Organisation in der Liste
- [ ] `ArchiveContact`/`RestoreContact`; archivierte Kontakte erscheinen nicht in Liste und Suche (US-05 AK1)

## Schritt 4 – Opportunity-Domäne (ADR-011)
- [ ] Value Objects `Pricing` und `Duration` (unveränderliche Records, Validierung per Factory: `Amount > 0`, `Value > 0`, ISO-4217-Code) + `Pricing.ToDisplayString()` („95 €/h“, „2.500 €/Monat“)
- [ ] Enums `PricingModel`, `DurationUnit`, `LeadSource`, `LostReason`, `StageStatus`
- [ ] Entität `Stage` + Seed der sechs Standard-Stages (SPEC 2.3) mit festen IDs in der Migration
- [ ] Entität `Opportunity` (SPEC 2.3), FKs auf Client-/Agency-Organisation, Primary Contact und Stage; `Pricing`/`Duration` als nullable Complex Types
- [ ] Domain-Service `OpportunityValuation`: `EstimatedValue` und `MonthlyRecurringValue` für alle vier Preismodelle, inkl. aller Regeln für fehlende Angaben
- [ ] `Opportunity.ChangeStage(stage, lostReason, now)`: `Lost` erfordert einen Absagegrund, `Won`/`Lost` setzen `ClosedAt`; Event `OpportunityStageChanged`; Wiedereröffnen (Wechsel von `Won`/`Lost` auf eine offene Stage) ist erlaubt und leert `ClosedAt` und `LostReason`
- [ ] Früh verifizieren: Nullable Complex Types mit EF Core 10 + Npgsql (Migration, Speichern, Laden, `null`). Bei Problemen: Fallback laut ADR-011 und ADR aktualisieren

## Schritt 5 – Anfragen erfassen (US-06)
- [ ] Use Cases `CreateOpportunity`, `UpdateOpportunity`, `GetOpportunity`, `ArchiveOpportunity`/`RestoreOpportunity`
- [ ] Dialog „Neue Anfrage“ (Quick-Add `N` auf der Pipeline): Pflicht ist nur der Titel, Stage-Default ist die erste offene Stage
- [ ] Endkunde und Vermittler als getrennte Autocompletes: passender Typ zuerst, alle anderen weiterhin wählbar (AK2)
- [ ] Preismodell-Auswahl mit dynamischem Label bzw. Einheit; Auslastung nur bei Stunden- und Tagessatz (AK3); Laufzeit als Zahl + Einheit oder leer (AK4)
- [ ] Live-Anzeige von `EstimatedValue` und bei Retainern zusätzlich MRR (AK5), berechnet über denselben Domain-Service

## Schritt 6 – Pipeline (US-07, US-08)
- [ ] `GetPipelineBoard`: offene Stages mit Karten (Titel, Endkunde/Vermittler, Preis im Modellformat), je Spalte Summe `EstimatedValue` und ggf. Summe MRR
- [ ] `MoveOpportunity` (Stage-Wechsel über `ChangeStage`): schreibt AuditEntry und `OpportunityStageChanged` (US-07 AK1)
- [ ] Seite `/pipeline` mit `MudDropContainer`; Won/Lost als Drop-Zonen
- [ ] Dialog Absagegrund beim Wechsel auf `Lost` (Pflicht, Abbrechen setzt die Karte zurück) (US-08 AK1)
- [ ] Gewonnene/verlorene Anfragen verlassen das aktive Board (AK2); Filter „Abgeschlossene“ zeigt Won und Lost (AK3)

## Schritt 7 – Stages verwalten (US-09)
- [ ] Seite `/settings` mit Abschnitt Stages: anlegen, umbenennen, umsortieren (Drag & Drop oder Pfeile)
- [ ] `DeleteStage` mit Ziel-Stage für die Umverteilung, falls Anfragen zugeordnet sind (AK2)
- [ ] Invariante: Es bleibt immer mindestens je eine Stage mit `Won` und `Lost` (fachlicher Fehler als `Result`)

## Schritt 8 – Tests
- [ ] Unit-Tests `Pricing`, `Duration`, `OpportunityValuation` (alle vier Modelle × Laufzeiteinheiten × fehlende Angaben, Retainer-Aufrundung)
- [ ] Unit-Tests `Opportunity.ChangeStage` (Lost ohne Grund, `ClosedAt`, Wiedereröffnen, Event)
- [ ] Handler-Tests (Happy Path + Validierungsfehler) für alle neuen Use Cases
- [ ] Integrationstests: AuditInterceptor (Created/Updated/Archived, Diff von Complex Types), Complex Types roundtrip, Stage-Seed, `DeleteStage` mit Umverteilung, Archiv-Filter, OutboxInterceptor (Event landet in derselben Transaktion wie die Änderung)
- [ ] bUnit: Anfrage-Dialog (dynamisches Label, Auslastung ein-/ausgeblendet, Live-Wert), Absagegrund-Dialog

## Schritt 9 – Abschluss
- [ ] Migrationen erzeugt und per `efbundle` in Produktion ausgerollt (abwärtskompatibel, siehe ADR-009)
- [ ] ADR-011: Erfahrungen mit nullable Complex Types dokumentieren
- [ ] README-Stand und Screenshot (Pipeline) aktualisieren

## Entscheidungen (2026-09-28)
1. **Outbox-Schreiben vorgezogen:** `OutboxMessage` + `OutboxInterceptor` entstehen in It. 2, damit `OpportunityStageChanged` (US-07 AK1) und die übrigen Events nicht verloren gehen. Verarbeitung und Webhooks bleiben in It. 5.
2. **Wiedereröffnen erlaubt:** Der Wechsel von `Won`/`Lost` auf eine offene Stage leert `ClosedAt` und `LostReason`; die alten Werte bleiben im AuditEntry erhalten.
3. **Preis-Defaults:** In It. 2 nur über `IAppSettings`; die Oberfläche folgt in It. 3.

## Definition of Done
- CI grün (Build + alle Tests); `EstimatedValue` für alle vier Preismodelle vollständig unit-getestet
- Organisationen und Kontakte lassen sich anlegen, bearbeiten, zuordnen, filtern, archivieren und wiederherstellen
- Anfragen lassen sich erfassen und per Drag & Drop durch die Pipeline bewegen; Won/Lost inkl. Absagegrund funktionieren
- Jede Änderung an Contact, Organization und Opportunity erzeugt einen AuditEntry (inkl. `Created` für Kontakte)
- In Produktion deployt, Migration ohne Datenverlust; keine Warnings
