# ADR-016: Datenanreicherung in der App über einen Foundry-Agent mit Websuche

**Status:** Accepted
**Datum:** 2026-11-24, überarbeitet 2026-11-26
**Entscheider:** Guido Altmann

## Kontext
Organisationen (später auch Kontakte) sollen per Knopfdruck mit öffentlichen Daten angereichert werden (Epic SOL-1, SPEC Kap. 9). Ergebnisse sind Vorschläge, die der Nutzer feldweise übernimmt oder verwirft; jeder übernommene Wert trägt Quelle, Zeitpunkt und Konfidenz (SPEC 9.1). Offen war, wo die Anreicherung läuft, welcher Provider zuerst kommt und über welchen Dienst die LLM-Aufrufe gehen (SOL-3).

Die erste Fassung (2026-11-24) sah als ersten Provider Website/Impressum vor: Die App ruft die Website selbst ab (mit eigenem SSRF-Schutz) und lässt ein Modell-Deployment in Azure AI Foundry nur extrahieren. Bei der Aufteilung von SOL-4 zeigte sich, dass der Abruf fremder Websites der aufwendigste und sicherheitskritischste Teil ist, aber nichts Fachliches beiträgt. Die Überarbeitung (2026-11-26) verlagert Abruf, Websuche und Modellwahl in einen Agent im Foundry Agent Service.

Randbedingungen:
- Weitere KI-Features stehen im Backlog: Zusammenfassung von Notizen (SOL-14) und semantische Suche (SOL-15). Sie laufen in jedem Fall in der App.
- Hintergrundarbeit läuft über eigene `BackgroundService`s; Hangfire kommt erst bei Bedarf (ADR-008). Die Outbox zeigt das Muster mit Claim und Lease (`FOR UPDATE SKIP LOCKED`).
- Datenschutz (SPEC 9.4): Ein Impressum enthält Namen von Geschäftsführern, also personenbezogene Daten. Es gelten Datenminimierung und, wo möglich, eine Auftragsverarbeitung mit dem Anbieter.
- Die Recherche zu OpenRouter (Kommentar in SOL-3, 2026-11-24) ergab: US-Anbieter; EU-Routing nur im Business-Tarif und mit unklarer vertraglicher Wirkung ohne Order Form; ZDR ist eine Zusage der nachgelagerten Provider, kein technischer Schutz; am 22.11.2026 wurden nach Datenabflüssen bei Inferenz-Providern drei Provider abgekoppelt und 125 Endpunkte aus der ZDR-Liste genommen.
- Die Websuche im Foundry Agent Service (Recherche 2026-11-26, siehe „Datenschutz der Websuche“) fällt nicht unter den Microsoft-Auftragsverarbeitungsvertrag.

