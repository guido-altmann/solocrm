# Iteration 3 – Timeline & Tasks

**Ziel:** Jedes Objekt hat seine Historie. Activities (Notiz, Anruf, Meeting, E-Mail, Bewerbung) und Follow-up-Tasks werden erfasst und erscheinen zusammen mit den relevanten AuditEntries in einer chronologischen Timeline. Die Ansicht „Heute“ zeigt täglich alles Fällige und die eingeschlafenen Anfragen. Kontakte, Organisationen und Anfragen bekommen dafür eine Detailansicht.

**Stories:** US-10 – US-12, US-07 AK2 (Anzeige des Stage-Wechsels in der Timeline)
**Referenzen:** `docs/SPEC.md` Kap. 2.3 (Activity, TaskItem), 2.4 (AppSetting), 2.5, 2.6, 3.1–3.3 (S1, S5, S6); ADR-006, ADR-010

**Aus It. 2 übernommen:**
- Timeline-Anzeige „Phase: Beworben → Im Gespräch“ (US-07 AK2); die AuditEntries dafür existieren bereits
- „Tage seit letzter Activity“ auf den Pipeline-Karten
- Oberfläche für die Preis-Defaults in `/settings` (heute nur über `IAppSettings`)
- Detailansichten (S5), zunächst ohne Inline-Editing

**Bewusst nicht in dieser Iteration:**
- Inline-Editing der Stammdaten (US-14) → It. 4; in It. 3 wird in der Detailansicht weiter über die bestehenden Dialoge bearbeitet
- Tags in der Detailansicht (US-15) → It. 4
- Command Palette und die Tastenkürzel `G` + `H/P/K/O` → It. 4
- Versand von `ActivityLogged`/`TaskCompleted` per Webhook → It. 5 (die Events landen ab jetzt in der Outbox)

---

## Schritt 1 – Domäne: Activity und TaskItem
- [x] Enum `ActivityType` (`Note`, `Call`, `Meeting`, `Email`, `ApplicationSent`)
- [x] Entität `Activity` (SPEC 2.3): `Type`, `OccurredAt` (rückdatierbar), `Subject?` (200), `Body` (text), `ContactId`/`OrganizationId`/`OpportunityId` (mindestens einer gesetzt, als Domain-Regel und Check-Constraint); Event `ActivityLogged`
- [x] Entität `TaskItem` (SPEC 2.3): `Title` (200), `DueDate?`, `CompletedAt?`, optionale Bezüge; `Complete(now)` mit Event `TaskCompleted`, `Reopen()` mit Event `TaskReopened` (für Undo, Entscheidung 2)
- [x] Beide `IAuditable`; FKs mit `ON DELETE CASCADE` bzw. `SET NULL` gemäß DSGVO-Löschung (US-20, It. 6) vorbereiten
- [x] Indizes für die Timeline: je Bezug `(contact_id, occurred_at)`, `(organization_id, occurred_at)`, `(opportunity_id, occurred_at)`; für Tasks `(completed_at, due_date)`
- [x] Migration (additiv, siehe ADR-009)

## Schritt 2 – Activities erfassen (US-10)
- [x] Use Cases `LogActivity`, `UpdateActivity`, `DeleteActivity` (hartes Löschen mit AuditEntry, Entscheidung 3)
- [x] `Markdig` über `Directory.Packages.props` einbinden; Body als Markdown rendern (Roh-HTML deaktiviert, siehe Entscheidung 1)
- [x] Eingabe oben in der Timeline: „Notiz hinzufügen…“ mit Typ-Auswahl, Betreff optional, `OccurredAt` mit Default „jetzt“ und rückdatierbar (AK1)
- [x] Bezug automatisch aus dem Kontext (Detailansicht); bei Anfragen optional zusätzlich Kontakt/Organisation
- [x] Quick-Add `N` in einer Detailansicht legt eine Notiz an (SPEC 3.2) – *fokussiert die Eingabe der Timeline*

