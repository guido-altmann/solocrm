# ADR-016: Datenanreicherung in der App, Inferenz über Azure AI Foundry

**Status:** Accepted
**Datum:** 2026-11-24
**Entscheider:** Guido Altmann

## Kontext
Organisationen (später auch Kontakte) sollen per Knopfdruck mit öffentlichen Daten angereichert werden (Epic SOL-1, SPEC Kap. 9). Ergebnisse sind Vorschläge, die der Nutzer feldweise übernimmt oder verwirft; jeder übernommene Wert trägt Quelle, Zeitpunkt und Konfidenz (SPEC 9.1). Offen war, wo die Anreicherung läuft, welcher Provider zuerst kommt und über welchen Dienst die LLM-Aufrufe gehen (SOL-3).

Randbedingungen:
- Weitere KI-Features stehen im Backlog: Zusammenfassung von Notizen (SOL-14) und semantische Suche (SOL-15). Sie laufen in jedem Fall in der App und brauchen ebenfalls eine LLM-Anbindung.
- Hintergrundarbeit läuft über eigene `BackgroundService`s; Hangfire kommt erst bei Bedarf (ADR-008). Die Outbox zeigt das Muster mit Claim und Lease (`FOR UPDATE SKIP LOCKED`).
- Datenschutz (SPEC 9.4): Ein Impressum enthält Namen von Geschäftsführern, also personenbezogene Daten. SOL-14 würde interne Notizen senden. Es gelten Datenminimierung und eine Auftragsverarbeitung mit dem LLM-Anbieter.
- Die Recherche zu OpenRouter (Kommentar in SOL-3, 2026-11-24) ergab: US-Anbieter; EU-Routing nur im Business-Tarif und mit unklarer vertraglicher Wirkung ohne Order Form; ZDR ist eine Zusage der nachgelagerten Provider, kein technischer Schutz; am 22.11.2026 wurden nach Datenabflüssen bei Inferenz-Providern drei Provider abgekoppelt und 125 Endpunkte aus der ZDR-Liste genommen.

