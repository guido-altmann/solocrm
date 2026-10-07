# ADR-012: Lizenz AGPL-3.0-or-later

**Status:** Accepted · **Datum:** 2026-10-07 · **Entscheider:** Guido Altmann

## Kontext
SoloCRM ist ein selbst gehostetes CRM, entstanden für den Eigenbedarf und als Lern- und Referenzprojekt. Der Code liegt auf GitHub und soll einsehbar und nutzbar sein. Gleichzeitig soll niemand die Software als geschlossenen, gehosteten Dienst anbieten können, ohne seine Änderungen zurückzugeben. Als Web-Anwendung wird SoloCRM nur über das Netzwerk genutzt, die klassische GPL greift dafür nicht.

## Entscheidung
- Lizenz: **GNU Affero General Public License v3.0 oder später** (SPDX: `AGPL-3.0-or-later`).
- Der Volltext liegt als `LICENSE` im Repo-Root (unverändert von <https://www.gnu.org/licenses/agpl-3.0.txt>).
- Urheber ist Guido Altmann. Fremde Beiträge werden nur mit Zustimmung zu einer Rechteeinräumung (CLA oder gleichwertig) übernommen, damit eine spätere Zweitlizenzierung möglich bleibt.
- Die Lizenz gilt für den Code dieses Repos. Der eigene Betrieb (Coolify, eigene Daten) ist davon nicht berührt; Kundendaten sind nicht Teil des Repos.
- Abhängigkeiten müssen mit AGPL-3.0 kompatibel sein (permissive Lizenzen wie MIT/Apache-2.0/BSD sind es). Neue Pakete werden dabei mitgeprüft (siehe Regel 10 in CLAUDE.md).

## Betrachtete Optionen
### Option A: MIT / Apache-2.0
**Pro:** maximal einfach, keine Hürden für Nutzer und Arbeitgeber · **Contra:** Dritte könnten das CRM ohne Gegenleistung als geschlossenen SaaS betreiben.
### Option B: AGPL-3.0-or-later (gewählt)
**Pro:** Schutz vor geschlossenem SaaS-Betrieb, Code bleibt offen, kommerzielle Zweitlizenz möglich · **Contra:** manche Firmen meiden AGPL-Code; Beiträge Dritter erfordern CLA, wenn Zweitlizenzierung offenbleiben soll.
### Option C: Proprietär / alle Rechte vorbehalten
**Pro:** volle Kontrolle · **Contra:** als Referenzprojekt nicht nutzbar, Öffnung später möglich, aber kein Mehrwert jetzt.

## Konsequenzen
- Leichter: klare Position gegenüber SaaS-Anbietern; Weiternutzung durch andere Freelancer im Self-Hosting ist erlaubt.
- Schwerer: Wer SoloCRM modifiziert und als Dienst anbietet, muss den Quelltext (§ 13) allen Nutzern anbieten. Das ist gewollt, begrenzt aber die Verbreitung in Unternehmen.
- Später prüfen: Quelltext-Header oder `SPDX-License-Identifier` pro Datei (derzeit nicht vorgesehen); Lizenzprüfung der NuGet-Abhängigkeiten in der CI; CLA-Verfahren, falls externe Beiträge kommen.