## Entscheidung
1. **Anreicherung in der App, nicht in n8n.** Anlegen des `EnrichmentRequest`, Aufruf des Agents, Vorschläge und Übernahme sind Use Cases in `Application/Features/Enrichment/`. Es gibt kein Event `enrichment.requested` und keine REST-Endpunkte für Vorschläge.
2. **Abstraktion `IEnrichmentProvider`** in `Application`, Implementierungen in `Infrastructure/Enrichment/`. Ein Provider liefert Vorschläge (Feld, Wert, Quell-URL, Konfidenz) für ein Zielobjekt. Ein späterer n8n- oder kommerzieller Provider ist eine Zusatz-Implementierung, kein Umbau.
3. **Hintergrundverarbeitung über einen eigenen `EnrichmentProcessor`** nach dem Outbox-Muster (ADR-008): `EnrichmentRequest` mit Status `Pending` ist die persistente Queue. Ein `BackgroundService` beansprucht fällige Anfragen per Claim mit Lease (`UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING id`), ohne während des Agent-Aufrufs eine Transaktion offen zu halten. Bricht ein Prozess ab, wird die Anfrage nach Ablauf der Lease erneut verarbeitet. Hangfire wird nicht eingeführt. Der Prozessor ist eine eigene, direkt testbare Klasse; der `BackgroundService` ist nur die Zeitschleife.
4. **Erster (und vorerst einziger) Provider: Foundry-Agent mit Websuche.** Ein Agent im Foundry Agent Service sucht mit dem Web-Search-Tool (Web IQ) nach der Organisation, wertet die Treffer aus (bevorzugt Impressum und eigene Website der Firma) und antwortet strukturiert. Gesucht werden die Impressumsangaben Adresse, Telefon, E-Mail, Rechtsform, Registergericht und -nummer, Geschäftsführung und USt-IdNr. Allgemeine Informationen zur Firma (z. B. Branche, Größe, Leistungen, Gründungsjahr, Kurzbeschreibung) liefert der Agent als ein Firmenprofil in Markdown mit Quellenangaben; es wird nach Übernahme als Notiz (Activity vom Typ `Note`, Herkunft `Enrichment`) an der Organisation gespeichert. Adresse, Telefon und E-Mail sind feste Felder der Organisation (Telefon und E-Mail kommen mit SOL-49), die übrigen Impressumsangaben landen in `ExtraFields`. Die App ruft selbst keine fremden Websites ab. Ein eigener Provider Website/Impressum und ein separater Provider „LLM mit Web-Suche“ entfallen.
5. **Aufgaben der App und des Agents.** Im Agent (in Foundry konfigurierbar, ohne Deployment der App): Modell, Instructions, Tools. In der App:
   - **Schema-Hoheit:** Das JSON-Schema der Antwort ist im Code definiert. Die App gibt es pro Aufruf mit, sofern der Agent Service das zulässt; in jedem Fall validiert sie die Antwort strikt dagegen und verwirft, was nicht passt (Anfrage `Failed`).
   - **Quellenpflicht:** Ein Vorschlag wird nur gespeichert, wenn seine Quell-URL (`http`/`https`) unter den URL-Zitaten der Antwort vorkommt. Die Konfidenz liefert der Agent; Werte ohne Quelle werden verworfen.
   - **Referenzdefinition im Repo:** Instructions, Schema und Tool-Auswahl des Agents liegen als Vorlage in `docs/` (Einrichtungsanleitung), damit die Konfiguration nachvollziehbar und wiederherstellbar ist. Änderungen im Portal sollen dort nachgezogen werden.
6. **Azure-Rahmen:** Foundry-Projekt in Germany West Central; das Modell des Agents ist ein Deployment vom Typ `Standard` (regional) in derselben Ressource. `GlobalStandard` ist ausgeschlossen (Verarbeitung in jeder Azure-Region möglich), ebenso `DataZoneStandard` (EU-Zone laut Microsoft-Doku uneinheitlich definiert und ohne Vorankündigung erweiterbar). Startmodell `gpt-5-mini` (in Germany West Central als `Standard` verfügbar, Stand 2026-11-26); das Web-Search-Tool unterstützt nur Azure-OpenAI-Modelle. Vertragspartner für Agent und Modell ist Microsoft mit dessen Auftragsverarbeitungsvertrag; Prompts und Antworten werden nicht zum Training genutzt. Projekt-Endpunkt, Agent-Name und Zugangsdaten kommen aus Umgebungsvariablen (`Ai__ProjectEndpoint`, `Ai__AgentName` und die Anmeldedaten), nie ins Repo. Ohne Konfiguration ist die Anreicherung in der UI deaktiviert, statt beim Start zu scheitern.
7. **Keine dauerhaft gespeicherten Gesprächsdaten:** Jeder Aufruf ist eigenständig. Wenn möglich ohne Speicherung (`store: false`); sonst werden Conversation bzw. Response unmittelbar nach dem Lauf gelöscht. Ohne Löschung blieben sie im Basic Setup 60 Tage in einem von Microsoft verwalteten Cosmos DB. Ein Standard Setup mit eigenem Cosmos DB ist für einen Single-User überdimensioniert.
8. **Datenminimierung:** An den Agent gehen nur Firmenname und, falls vorhanden, die Domain aus `Organization.Website`, keine Notizen, Kontakte oder sonstigen CRM-Daten. In Logs stehen nur Ids, Status, Dauer, Tokenzahlen und Anzahl der Suchen, nie Suchanfragen, URLs mit Pfad oder extrahierte Werte.
9. **Kosten:** Pro Anreicherung fallen Token- und Suchkosten an. Ein Tagesdeckel `Enrichment__MaxRequestsPerDay` (Default 20) begrenzt sie.

