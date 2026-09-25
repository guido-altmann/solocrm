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
