# ADR-011: Preismodell und Laufzeit als Value Objects (EF Core Complex Types)

**Status:** Accepted · **Datum:** 2026-09-25 · **Entscheider:** Guido Altmann

## Kontext
Anfragen werden mit unterschiedlichen Preismodellen angeboten: Stundensatz (Standard), Tagessatz, Festpreis und monatlicher Retainer. Laufzeiten werden in Tagen, Wochen oder Monaten angegeben. Einzelne lose Felder (`DailyRate`, `DurationMonths`) bilden das nicht ab und verteilen Berechnungslogik über die Anwendung.

## Entscheidung
- `Pricing(PricingModel Model, decimal Amount, string Currency)` und `Duration(int Value, DurationUnit Unit)` als unveränderliche Records im Domain-Projekt, mit Validierung im Konstruktor bzw. einer Factory (`Amount > 0`, `Value > 0`, gültiger ISO-Währungscode).
- Persistenz als **EF Core Complex Types** (`ComplexProperty`); beide sind optional (nullable Complex Types, verfügbar ab EF Core 10).
- Die Berechnung von `EstimatedValue` und `MonthlyRecurringValue` liegt als Methode in `Opportunity` bzw. einem Domain-Service `OpportunityValuation`, der die Einstellungen (`HoursPerDay`, `RetainerValuationMonths`) als Parameter erhält.

## Betrachtete Optionen
### A: Value Objects als Complex Types (gewählt)
**Pro:** fachlich ausdrucksstark, Berechnung an einer Stelle, gut unit-testbar, keine Joins (Spalten liegen in `opportunities`), guter Lerneffekt (DDD-Baustein). **Contra:** Complex Types haben Einschränkungen (z. B. keine eigene Identität, eingeschränkte Abfrage-Features bei älteren EF-Versionen).
### B: Owned Entities
**Pro:** länger etabliert. **Contra:** semantisch Entitäten mit Schatten-Schlüssel, eher für Tabellen-Splitting gedacht; Microsoft empfiehlt für Wertobjekte inzwischen Complex Types.
### C: Lose Felder direkt in `Opportunity`
**Pro:** am einfachsten. **Contra:** Ungültige Kombinationen möglich, Logik verstreut.
### D: JSONB-Spalte
**Pro:** flexibel. **Contra:** Summen und Filter über Beträge (Pipeline, Dashboard) umständlicher.

## Konsequenzen
- Leichter: neue Preismodelle ergänzen (Enum + Formel + Tests), Anzeigeformatierung zentral (`Pricing.ToDisplayString()`).
- Schwerer: Nullable Complex Types im Walking Skeleton / Iteration 2 früh verifizieren. Falls Probleme auftreten, Fallback auf Owned Entities (Option B) und diesen ADR aktualisieren.
- Später prüfen: Währungsumrechnung, Satz-Varianten (Remote/Vor-Ort).

## Erfahrungen (Iteration 2, 2026-09-29)
Verifiziert mit EF Core 10.0.12 und Npgsql 10.0.3: Nullable Complex Types funktionieren für Migration, Speichern, Laden, `null` und den Wechsel zwischen Wert und `null` (Integrationstests `OpportunityPersistenceTests`). Der Fallback auf Owned Entities ist **nicht** nötig. Die Spalten liegen wie geplant in `opportunities` (`pricing_model`, `pricing_amount`, `pricing_currency`, `duration_value`, `duration_unit`, alle nullable) und werden zusätzlich durch Check-Constraints (`pricing_amount > 0`, `duration_value > 0`) abgesichert.

Stolpersteine:
- **Nur mappbare Properties:** Reine Getter (`{ get; }`) werden per Konvention nicht gemappt; die Konstruktorbindung scheitert dann. Lösung: `{ get; private init; }` – die Records bleiben nach außen unveränderlich.
- **Change Tracking bei `null`:** Ist der Complex Type `null`, liefert der ChangeTracker für die inneren Properties CLR-Defaults (`0`, `PricingModel.Hourly`, `DurationUnit.Days`) statt `null`. `IsModified` ist dann für Properties mit unverändertem Default `false`, und `ComplexPropertyEntry` besitzt kein `OriginalValue`. Der `AuditInterceptor` ermittelt die ursprüngliche Null-Belegung deshalb über `entry.OriginalValues.ToObject()` und vergleicht die formatierten Werte, statt sich auf `IsModified` zu verlassen. Mitglieder eines `null`-Complex-Types werden im Audit als `null` protokolliert (Feldnamen `Pricing.Amount` usw.).
- **Anzeige unabhängig von ICU:** `Pricing.ToDisplayString()` formatiert mit einer festen deutschen `NumberFormatInfo`; die ISO-4217-Prüfung nutzt eine feste Code-Liste statt Kulturdaten. So sind Tests und Container-Ausgabe identisch.
