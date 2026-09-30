# Iteration 4 – Suche & Komfort

**Ziel:** Alles ist mit wenigen Tastendrücken erreichbar. Eine Command Palette findet Kontakte, Organisationen und Anfragen tippfehlertolerant, und globale Tastenkürzel führen zu den Hauptansichten. Stammdaten werden direkt in der Detailansicht bearbeitet, und Tags ordnen Kontakte, Organisationen und Anfragen quer zu den Typen.

**Stories:** US-13 – US-15, US-04 AK2 (Filter nach Tag)
**Referenzen:** `docs/SPEC.md` Kap. 2.3 (Tag), 3.1–3.3 (S3–S6), 4 (US-13 – US-15), 6 (Performance, Barrierearmut), 7.4 (Suche), 7.5 (`keyboard.js`); ADR-003, ADR-006, ADR-007

**Aus früheren Iterationen übernommen:**
- Volltext- und Trigram-Suche statt `ILIKE` (It. 2)
- Filter nach Tag in den Listen (US-04 AK2, It. 2)
- Tags in der Detailansicht (US-15, It. 3)
- Inline-Editing der Stammdaten (US-14, It. 3); die Detailansichten bearbeiten bisher über Dialoge
- Tastenkürzel `G` + `H/P/K/O` (It. 3)
- `TODO(US-13)` in `MainLayout.razor`: Das Suchfeld in der App-Leiste öffnet die Command Palette

**Bewusst nicht in dieser Iteration:**
- Semantische Suche per pgvector (Backlog, ADR-007)
- Gespeicherte Filter und Listen (Backlog)
- Suche in Activity-Texten und Tasks (US-13 AK2 umfasst nur Kontakte, Organisationen und Anfragen)
- Tags auf Activities und Tasks (SPEC 2.3: nur Contact, Organization, Opportunity)

---

## Schritt 1 – Suche: Datenbank (ADR-007)
- [x] Extension `pg_trgm` per Migration aktivieren (analog `citext`)
- [x] Generierte `tsvector`-Spalten (Konfiguration `simple`) mit GIN-Index: Kontakte (Vorname, Nachname, E-Mail), Organisationen (Name, Website/Domain), Anfragen (Titel)
- [x] `pg_trgm`-GIN-Indizes auf Kontaktname, Organisationsname und Anfragetitel
- [x] Früh verifizieren: generierte `tsvector`-Spalten mit EF Core 10 + Npgsql (Mapping, Migration, keine Schreibzugriffe aus EF); bei Problemen ADR-007 ergänzen
- [x] Migration (additiv, siehe ADR-009)

## Schritt 2 – Suche: Use Case und Listen (US-13 AK2/AK3)
- [x] `Search(Text, Limit)`: Treffer über Kontakte (Name, E-Mail, Firma), Organisationen und Anfragen (Titel); Ranking kombiniert `ts_rank` und `similarity` (ADR-007); ab 2 Zeichen; Archivierte ausgeschlossen (US-05 AK1)
- [x] Tippfehlertoleranz: „Schmitt“ findet „Schmidt“ (AK3); Schwellwert für `similarity` festlegen und begründen
- [x] Firma bei Kontakten: Treffer über den Namen der zugeordneten Organisation (per Join, keine denormalisierte Spalte)
- [x] Listen- und Autocomplete-Suche (`GetContacts`, `GetOrganizations`, `SearchOrganizations`, `SearchOpportunities`) auf dieselbe Suche umstellen (Entscheidung 1)
- [x] Integrationstest: Relevanz-Reihenfolge, Tippfehler, Archivierte, Laufzeit < 200 ms bei 10k Kontakten (NFR Kap. 6, `LargeDataset` aus It. 3 wiederverwenden)

## Schritt 3 – Command Palette (US-13)
- [x] `Ctrl/Cmd + K` öffnet die Palette; ein Klick (bzw. `Enter`) auf das Suchfeld in der App-Leiste ebenfalls (`TODO(US-13)` auflösen) (AK1); bewusst nicht schon beim Fokus, damit Tab-Navigation keinen Dialog öffnet (WCAG 3.2.1)
- [x] Treffer nach ≤ 2 Zeichen, gruppiert nach Typ, mit Zusatzinfo (Firma, Phase, Typ) (AK1, AK2)
- [x] Pfeiltasten wählen, `Enter` öffnet die Detailansicht, `Esc` schließt (AK4)
- [x] Aktionen ohne Suchtext bzw. passend zum Suchtext: „Neuer Kontakt“, „Neue Organisation“, „Neue Anfrage“, „Neue Aufgabe“, „Gehe zu Heute/Pipeline/Kontakte/Organisationen/Einstellungen“ (SPEC 3.1 Nr. 2)
- [x] Debounce und Abbruch veralteter Anfragen (`CancellationToken`), damit schnelles Tippen die DB nicht flutet