## Entscheidung
1. **Anreicherung in der App, nicht in n8n.** Anlegen des `EnrichmentRequest`, Abruf, Extraktion, Vorschläge und Übernahme sind Use Cases in `Application/Features/Enrichment/`. Es gibt kein Event `enrichment.requested` und keine REST-Endpunkte für Vorschläge.
2. **Abstraktion `IEnrichmentProvider`** in `Application`, Implementierungen in `Infrastructure/Enrichment/`. Ein Provider liefert Vorschläge (Feld, Wert, Quell-URL, Konfidenz) für ein Zielobjekt. Ein späterer n8n- oder kommerzieller Provider ist eine Zusatz-Implementierung, kein Umbau.
3. **Hintergrundverarbeitung über einen eigenen `EnrichmentProcessor`** nach dem Outbox-Muster (ADR-008): `EnrichmentRequest` mit Status `Pending` ist die persistente Queue. Ein `BackgroundService` beansprucht fällige Anfragen per Claim mit Lease (`UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING id`), ohne während der HTTP- und LLM-Aufrufe eine Transaktion offen zu halten. Bricht ein Prozess ab, wird die Anfrage nach Ablauf der Lease erneut verarbeitet. Hangfire wird nicht eingeführt. Der Prozessor ist eine eigene, direkt testbare Klasse; der `BackgroundService` ist nur die Zeitschleife.
4. **Erster Provider: Website/Impressum.** Die App ruft die Website der Organisation (`Organization.Website`) selbst ab, findet die Impressum-Seite und lässt das LLM nur den Seitentext gegen ein festes JSON-Schema extrahieren (Adresse, Rechtsform, Registergericht und -nummer, Geschäftsführung, USt-IdNr.). Die Quell-URL ist die abgerufene Seite. Es gibt keine Web-Suche beim LLM-Anbieter. „LLM mit Web-Suche“ und weitere Provider folgen bei Bedarf.
5. **LLM-Abstraktion `Microsoft.Extensions.AI` (`IChatClient`)** mit `Microsoft.Extensions.AI.OpenAI` als Adapter. Dieser bringt das offizielle `OpenAI`-SDK mit, das die v1-API von Azure direkt anspricht: `new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri("https://<ressource>.openai.azure.com/openai/v1/") }).GetChatClient("<deployment>").AsIChatClient()`. Als Modellname dient der Deployment-Name. `Azure.AI.OpenAI` wird nicht gebraucht. Strukturierte Antworten per JSON-Schema. Die Anbindung gilt auch für SOL-14; SOL-15 klärt Embeddings separat. Beide Pakete sind freigegeben (Regel 10) und kommen mit der Umsetzung (SOL-4) in `Directory.Packages.props`.
6. **Inferenz über Azure AI Foundry, Deployment-Typ `Standard` (regional) in Sweden Central.** Bei diesem Typ werden Prompts und Antworten nur in der gewählten Azure-Geografie verarbeitet; gespeicherte Daten bleiben bei allen Typen in der Geografie. `GlobalStandard` ist ausgeschlossen (Verarbeitung in jeder Azure-Region möglich), ebenso `DataZoneStandard` (EU-Zone laut Microsoft-Doku uneinheitlich definiert, teils inklusive Norwegen und Schweiz, und ohne Vorankündigung erweiterbar). Germany West Central bietet regional keine Chat-Modelle (Stand 2026-10-19). Startmodell ist `gpt-4.1-mini`, Ausweichoption `gpt-4.1-nano` oder `gpt-4o-mini`; alle drei sind regional in Sweden Central verfügbar. Vertragspartner ist Microsoft mit dessen Auftragsverarbeitungsvertrag; laut Microsoft sind Prompts und Antworten weder für OpenAI noch für andere Modellanbieter zugänglich und werden nicht zum Training genutzt. Endpunkt, Deployment-Name und API-Key kommen aus Umgebungsvariablen (`Ai__Endpoint`, `Ai__Deployment`, `Ai__ApiKey`), nie ins Repo. Anmeldung per API-Key; Entra-Anmeldung (`Azure.Identity`) erst nach Rückfrage. Ohne Konfiguration ist die Anreicherung in der UI deaktiviert, statt beim Start zu scheitern.
7. **Nur zustandslose Aufrufe:** Chat Completions ohne zustandsbehaftete Funktionen (Responses API mit gespeicherten Antworten, Assistants/Threads, Stored Completions, Files, Batch). Laut Microsoft sind die Modelle selbst zustandslos; gespeichert wird nur bei diesen Funktionen.
8. **Datenminimierung:** An das LLM gehen nur der abgerufene Seitentext (auf eine Maximallänge gekürzt) und der Firmenname, keine Notizen und keine CRM-internen Daten. In Logs stehen nur Ids, Status, Dauer und Tokenzahlen, nie URLs mit Pfad, Seiteninhalt oder extrahierte Werte.
9. **Abruf fremder Websites mit SSRF-Schutz.** Anders als bei Webhooks (ADR-010, F-14) ruft die App hier URLs ab, die aus Daten stammen (Import, API, Eingabe). Deshalb: nur `http`/`https` auf Standard-Ports; nach der DNS-Auflösung keine privaten, Loopback-, Link-Local- oder Multicast-Adressen (Prüfung im `SocketsHttpHandler.ConnectCallback`, damit DNS-Rebinding nicht greift); höchstens 3 Redirects, jedes Ziel erneut geprüft; Timeout 10 s; Antwortgröße begrenzt (z. B. 1 MB); nur `text/html`. Ein eigener `User-Agent`, der SoloCRM als Client nennt, ohne persönliche Daten.

## Betrachtete Optionen

### Ort der Anreicherung
#### A: In der App (gewählt)
**Pro:** Der Zustand (Anfrage, Vorschläge, Übernahme mit Audit, Herkunft für die DSGVO-Auskunft) ist ohnehin fachlich und liegt in der App. Eine LLM-Anbindung für SOL-14/SOL-15 wird sowieso gebraucht. Mit xUnit und Testcontainers testbar, versioniert im Repo. **Contra:** KI- und Abruflogik liegen im Code und müssen gewartet werden; der SSRF-Schutz muss selbst gebaut werden.
#### B: In n8n (Webhook `enrichment.requested`, Rückschreiben per REST)
**Pro:** Nutzt die vorhandene Integration (Outbox, Webhooks); KI-Logik bleibt außerhalb der App; Workflows lassen sich ohne Deployment ändern. **Contra:** Neue REST-Endpunkte für Vorschläge; ein Kern-Feature hängt an einem externen Dienst; Workflows sind kaum automatisiert testbar; zwei getrennte LLM-Anbindungen (n8n und App für SOL-14). Bleibt als möglicher späterer `IEnrichmentProvider`.

### Hintergrundverarbeitung
#### A: Eigener `EnrichmentProcessor` nach dem Outbox-Muster (gewählt)
**Pro:** Erprobtes Muster, keine zusätzlichen Tabellen, die Anfrage-Tabelle ist die Queue. **Contra:** Retry und Aufräumen müssen erneut gebaut werden (Vorlage existiert).
#### B: Hangfire einführen
**Pro:** Retries und Dashboard fertig. **Contra:** Zusätzliche Tabellen und Abhängigkeit für eine einzige neue Jobart; ADR-008 sieht Hangfire erst bei mehreren Jobarten vor.