## Schritt 3 – Tasks (US-11)
- [x] Use Cases `CreateTask`, `UpdateTask`, `CompleteTask`, `ReopenTask`, `DeleteTask`
- [ ] Anlage aus der Detailansicht (Bezug vorbelegt) und aus „Heute“ (frei oder mit Bezug) (AK1) – *Detailansicht erledigt, „Heute“ folgt in Schritt 6*
- [x] Erledigen per Checkbox, danach Snackbar mit „Rückgängig“ für 5 s (AK2)
- [x] Offene Tasks eines Objekts in der Detailansicht; erledigte erscheinen in der Timeline

## Schritt 4 – Timeline (Aggregation nach SPEC 2.5)
- [x] `GetTimeline(EntityType, EntityId, Before?, Limit)`: absteigend sortierter Strom aus Activities, Tasks (angelegt/erledigt) und AuditEntries; Paging per Cursor („Mehr laden“)
- [x] Aggregation: Organization inklusive Einträge ihrer Kontakte und Anfragen (Kennzeichnung „via Max Mustermann“); Contact inklusive Anfragen, bei denen er `PrimaryContact` ist; Opportunity nur direkt
- [x] `Created` erscheint als „Angelegt“; in der Organisations-Aggregation nur für Anfragen (Entscheidung 6)
- [x] Audit-Filter: nur `Created`, `Archived` und Änderungen an Stage, Pricing, Duration, `OrganizationId` und `IsArchived`; übrige Änderungen bleiben unsichtbar
- [x] Darstellung der Audit-Einträge in lesbarer Form: „Phase: Beworben → Im Gespräch“ (Stage-Namen auflösen, gelöschte Stages als „(gelöscht)“) (**US-07 AK2**), „Preis: 95 €/h → 105 €/h“, „Laufzeit: offen → 6 Monate“, „Firma: – → Contoso“, „Archiviert“/„Wiederhergestellt“
- [x] Integrationstest: Aggregation und Audit-Filter gegen Postgres, Laufzeit < 200 ms bei 50k Activities (NFR Kap. 6), ggf. mit `EXPLAIN` – *lokal gemessen (Median): Organisation 14 ms, Kontakt 5 ms*

## Schritt 5 – Detailansichten (S5)
- [x] `/contacts/{id}`, `/organizations/{id}`, `/opportunities/{id}`: links Stammdaten (lesend, „Bearbeiten“ öffnet den bestehenden Dialog), verknüpfte Objekte, offene Tasks; rechts die Timeline mit Eingabe
- [x] Verknüpfte Objekte: Organisation → Kontakte und Anfragen (als Endkunde bzw. Vermittler); Kontakt → Organisation und Anfragen; Anfrage → Endkunde, Vermittler, Ansprechpartner, Stage, Wert/MRR
- [x] Navigation: Zeilen der Listen und Pipeline-Karten führen zur Detailansicht (die Bearbeiten-Dialoge bleiben erreichbar)
- [x] Responsive: unter 960 px einspaltig (SPEC 3.4)

## Schritt 6 – Heute-Ansicht (US-12)
- [ ] `GetToday`: Abschnitte Überfällig (rot), Heute, Eingeschlafene Anfragen, Zuletzt bearbeitet (AK1) sowie eingeklappt „Ohne Termin“ (Entscheidung 4)
- [ ] Zeitzone aus `App:TimeZone` (Default `Europe/Berlin`) für „heute“/„überfällig“ (Entscheidung 5)
- [ ] Eingeschlafen = offene, nicht archivierte Anfrage ohne Activity seit `StaleOpportunityDays` Tagen (Default 7); ohne jede Activity zählt `CreatedAt`
- [ ] Zuletzt bearbeitet: die zuletzt geänderten Kontakte, Organisationen und Anfragen (nach `UpdatedAt`, max. 10)
- [ ] Tasks direkt auf „Heute“ erledigen und anlegen
- [ ] Pipeline-Karten zeigen „Tage seit letzter Activity“ (aus It. 2 übernommen)