## Datenschutz der Websuche (akzeptiertes Restrisiko)
Stand der Microsoft-Doku am 2026-11-26:
- Das Web-Search-Tool läuft, direkt am Agent eingebunden, über **Web IQ**; über eine Toolbox eingebunden über **Grounding with Bing Search**. In beiden Fällen gilt der Microsoft Products and Services Data Protection Addendum **nicht** für die an die Suche übermittelten Daten. Es gelten die Web-IQ- bzw. Bing-Nutzungsbedingungen und das Microsoft Privacy Statement; Microsoft ist dafür eigener Verantwortlicher, kein Auftragsverarbeiter.
- Die Daten können die Azure-Compliance- und Geo-Grenze verlassen. Private Endpoints und VPN-Routing greifen nicht.
- Bei Grounding with Bing gehen laut Doku nur Suchanfrage, Tool-Parameter und Ressourcen-Key an Bing. Für Web IQ gibt es keine so genaue Aufstellung.
- Die Web-IQ-Bedingungen untersagen das Cachen und Speichern von Ergebnissen, außer im eigenen Chatverlauf und als internes Arbeitsergebnis. Übernommene Vorschläge im eigenen CRM werden als internes Arbeitsergebnis gewertet.

Bewertung: Die Suchanfragen formuliert das Modell aus Firmenname und Domain; sie enthalten keine CRM-internen Daten. Ausnahme sind Einzelunternehmen, deren Firmenname ein Personenname ist; dieser ist jedoch selbst öffentlich (Impressumspflicht). Die Antworten enthalten Personendaten (Geschäftsführung); das gilt für jede Form der Anreicherung (Dritterhebung, Art. 14 DSGVO). Für ein Single-User-CRM, das öffentliche Firmendaten sucht, wird das Risiko akzeptiert. Gewählt ist Web IQ (direkt am Agent), weil es keine zusätzliche Bing-Ressource braucht; rechtlich unterscheiden sich beide Wege kaum.

## Betrachtete Optionen

### Ort der Anreicherung
#### A: In der App (gewählt)
**Pro:** Der Zustand (Anfrage, Vorschläge, Übernahme mit Audit, Herkunft für die DSGVO-Auskunft) ist ohnehin fachlich und liegt in der App. Mit xUnit und Testcontainers testbar, versioniert im Repo. **Contra:** Anbindung an den Agent Service muss gewartet werden.
#### B: In n8n (Webhook `enrichment.requested`, Rückschreiben per REST)
**Pro:** Nutzt die vorhandene Integration (Outbox, Webhooks); Workflows lassen sich ohne Deployment ändern. **Contra:** Neue REST-Endpunkte für Vorschläge; ein Kern-Feature hängt an einem externen Dienst; Workflows sind kaum automatisiert testbar. Der Vorteil „ohne Deployment änderbar“ entsteht mit dem Foundry-Agent ebenfalls.

### Hintergrundverarbeitung
#### A: Eigener `EnrichmentProcessor` nach dem Outbox-Muster (gewählt)
**Pro:** Erprobtes Muster, die Anfrage-Tabelle ist die Queue. **Contra:** Retry und Aufräumen müssen erneut gebaut werden (Vorlage existiert).
#### B: Hangfire einführen
**Pro:** Retries und Dashboard fertig. **Contra:** Zusätzliche Tabellen und Abhängigkeit für eine einzige neue Jobart; ADR-008 sieht Hangfire erst bei mehreren Jobarten vor.

### Erster Provider
#### A: Foundry-Agent mit Websuche (gewählt)
**Pro:** Kein eigener Abruf fremder Websites, damit kein SSRF-Schutz in der App; findet auch Firmen ohne gepflegte Website oder mit Impressum hinter JavaScript; liefert auch Branche, Größe und Beschreibung; Modell, Instructions und Tools in Foundry änderbar. **Contra:** Websuche außerhalb des Auftragsverarbeitungsvertrags (siehe oben); das Modell wählt die Quellen selbst, daher höheres Halluzinationsrisiko (begrenzt durch Quellenpflicht und Schema-Validierung); die Agent-Konfiguration liegt außerhalb des Repos und ist nicht automatisiert testbar.
#### B: Website/Impressum, Abruf durch die App, Modell extrahiert nur (erste Fassung)
**Pro:** Alles unter einem Auftragsverarbeitungsvertrag; die Quelle ist eindeutig, das Modell erfindet keine Fakten. **Contra:** Die App ruft URLs aus Daten ab und braucht einen eigenen SSRF-Schutz (private Adressen, Redirects, DNS-Rebinding, Größenlimit); liefert nichts ohne Website oder bei Impressum hinter JavaScript; Felder wie Branche oder Größe fehlen meist.
#### C: Kommerzielle APIs (Firmendatenbanken, Handelsregister)
**Pro:** Strukturierte, geprüfte Daten. **Contra:** Kosten- und Lizenzprüfung; für einen Freelancer mit wenigen Anreicherungen pro Monat überdimensioniert.

