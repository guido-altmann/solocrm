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
- [ ] Extension `pg_trgm` per Migration aktivieren (analog `citext`)
- [ ] Generierte `tsvector`-Spalten (Konfiguration `simple`) mit GIN-Index: Kontakte (Vorname, Nachname, E-Mail), Organisationen (Name, Website/Domain), Anfragen (Titel)
- [ ] `pg_trgm`-GIN-Indizes auf Kontaktname, Organisationsname und Anfragetitel
- [ ] Früh verifizieren: generierte `tsvector`-Spalten mit EF Core 10 + Npgsql (Mapping, Migration, keine Schreibzugriffe aus EF); bei Problemen ADR-007 ergänzen
- [ ] Migration (additiv, siehe ADR-009)

## Schritt 2 – Suche: Use Case und Listen (US-13 AK2/AK3)
- [ ] `Search(Text, Limit)`: Treffer über Kontakte (Name, E-Mail, Firma), Organisationen und Anfragen (Titel); Ranking kombiniert `ts_rank` und `similarity` (ADR-007); ab 2 Zeichen; Archivierte ausgeschlossen (US-05 AK1)
- [ ] Tippfehlertoleranz: „Schmitt“ findet „Schmidt“ (AK3); Schwellwert für `similarity` festlegen und begründen
- [ ] Firma bei Kontakten: Treffer über den Namen der zugeordneten Organisation (per Join, keine denormalisierte Spalte)
- [ ] Listen- und Autocomplete-Suche (`GetContacts`, `GetOrganizations`, `SearchOrganizations`, `SearchOpportunities`) auf dieselbe Suche umstellen (siehe offene Frage 1)
- [ ] Integrationstest: Relevanz-Reihenfolge, Tippfehler, Archivierte, Laufzeit < 200 ms bei 10k Kontakten (NFR Kap. 6, `LargeDataset` aus It. 3 wiederverwenden)

## Schritt 3 – Command Palette (US-13)
- [ ] `Ctrl/Cmd + K` öffnet die Palette; ein Klick oder Fokus auf das Suchfeld in der App-Leiste ebenfalls (`TODO(US-13)` auflösen) (AK1)
- [ ] Treffer nach ≤ 2 Zeichen, gruppiert nach Typ, mit Zusatzinfo (Firma, Phase, Typ) (AK1, AK2)
- [ ] Pfeiltasten wählen, `Enter` öffnet die Detailansicht, `Esc` schließt (AK4)
- [ ] Aktionen ohne Suchtext bzw. passend zum Suchtext: „Neuer Kontakt“, „Neue Organisation“, „Neue Anfrage“, „Neue Aufgabe“, „Gehe zu Heute/Pipeline/Kontakte/Organisationen/Einstellungen“ (SPEC 3.1 Nr. 2)
- [ ] Debounce und Abbruch veralteter Anfragen (`CancellationToken`), damit schnelles Tippen die DB nicht flutet

## Schritt 4 – Tastenkürzel (SPEC 3.2)
- [ ] `keyboard.js` um Sequenzen erweitern: `G` dann `H`/`P`/`K`/`O` (Zeitfenster z. B. 1 s)
- [ ] `Ctrl/Cmd + K` global, auch in Eingabefeldern; alle übrigen Kürzel nur außerhalb von Eingabefeldern und Dialogen (wie `N`)
- [ ] `Esc` bricht Inline-Edit ab und schließt Dialoge/Palette (einheitlich prüfen)
- [ ] Übersicht der Tastenkürzel (siehe offene Frage 5)

## Schritt 5 – Tags: Domäne und Verwaltung (US-15)
- [ ] Entität `Tag` (SPEC 2.3): `Name` (eindeutig, case-insensitive), `Color` (Hex); drei typisierte Join-Tabellen `contact_tags`, `organization_tags`, `opportunity_tags` (ADR-005), FKs mit `ON DELETE CASCADE`
- [ ] Use Cases `CreateTag`, `RenameTag`, `ChangeTagColor`, `DeleteTag`, `GetTags` (mit Anzahl Zuordnungen), `SearchTags` (Autocomplete)
- [ ] Zuordnen/Entfernen: `AssignTag`/`RemoveTag` je Objekttyp oder ein gemeinsamer Use Case mit `TimelineRecordType`
- [ ] Abschnitt „Tags“ in `/settings`: umbenennen, Farbe ändern, löschen (mit Hinweis auf Anzahl Zuordnungen)
- [ ] Audit: Tag-Zuordnungen protokollieren oder nicht (siehe offene Frage 3)
- [ ] Migration (additiv)

