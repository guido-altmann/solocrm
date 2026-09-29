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
- [ ] Enum `ActivityType` (`Note`, `Call`, `Meeting`, `Email`, `ApplicationSent`)
- [ ] Entität `Activity` (SPEC 2.3): `Type`, `OccurredAt` (rückdatierbar), `Subject?` (200), `Body` (text), `ContactId`/`OrganizationId`/`OpportunityId` (mindestens einer gesetzt, als Domain-Regel und Check-Constraint); Event `ActivityLogged`
- [ ] Entität `TaskItem` (SPEC 2.3): `Title` (200), `DueDate?`, `CompletedAt?`, optionale Bezüge; `Complete(now)` mit Event `TaskCompleted`, `Reopen()` (für Undo, siehe Offene Fragen)
- [ ] Beide `IAuditable`; FKs mit `ON DELETE CASCADE` bzw. `SET NULL` gemäß DSGVO-Löschung (US-20, It. 6) vorbereiten
- [ ] Indizes für die Timeline: je Bezug `(contact_id, occurred_at)`, `(organization_id, occurred_at)`, `(opportunity_id, occurred_at)`; für Tasks `(completed_at, due_date)`
- [ ] Migration (additiv, siehe ADR-009)

## Schritt 2 – Activities erfassen (US-10)
- [ ] Use Cases `LogActivity`, `UpdateActivity`, `DeleteActivity` (siehe Offene Fragen)
- [ ] Eingabe oben in der Timeline: „Notiz hinzufügen…“ mit Typ-Auswahl, Betreff optional, `OccurredAt` mit Default „jetzt“ und rückdatierbar (AK1)
- [ ] Bezug automatisch aus dem Kontext (Detailansicht); bei Anfragen optional zusätzlich Kontakt/Organisation
- [ ] Quick-Add `N` in einer Detailansicht legt eine Notiz an (SPEC 3.2)

## Schritt 3 – Tasks (US-11)
- [ ] Use Cases `CreateTask`, `UpdateTask`, `CompleteTask`, `ReopenTask`, `DeleteTask`
- [ ] Anlage aus der Detailansicht (Bezug vorbelegt) und aus „Heute“ (frei oder mit Bezug) (AK1)
- [ ] Erledigen per Checkbox, danach Snackbar mit „Rückgängig“ für 5 s (AK2)
- [ ] Offene Tasks eines Objekts in der Detailansicht; erledigte erscheinen in der Timeline

## Schritt 4 – Timeline (Aggregation nach SPEC 2.5)
- [ ] `GetTimeline(EntityType, EntityId, Before?, Limit)`: absteigend sortierter Strom aus Activities, Tasks (angelegt/erledigt) und AuditEntries; Paging per Cursor („Mehr laden“)
- [ ] Aggregation: Organization inklusive Einträge ihrer Kontakte und Anfragen (Kennzeichnung „via Max Mustermann“); Contact inklusive Anfragen, bei denen er `PrimaryContact` ist; Opportunity nur direkt
- [ ] Audit-Filter: nur `Created`, `Archived` und Änderungen an Stage, Pricing, Duration, `OrganizationId` und `IsArchived`; übrige Änderungen bleiben unsichtbar
- [ ] Darstellung der Audit-Einträge in lesbarer Form: „Phase: Beworben → Im Gespräch“ (Stage-Namen auflösen, gelöschte Stages als „(gelöscht)“) (**US-07 AK2**), „Preis: 95 €/h → 105 €/h“, „Laufzeit: offen → 6 Monate“, „Firma: – → Contoso“, „Archiviert“/„Wiederhergestellt“
- [ ] Integrationstest: Aggregation und Audit-Filter gegen Postgres, Laufzeit < 200 ms bei 50k Activities (NFR Kap. 6), ggf. mit `EXPLAIN`

## Schritt 5 – Detailansichten (S5)
- [ ] `/contacts/{id}`, `/organizations/{id}`, `/opportunities/{id}`: links Stammdaten (lesend, „Bearbeiten“ öffnet den bestehenden Dialog), verknüpfte Objekte, offene Tasks; rechts die Timeline mit Eingabe
- [ ] Verknüpfte Objekte: Organisation → Kontakte und Anfragen (als Endkunde bzw. Vermittler); Kontakt → Organisation und Anfragen; Anfrage → Endkunde, Vermittler, Ansprechpartner, Stage, Wert/MRR
- [ ] Navigation: Zeilen der Listen und Pipeline-Karten führen zur Detailansicht (die Bearbeiten-Dialoge bleiben erreichbar)
- [ ] Responsive: unter 960 px einspaltig (SPEC 3.4)