## Schritt 4 – Tastenkürzel (SPEC 3.2)
- [x] `keyboard.js` um Sequenzen erweitern: `G` dann `H`/`P`/`K`/`O` (Zeitfenster z. B. 1 s)
- [x] `Ctrl/Cmd + K` global, auch in Eingabefeldern; alle übrigen Kürzel nur außerhalb von Eingabefeldern und Dialogen (wie `N`)
- [x] `Esc` bricht Inline-Edit ab und schließt Dialoge/Palette (einheitlich prüfen); alle Dialoge und Bestätigungen nutzen `DialogDefaults` (Inline-Edit siehe Schritt 7)
- [x] Übersicht der Tastenkürzel per `?` als kleiner, statischer Dialog (Entscheidung 5)

## Schritt 5 – Tags: Domäne und Verwaltung (US-15)
- [x] Entität `Tag` (SPEC 2.3): `Name` (eindeutig, case-insensitive), `Color` aus einer festen Palette mit geprüftem Kontrast, neue Tags bekommen reihum die nächste Farbe (Entscheidung 7); drei typisierte Join-Tabellen `contact_tags`, `organization_tags`, `opportunity_tags` (ADR-005), FKs mit `ON DELETE CASCADE`
- [x] Use Cases `CreateTag`, `RenameTag`, `ChangeTagColor` (Auswahl aus der Palette), `DeleteTag`, `GetTags` (mit Anzahl Zuordnungen), `SearchTags` (Autocomplete)
- [x] Zuordnen/Entfernen: gemeinsame Use Cases `AssignTag`/`RemoveTag` mit `TimelineRecordType`; `AssignTag` legt einen neuen Tag inline an bzw. nutzt einen gleichnamigen (case-insensitive); dazu `GetRecordTags` für die Detailansichten
- [x] Abschnitt „Tags“ in `/settings`: anlegen, umbenennen, Farbe ändern, löschen (mit Hinweis auf Anzahl Zuordnungen)
- [x] Audit: Tag-Zuordnungen werden auditiert (hinzugefügt/entfernt, als `Updated` des Datensatzes mit Feld `Tags`), erscheinen aber nicht in der Timeline (Entscheidung 3); `DeleteTag` entfernt die Zuordnungen explizit, damit jeder Datensatz seinen Eintrag bekommt
- [x] Migration (additiv)

## Schritt 6 – Tags: Oberfläche (US-15, US-04 AK2)
- [x] Chip-Eingabe in allen drei Detailansichten: Autocomplete auf bestehende Tags, „Neu anlegen: <Eingabe>“ legt den Tag inline an (AK1)
- [x] Tags als farbige Chips in den Listen (Kontakte, Organisationen) und auf Pipeline-Karten
- [x] Filter nach Tag in `/contacts` und `/organizations` (US-04 AK2): mehrere Tags, ODER-verknüpft; kein Pipeline-Filter (Entscheidung 4)
- [x] Kontrast der Tag-Farben im Hell- und Dunkelmodus prüfen (NFR Barrierearmut): weiße Schrift auf jeder Palettenfarbe ≥ 4,5:1, per Unit-Test berechnet; im Browser in beiden Modi gesichtet