### Erster Provider
#### A: Website/Impressum (gewählt)
**Pro:** Im DACH-Raum gesetzlich vorgeschrieben und ergiebig; die Quelle ist eindeutig; das LLM extrahiert nur, es erfindet keine Fakten; keine Web-Suche beim Anbieter nötig. **Contra:** Liefert nichts ohne Website oder bei Impressum hinter JavaScript; Felder wie Branche oder Größe fehlen meist.
#### B: LLM mit Web-Suche
**Pro:** Flexibel, auch ohne eigene Website. **Contra:** Halluzinationsrisiko; Suchanfragen verlassen den Vertragsrahmen des Modell-Deployments (bei OpenRouter z. B. gilt ZDR nicht für die Web-Suche). Später als zweiter Provider.
#### C: Kommerzielle APIs (Firmendatenbanken, Handelsregister)
**Pro:** Strukturierte, geprüfte Daten. **Contra:** Kosten- und Lizenzprüfung; für einen Freelancer mit wenigen Anreicherungen pro Monat überdimensioniert.

### Inferenz
#### A: Azure AI Foundry, EU-Region (gewählt)
**Pro:** Ein Vertragspartner mit Auftragsverarbeitungsvertrag und EU-Verarbeitung; kein Routing über wechselnde Dritt-Provider; passt zur vorhandenen Microsoft-Anbindung (ADR-013). **Contra:** Einrichtung (Subscription, Ressource, Deployment) aufwendiger; kleinere Modellauswahl als bei einem Router.
#### B: OpenRouter
**Pro:** Sehr breite Modellauswahl mit einem Key; minimaler Einrichtungsaufwand. **Contra:** US-Anbieter; EU-Routing nur im Business-Tarif, ohne Web-Suche und mit unklarer Vertragswirkung; ZDR hängt an den Zusagen der Provider, deren Bruch im November 2026 belegt ist. Bleibt über `IChatClient` als reine Konfigurationsoption für unkritische Fälle möglich.
#### C: LiteLLM (selbst gehostet)
**Pro:** Volle Kontrolle über Routing, Budgets und Logging. **Contra:** Ein weiterer Dienst im Betrieb, Upstream-Anbieter und -Verträge braucht es trotzdem; die Abstraktion leistet bereits `Microsoft.Extensions.AI`. Für einen Single-User ohne Nutzen.

## Konsequenzen
- Leichter: Eine LLM-Anbindung für alle KI-Features; der Anbieter ist über `IChatClient` austauschbar. Neue Enrichment-Provider sind Zusatz-Implementierungen.
- Schwerer: Die App ruft fremde Websites ab; der SSRF-Schutz ist sicherheitskritisch und braucht eigene Tests (private Adressen, Redirect auf interne Ziele, DNS-Rebinding, Größenlimit).
- Kosten: Pro Anreicherung fallen Tokenkosten bei Azure an; ein Kostendeckel (max. Anfragen pro Tag, Konfiguration) gehört in SOL-4.
- **Missbrauchsüberwachung bei Microsoft (akzeptiertes Restrisiko):** Jeder Prompt wird in Echtzeit klassifiziert. Gespeichert werden nur Prompts und Antworten, die dabei markiert werden oder zu einem Missbrauchsmuster gehören, und zwar in der Geografie der Ressource. Zugriff haben nur autorisierte Microsoft-Mitarbeitende über Secure Access Workstations mit Just-in-Time-Freigabe, bei EWR-Ressourcen mit Sitz im EWR. Eine Aufbewahrungsdauer nennt die Doku nicht (die oft zitierten 30 Tage stehen dort nicht). Eine Befreiung („Modified Abuse Monitoring“) gibt es nur als Limited Access auf Antrag; für Modelle ab dem 01.11.2026 läuft sie aus. Akzeptiert, weil Impressumstexte praktisch nie markiert werden und die Ausnahme nur bei Verdacht greift. Bei SOL-14 (Notizen) erneut bewerten.
- **Bei Umsetzung (SOL-4) prüfen:** strukturierte Antworten per JSON-Schema mit dem gewählten Deployment (in der Recherche nicht auf den Microsoft-Seiten belegt); Modellverfügbarkeit im Portal gegenprüfen, da die Regionstabellen sich ändern.
- Datenschutz-Doku (`docs/datenschutz.md`): Anreicherung als Dritterhebung (Art. 14), Microsoft als Auftragsverarbeiter mit Verarbeitung in Sweden Central, Missbrauchsüberwachung wie oben, Herkunft in der DSGVO-Auskunft (SOL-5).
- Später prüfen: zweiter Provider „LLM mit Web-Suche“; automatische Anreicherung bei Anlage; Anreicherung von Kontakten.