## Schritt 7 – Einstellungen (S6)
- [ ] `AppSettingKeys.StaleOpportunityDays` (Default 7)
- [ ] Abschnitt „Preise & Bewertung“ in `/settings`: `DefaultPricingModel`, `DefaultCurrency`, `HoursPerDay`, `RetainerValuationMonths` mit Validierung (`UpdatePricingSettings`)
- [ ] Abschnitt „Heute“: Schwellwert „eingeschlafen“ in Tagen (`UpdateTodaySettings`) (US-12 AK2)

## Schritt 8 – Tests
- [ ] Unit-Tests `Activity` (mindestens ein Bezug, Event), `TaskItem` (Complete/Reopen, Event, Überfällig-Logik)
- [ ] Handler-Tests (Happy Path + Validierungsfehler) für alle neuen Use Cases
- [ ] Integrationstests: Timeline-Aggregation je Objekttyp, Audit-Filter und Formatierung, Cursor-Paging, eingeschlafene Anfragen (Grenzfälle: keine Activity, genau N Tage, archiviert, geschlossen), Outbox-Events `activity.logged`/`task.completed`/`task.reopened`
- [ ] bUnit: Timeline-Eingabe (Typ, Rückdatieren), Task-Checkbox mit Undo, Heute-Abschnitte

## Schritt 9 – Abschluss
- [ ] Migrationen erzeugt und per `efbundle` in Produktion ausgerollt (abwärtskompatibel, siehe ADR-009)
- [ ] ADR-006: Umsetzung der Timeline-Aggregation und des Audit-Filters dokumentieren (Konfiguration der sichtbaren Felder)
- [ ] README-Stand und Screenshot (Heute oder Detailansicht) aktualisieren

## Entscheidungen (2026-09-29)
1. **Markdown im Activity-Body:** wird mit `Markdig` gerendert (Paket freigegeben), Ausgabe als bereinigtes HTML (kein Roh-HTML aus der Eingabe).
2. **Undo beim Erledigen:** `CompleteTask` speichert sofort (inklusive `TaskCompleted` in der Outbox); „Rückgängig“ führt `ReopenTask` aus. `ReopenTask` erzeugt das Event `TaskReopened`, damit Webhook-Empfänger (It. 5) das Rückgängigmachen sehen.
3. **Activities** lassen sich bearbeiten und hart löschen; das Löschen wird als AuditEntry `Deleted` protokolliert.
4. **Tasks ohne Fälligkeitsdatum** erscheinen auf „Heute“ in einem eingeklappten Abschnitt „Ohne Termin“ (keine eigene Task-Liste).
5. **Zeitzone:** „heute“ und „überfällig“ werden in einer konfigurierten Zeitzone berechnet (`App__TimeZone`, Default `Europe/Berlin`).
6. **„Angelegt“ in der Timeline:** Der AuditEntry `Created` erscheint als „Angelegt“; in der aggregierten Organisations-Timeline nur für Anfragen, nicht für die Kontakte der Organisation.

## Definition of Done
- CI grün (Build + alle Tests); keine Warnings
- Activities und Tasks lassen sich zu Kontakten, Organisationen und Anfragen erfassen; Tasks lassen sich mit Undo erledigen
- Jede Detailansicht zeigt die Timeline gemäß SPEC 2.5, inklusive „Phase: X → Y“
- „Heute“ zeigt Überfällig, Heute, Eingeschlafene Anfragen und Zuletzt bearbeitet; der Schwellwert ist einstellbar
- Preis-Defaults sind in `/settings` pflegbar
- Timeline und Heute-Ansicht < 200 ms bei 10k Kontakten und 50k Activities
- In Produktion deployt, Migration ohne Datenverlust