## Schritt 7 – Inline-Editing (US-14)
- [x] Wiederverwendbare Inline-Felder: Text, mehrzeiliger Text, Auswahl, Zahl, Datum, Organisations-/Kontakt-Autocomplete (Hülle `InlineField`; Datum als natives Datumsfeld, weil `MudDatePicker` bei Enter den Kalender öffnet)
- [x] Klick auf ein Feld (oder `Enter`/`F2` per Tastatur) → Bearbeiten → `Enter`/Blur speichert, `Esc` bricht ab (SPEC 3.1 Nr. 3)
- [x] Validierung und Fehlermeldung am Feld; Fehler aus den bestehenden Validatoren und `…Errors.FieldOf` nutzen (AK1)
- [x] Anfrage: zusammengesetzte Felder als Gruppe bearbeiten (Preis = Modell + Betrag + Währung, Laufzeit = Zahl + Einheit), inklusive Live-Wert; Phasenwechsel auf `Lost` öffnet den Absagegrund-Dialog
- [x] Speichern über die bestehenden `Update…`-Handler mit allen aktuellen Werten („last write wins“, Entscheidung 2); danach Timeline neu laden (Änderungen erscheinen dort)
- [x] Bearbeiten-Dialog (Stift) aus den Detailansichten entfernen; Quick-Add, Listen und Pipeline behalten ihre Dialoge (Entscheidung 2); „Archivieren/Wiederherstellen“ der Anfrage wandert aus dem Dialog in den Kopf der Detailansicht

## Schritt 8 – Tests
- [ ] Unit-Tests `Tag` (Name, Farbe, Normalisierung)
- [ ] Handler-Tests (Happy Path + Validierungsfehler) für alle neuen Use Cases
- [ ] Integrationstests: Suche (Ranking, Tippfehler, Firma, Archivierte, Performance), Tag-Eindeutigkeit (case-insensitive), Löschen eines Tags mit Zuordnungen, Tag-Filter in den Listen, Migration der generierten Spalten
- [ ] bUnit: Command Palette (Tastaturnavigation, Aktionen), Inline-Feld (Enter/Blur/Esc, Fehler am Feld), Tag-Chip-Eingabe mit „Neu anlegen“
- [ ] Browser-Smoke-Test der Tastenkürzel (`Ctrl/Cmd + K`, `G` + `H/P/K/O`), da die JS-Logik in bUnit nicht läuft

## Schritt 9 – Abschluss
- [ ] Migrationen erzeugt und per `efbundle` in Produktion ausgerollt (abwärtskompatibel, siehe ADR-009)
- [ ] ADR-007: Erfahrungen mit generierten `tsvector`-Spalten, `pg_trgm`-Schwellwert und Ranking dokumentieren
- [ ] README-Stand und Screenshot (Command Palette) aktualisieren
- [ ] SPEC nachziehen (Entscheidungen, Tastenkürzel, Tag-Regeln)

## Entscheidungen (2026-09-29)
1. **Einheitliche Suche:** Listen und Autocompletes nutzen dieselbe Volltext-/Trigram-Suche wie die Command Palette (Tippfehlertoleranz überall); `ILIKE` entfällt.
2. **Inline-Editing ersetzt den Dialog in der Detailansicht.** Gespeichert wird über die bestehenden `Update…`-Handler mit allen aktuellen Werten; bei einem Single-User-System ist „last write wins“ vertretbar. Quick-Add, Listen und Pipeline behalten ihre Dialoge.
3. **Tag-Zuordnungen werden auditiert**, aber nicht in der Timeline angezeigt (SPEC 2.5 bleibt unverändert).
4. **Tag-Filter:** mehrere Tags, ODER-verknüpft, in Kontakten und Organisationen; ein Pipeline-Filter folgt erst bei Bedarf.
5. **Tastenkürzel-Übersicht** per `?` als kleiner, statischer Dialog.
6. **Umlaute:** zunächst nur Trigram-Ähnlichkeit; `unaccent` erst, wenn Schreibvarianten („Mueller“/„Müller“) im Alltag stören.
7. **Tag-Farben:** feste Palette mit geprüftem Kontrast (hell und dunkel); neue Tags bekommen reihum die nächste Farbe, änderbar in den Einstellungen.
8. **Testabdeckung** wird in der CI vorerst nicht gemessen (NFR Wartbarkeit bleibt Ziel, ohne automatische Prüfung).

## Definition of Done
- CI grün (Build + alle Tests); keine Warnings
- `Ctrl/Cmd + K` findet Kontakte, Organisationen und Anfragen nach ≤ 2 Zeichen, tippfehlertolerant, per Tastatur bedienbar
- `G` + `H/P/K/O` navigiert; alle Kürzel sind dokumentiert
- Alle Stammdatenfelder der Detailansichten sind inline editierbar, mit Fehlermeldung am Feld
- Tags lassen sich vergeben, inline anlegen, verwalten und in den Listen filtern
- Suche < 200 ms bei 10k Kontakten
- In Produktion deployt, Migration ohne Datenverlust