### Inferenz
#### A: Azure AI Foundry, EU-Region (gewählt)
**Pro:** Agent und Modell bei einem Vertragspartner mit Auftragsverarbeitungsvertrag und Verarbeitung in Germany West Central; kein Routing über wechselnde Dritt-Provider; passt zur vorhandenen Microsoft-Anbindung (ADR-013). **Contra:** Einrichtung (Subscription, Ressource, Projekt, Deployment, Agent) aufwendiger; kleinere Modellauswahl als bei einem Router.
#### B: OpenRouter
**Pro:** Sehr breite Modellauswahl mit einem Key. **Contra:** US-Anbieter; EU-Routing nur im Business-Tarif, ohne Web-Suche und mit unklarer Vertragswirkung; ZDR hängt an den Zusagen der Provider, deren Bruch im November 2026 belegt ist.
#### C: LiteLLM (selbst gehostet)
**Pro:** Volle Kontrolle über Routing, Budgets und Logging. **Contra:** Ein weiterer Dienst im Betrieb, Upstream-Anbieter und -Verträge braucht es trotzdem. Für einen Single-User ohne Nutzen.

## Konsequenzen
- Leichter: Kein Abruf fremder Websites in der App, kein eigener SSRF-Schutz. Modell und Suchverhalten lassen sich in Foundry anpassen. Neue Enrichment-Provider bleiben Zusatz-Implementierungen.
- Schwerer: Die Qualität hängt an einer Konfiguration außerhalb des Repos; die Referenzdefinition in `docs/` muss gepflegt werden. Tests der App laufen gegen einen Fake-Provider; das Verhalten des Agents selbst wird nur manuell geprüft.
- **SOL-14 und SOL-15** sind nicht mehr Teil dieser Entscheidung. Die frühere Festlegung auf `Microsoft.Extensions.AI` (`IChatClient`) als gemeinsame LLM-Anbindung entfällt; SOL-14 und SOL-15 entscheiden ihre Anbindung selbst (direktes Modell-Deployment oder Agent).
- **Missbrauchsüberwachung bei Microsoft (akzeptiertes Restrisiko):** Jeder Prompt wird in Echtzeit klassifiziert. Gespeichert werden nur markierte Prompts und Antworten, in der Geografie der Ressource; Zugriff nur für autorisierte Microsoft-Mitarbeitende mit Just-in-Time-Freigabe, bei EWR-Ressourcen mit Sitz im EWR. Eine Aufbewahrungsdauer nennt die Doku nicht. Akzeptiert, weil Firmennamen und Firmendaten praktisch nie markiert werden.
- **Bei Umsetzung (SOL-41) prüfen:**
  - SDK und Pakete für den Aufruf eines Agents aus .NET (Agent-Referenz über die Responses API); Aufnahme in `Directory.Packages.props` nach Rückfrage (Regel 10).
  - Anmeldung: Unterstützt der Agent Service einen API-Key, oder ist Entra ID nötig (Service Principal mit passender Rolle, `Azure.Identity`, nach Rückfrage)?
  - Ob das Antwortschema pro Aufruf mitgegeben werden kann oder in der Agent-Definition stehen muss.
  - Ob `store: false` mit Agents möglich ist oder gelöscht werden muss.
  - Verfügbarkeit von Agent Service und Web-Search-Tool in Germany West Central sowie die Unterstützung von `gpt-5-mini` durch das Web-Search-Tool im Portal gegenprüfen.
- Datenschutz-Doku (`docs/datenschutz.md`): Anreicherung als Dritterhebung (Art. 14), Microsoft als Auftragsverarbeiter für Agent und Modell mit Verarbeitung in Germany West Central, Websuche über Web IQ außerhalb des Auftragsverarbeitungsvertrags, Missbrauchsüberwachung wie oben, Herkunft in der DSGVO-Auskunft (SOL-5).
- Später prüfen: automatische Anreicherung bei Anlage (SOL-47); Anreicherung von Kontakten.
