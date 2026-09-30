# ADR-007: Suche mit PostgreSQL-Volltext und Trigram

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Command Palette und Listen brauchen schnelle, tippfehlertolerante Suche über Namen, E-Mails und Titel. Datenmenge: < 100k Datensätze.

## Entscheidung
Generierte `tsvector`-Spalten (Konfiguration `simple`, da überwiegend Eigennamen) mit GIN-Index plus `pg_trgm`-GIN-Index für Ähnlichkeitssuche. Ranking kombiniert `ts_rank` und `similarity`. Die Suche wird über eine Union-Query bzw. View über alle Objekttypen ausgeführt.

## Betrachtete Optionen
### A: Postgres-Bordmittel (gewählt)
**Pro:** kein Zusatzdienst, transaktional konsistent, lehrreich. **Contra:** weniger Features als dedizierte Suchmaschinen.
### B: Meilisearch / Elasticsearch
**Pro:** sehr gute Relevanz. **Contra:** zusätzlicher Dienst, Synchronisation nötig.

## Konsequenzen
- Später prüfen: semantische Suche über pgvector ergänzend.

## Umsetzung (Iteration 4, 2026-09-30)
**Generierte Spalten:** `search_vector` (`tsvector`, Konfiguration `simple`) auf `contacts`, `organizations` und `opportunities` sowie `search_name` (Vor- und Nachname) auf `contacts` sind *stored generated columns*. In EF Core 10 + Npgsql sind sie **Shadow Properties** mit `HasComputedColumnSql(…, stored: true)`, damit das Domänenmodell frei von Suchbelangen bleibt; EF liest sie nur (nach Insert/Update per `RETURNING`) und schreibt sie nie. Der `AuditInterceptor` überspringt Spalten mit Computed-SQL, sonst tauchten sie als Diff auf. Probleme gab es keine; zu beachten war nur, dass die Naming-Convention explizite Indexnamen überschreibt (daher `HasDatabaseName`) und dass ein zweiter Index auf `organizations.name` einen eigenen Namen braucht.

**Gewichtung und Zerlegung:** Name/Titel haben das Gewicht A, E-Mail, Website und Ort B. E-Mail-Adressen und URLs werden vor `to_tsvector` per `regexp_replace` an `@ . / : ? # = & _ + -` in Wörter zerlegt, sonst behält der Standard-Parser sie als ein einziges Token und „contoso“ fände `https://www.contoso.de` nicht.

**Anfrage:** Die Eingabe wird in Wörter aus Buchstaben und Ziffern zerlegt (`SearchTerm`); nur diese gelangen in `to_tsquery`, jedes als Präfix (`schmi:* & max:*`). Ein Datensatz trifft, wenn die Volltext-Anfrage passt **oder** die Eingabe dem Namen/Titel per `pg_trgm`-Wortähnlichkeit ähnelt (`eingabe <% name`, per GIN-Index `gin_trgm_ops`). Kontakte treffen zusätzlich über ihre Firma: Die passenden Organisations-Ids werden vorab ermittelt und als `organization_id = ANY(@ids)` angefügt. Ein `OR` über die gejointe Organisation hätte einen Sequential Scan erzwungen; so kombiniert Postgres drei Indizes per `BitmapOr` (seltener Name: 50 ms → 0,2 ms).

**Schwellwert:** Wir nutzen den Operator `<%` mit dem Standard `pg_trgm.word_similarity_threshold = 0.6` statt eines eigenen Werts. Die Wortähnlichkeit misst, welcher Anteil der Trigramme der *Eingabe* im ähnlichsten Wortausschnitt des Namens vorkommt. „Schmitt“ → „Schmidt“ erreicht 0,625 (US-13 AK3). Bei 0,5 träfe „Max“ bereits „Maria“ und „Mayer“, kurze Eingaben lieferten viel Rauschen. Präfixe deckt ohnehin die Volltextsuche ab. Ein eigener Schwellwert ginge nur per GUC (Verbindung oder Datenbank) oder mit der Funktion statt des Operators, dann aber ohne Index. Die Kehrseite: „Meier“/„Meyer“ (0,5) findet sich nicht gegenseitig; das passt zu Entscheidung 6 (Schreibvarianten erst bei Bedarf).

**Ranking:** Relevanz = `word_similarity(eingabe, name) + ts_rank(vector, query)`. Die Wortähnlichkeit (0–1) dominiert, `ts_rank` bricht Gleichstände zugunsten exakter und gewichteter Treffer. Bei Kontakten zählt die Firma mit halbem Gewicht und nur, wenn sie besser passt als der eigene Name (`GREATEST`); eine Addition ließ ähnlich klingende Firmennamen den besseren Namenstreffer verdrängen.

**Union statt drei Abfragen:** Die Command Palette fragt die drei Typen nacheinander mit je eigenem Limit ab (`Search`), statt eine Union oder View zu verwenden. So kann kein Typ die anderen verdrängen, und jede Abfrage nutzt ihre Indizes. Listen und Autocompletes verwenden dieselben Prädikate (`SearchPredicates`); ohne gewählte Sortierspalte sortieren Listen bei Suche nach Relevanz.

**Messung** (Integrationstest `PerformanceTests`, 10k Kontakte mit realistisch verteilten Namen, lokal im Median): Palette 28–46 ms, Kontaktliste 42–52 ms, Organisationen < 8 ms, also deutlich unter der NFR von 200 ms. Im ersten Datensatz hießen alle Kontakte „Vorname n Nachname n“; dann trifft jede Zeile und muss gerankt werden (110–140 ms). Das ist kein realistisches Szenario, deshalb verteilt der Datensatz die Namen jetzt realistisch.