## Schritt 6 – Tags: Oberfläche (US-15, US-04 AK2)
- [ ] Chip-Eingabe in allen drei Detailansichten: Autocomplete auf bestehende Tags, „Neu anlegen: <Eingabe>“ legt den Tag inline an (AK1)
- [ ] Tags als farbige Chips in den Listen (Kontakte, Organisationen) und auf Pipeline-Karten
- [ ] Filter nach Tag in `/contacts` und `/organizations` (US-04 AK2), optional auf der Pipeline (siehe offene Frage 4)
- [ ] Kontrast der Tag-Farben im Hell- und Dunkelmodus prüfen (NFR Barrierearmut)

## Schritt 7 – Inline-Editing (US-14)
- [ ] Wiederverwendbare Inline-Felder: Text, mehrzeiliger Text, Auswahl, Zahl, Datum, Organisations-/Kontakt-Autocomplete
- [ ] Klick auf ein Feld (oder `Enter`/`F2` per Tastatur) → Bearbeiten → `Enter`/Blur speichert, `Esc` bricht ab (SPEC 3.1 Nr. 3)
- [ ] Validierung und Fehlermeldung am Feld; Fehler aus den bestehenden Validatoren und `…Errors.FieldOf` nutzen (AK1)
- [ ] Anfrage: zusammengesetzte Felder als Gruppe bearbeiten (Preis = Modell + Betrag + Währung, Laufzeit = Zahl + Einheit), inklusive Live-Wert; Phasenwechsel auf `Lost` öffnet den Absagegrund-Dialog
- [ ] Speichern über die bestehenden `Update…`-Handler (siehe offene Frage 2); danach Timeline neu laden (Änderungen erscheinen dort)
- [ ] Umgang mit den Bearbeiten-Dialogen in der Detailansicht (siehe offene Frage 2)

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

## Offene Fragen (vor dem Start zu klären)
1. **Suche in den Listen:** Sollen Listen und Autocompletes ebenfalls die neue Volltext-/Trigram-Suche nutzen (einheitliche Treffer, Tippfehlertoleranz auch dort)? *Vorschlag: ja.*
2. **Inline-Editing und Dialoge:** Ersetzt Inline-Editing den Bearbeiten-Dialog in der Detailansicht vollständig, oder bleibt der Stift als Alternative? Gespeichert wird über die bestehenden `Update…`-Handler mit allen aktuellen Werten (bei einem Single-User-System ist „last write wins“ vertretbar). *Vorschlag: Dialog in der Detailansicht entfernen; Quick-Add, Listen und Pipeline behalten ihre Dialoge.*
3. **Tags im Audit/in der Timeline:** Sollen Zuordnungen auditiert werden (z. B. „Tag hinzugefügt: Azure“), und sollen sie in der Timeline erscheinen? *Vorschlag: auditieren, aber nicht in der Timeline anzeigen (SPEC 2.5 nennt Tags nicht).*
4. **Tag-Filter:** Ein Tag oder mehrere (und wenn mehrere: UND oder ODER)? Auch auf der Pipeline? *Vorschlag: mehrere Tags mit ODER in Kontakte/Organisationen; Pipeline-Filter erst bei Bedarf.*
5. **Tastenkürzel-Übersicht:** Eine Hilfe per `?` (Dialog mit allen Kürzeln)? *Vorschlag: ja, klein und statisch.*
6. **Umlaute und Schreibvarianten:** Soll „Mueller“ auch „Müller“ finden (Extension `unaccent` plus eigene Suchkonfiguration), oder reicht die Trigram-Ähnlichkeit? *Vorschlag: zunächst nur Trigram; `unaccent` erst, wenn es im Alltag stört.*
7. **Tag-Farben:** Freie Hex-Farbe oder eine feste Palette (z. B. 10 Farben mit geprüftem Kontrast)? *Vorschlag: feste Palette, neue Tags bekommen reihum eine Farbe.*

## Definition of Done
- CI grün (Build + alle Tests); keine Warnings
- `Ctrl/Cmd + K` findet Kontakte, Organisationen und Anfragen nach ≤ 2 Zeichen, tippfehlertolerant, per Tastatur bedienbar
- `G` + `H/P/K/O` navigiert; alle Kürzel sind dokumentiert
- Alle Stammdatenfelder der Detailansichten sind inline editierbar, mit Fehlermeldung am Feld
- Tags lassen sich vergeben, inline anlegen, verwalten und in den Listen filtern
- Suche < 200 ms bei 10k Kontakten
- In Produktion deployt, Migration ohne Datenverlust
