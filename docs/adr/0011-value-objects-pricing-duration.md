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