## Schritt 6 – Heute-Ansicht (US-12)
- [ ] `GetToday`: Abschnitte Überfällig (rot), Heute, Eingeschlafene Anfragen, Zuletzt bearbeitet (AK1); „heute“ nach lokaler Zeitzone (siehe Offene Fragen)
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
- [ ] Integrationstests: Timeline-Aggregation je Objekttyp, Audit-Filter und Formatierung, Cursor-Paging, eingeschlafene Anfragen (Grenzfälle: keine Activity, genau N Tage, archiviert, geschlossen), Outbox-Events `activity.logged`/`task.completed`
- [ ] bUnit: Timeline-Eingabe (Typ, Rückdatieren), Task-Checkbox mit Undo, Heute-Abschnitte

## Schritt 9 – Abschluss
- [ ] Migrationen erzeugt und per `efbundle` in Produktion ausgerollt (abwärtskompatibel, siehe ADR-009)
- [ ] ADR-006: Umsetzung der Timeline-Aggregation und des Audit-Filters dokumentieren (Konfiguration der sichtbaren Felder)
- [ ] README-Stand und Screenshot (Heute oder Detailansicht) aktualisieren

## Offene Fragen (vor Start klären)
1. **Markdown im Activity-Body:** Die SPEC erlaubt Markdown. Zum Rendern bräuchte es ein neues Paket (Vorschlag: `Markdig`, Ausgabe als bereinigtes HTML). Alternative für It. 3: Klartext mit Zeilenumbrüchen, Markdown später. *Paket freigeben?*
2. **Undo beim Erledigen (US-11 AK2):** Vorschlag: sofort speichern (inklusive `TaskCompleted` in der Outbox) und bei „Rückgängig“ `ReopenTask` ausführen. Alternative: erst nach Ablauf der 5 s speichern (dann geht der Klick verloren, wenn die Verbindung abbricht). Beim Vorschlag sehen Webhook-Empfänger (It. 5) ggf. ein `task.completed` ohne Gegenereignis. Soll es ein `TaskReopened`-Event geben?
3. **Bearbeiten und Löschen von Activities:** Die SPEC regelt das nicht. Vorschlag: bearbeiten und hart löschen (mit AuditEntry `Deleted`), weil Activities Notizen sind und kein Archivieren brauchen.
4. **Tasks ohne Fälligkeitsdatum:** „Heute“ zeigt nur Überfällig und Heute; freie Tasks ohne Datum wären nirgends sichtbar. Vorschlag: zusätzlicher, eingeklappter Abschnitt „Ohne Termin“ auf „Heute“ (statt einer eigenen Task-Liste).
5. **Zeitzone für „heute“/„überfällig“:** Vorschlag: feste Zeitzone per Konfiguration (`App__TimeZone`, Default `Europe/Berlin`), da Single-User und Server in UTC.
6. **Audit-Eintrag `Created` in der Timeline:** Vorschlag: als „Angelegt“ anzeigen (SPEC 2.5 nennt „Anlage“), bei Aggregation mit „via …“ nur für Anfragen, nicht für Kontakte einer Organisation (sonst viel Rauschen).

## Definition of Done
- CI grün (Build + alle Tests); keine Warnings
- Activities und Tasks lassen sich zu Kontakten, Organisationen und Anfragen erfassen; Tasks lassen sich mit Undo erledigen
- Jede Detailansicht zeigt die Timeline gemäß SPEC 2.5, inklusive „Phase: X → Y“
- „Heute“ zeigt Überfällig, Heute, Eingeschlafene Anfragen und Zuletzt bearbeitet; der Schwellwert ist einstellbar
- Preis-Defaults sind in `/settings` pflegbar
- Timeline und Heute-Ansicht < 200 ms bei 10k Kontakten und 50k Activities
- In Produktion deployt, Migration ohne Datenverlust
