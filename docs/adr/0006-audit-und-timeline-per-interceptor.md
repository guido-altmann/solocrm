# ADR-006: Audit und Timeline über EF-Core-Interceptors

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Jede Änderung soll nachvollziehbar sein und relevante Änderungen (z. B. Stage-Wechsel) sollen in der Timeline erscheinen – ohne dass jeder Handler daran denken muss.

## Entscheidung
Ein `AuditInterceptor` (`SaveChangesInterceptor`) liest vor dem Speichern den ChangeTracker, erzeugt `AuditEntry`-Datensätze mit Feld-Diffs (jsonb) und speichert sie in derselben Transaktion. Welche Felder in der Timeline sichtbar sind, steuert eine Konfiguration (SPEC 2.5).

## Betrachtete Optionen
### A: EF-Interceptor (gewählt)
**Pro:** zentral, vollständig, lehrreich. **Contra:** Massen-Updates via `ExecuteUpdate` umgehen ihn (bewusst vermeiden oder manuell auditieren).
### B: DB-Trigger
**Pro:** lückenlos. **Contra:** Logik in SQL versteckt, schwer testbar.
### C: Explizites Logging im Handler
**Pro:** einfach. **Contra:** fehleranfällig, wird vergessen.

## Konsequenzen
- Regel: `ExecuteUpdate`/`ExecuteDelete` nur mit expliziter Audit-Behandlung.

## Umsetzung der Timeline (Iteration 3, 2026-09-29)
**Aggregation** (`GetTimeline`, SPEC 2.5): Zuerst wird der *Scope* ermittelt, also die Ids aller Datensätze, deren Einträge in die Timeline gehören. Für eine Anfrage ist das nur sie selbst. Ein Kontakt bringt zusätzlich die Anfragen mit, bei denen er Ansprechpartner ist. Eine Organisation bringt ihre aktuellen Kontakte und die Anfragen mit, bei denen sie Endkunde oder Vermittler ist. Vier Quellen werden dann getrennt abgefragt: Activities, angelegte Tasks, erledigte Tasks und AuditEntries. Jede Quelle liefert höchstens `Limit + 1` Zeilen, erst danach werden die Quellen im Speicher gemischt. Einträge aus dem Umfeld bekommen ein „via …“ mit Link. Eine Activity, die direkt und zusätzlich über das Umfeld verknüpft ist, erscheint nur einmal und ohne „via“.

**Paging per Cursor** statt Offset: Die Position ist `(Zeitpunkt, Quelle, Id)`, absteigend sortiert. Innerhalb einer Quelle filtert ein Row-Value-Vergleich (`EF.Functions.LessThan(ValueTuple.Create(occurred_at, id), …)` → `(occurred_at, id) < (@p1, @p2)`). Bei gleichem Zeitstempel entscheidet die Rangfolge der Quellen, deshalb bleibt „Mehr laden“ auch bei identischen Zeitstempeln lückenlos und frei von Dubletten.

**Audit-Filter:** Welche Felder sichtbar sind, steht zentral in `TimelineAuditFilter.VisibleFields` (Stage, Pricing.*, Duration.*, OrganizationId, IsArchived). `Created` erscheint als „Angelegt“; in der Organisations-Timeline fehlt es für die Kontakte (nicht für Anfragen). `Archived` erscheint als „Archiviert“, ein `Updated` mit `IsArchived: true → false` als „Wiederhergestellt“. Übrige `Updated` ohne sichtbares Feld werden übersprungen. Weil ein Filter auf den jsonb-Inhalt in SQL schwer lesbar wäre, filtert der Handler im Speicher und liest die AuditEntries dafür in Blöcken von 100, bis eine Seite voll ist.

**Lesbare Darstellung:** Stage- und Organisations-Ids werden zu Namen aufgelöst; gelöschte Phasen erscheinen als „(gelöscht)“. Für Complex Types enthält der Diff nur die geänderten Mitglieder (z. B. nur `Pricing.Amount`). Den vollständigen Preis bzw. die Laufzeit vor und nach der Änderung rekonstruiert der Handler, indem er die Audit-Historie der betroffenen Anfrage ab `Created` nachspielt. So entsteht „Preis: 95 €/h → 105 €/h“ ohne Änderung am Interceptor, und das gilt auch für Bestandsdaten.

**Messung** (Integrationstest `PerformanceTests`, 10k Kontakte, 50k Activities, 30k AuditEntries, lokal im Median): Organisations-Timeline ≈ 6–14 ms, Kontakt-Timeline ≈ 4–5 ms, „Heute“ ≈ 34 ms, also deutlich unter der NFR von 200 ms. Genutzt werden die Indizes `(contact_id|organization_id|opportunity_id, occurred_at)` auf `activities` und `(entity_id, occurred_at)` auf `audit_entries`.
