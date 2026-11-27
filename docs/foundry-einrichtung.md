# Datenanreicherung einrichten (Azure AI Foundry)

> Hintergrund und Entscheidungen: ADR-016. Die Anbindung ist bisher nur gegen die Microsoft-Doku umgesetzt, nicht gegen einen echten Agent geprüft; die Menübezeichnungen im Portal können leicht abweichen.

Die Anreicherung ruft einen Agent im Azure AI Foundry Agent Service auf. Die App meldet sich dort mit `DefaultAzureCredential` an. Diese Anleitung beschreibt, wie du Projekt, Modell und Agent in Foundry anlegst, wie sich die App anmeldet und welche Einstellungen sie braucht.

**Verantwortung:** Modell, Standort und Datenverarbeitung hängen von deiner Konfiguration in Foundry ab. SoloCRM kann sie nicht prüfen oder zusichern; sie liegen beim Betreiber von SoloCRM und der Foundry-Instanz (`docs/datenschutz.md`).

## 0. Projekt, Modell und Agent in Foundry anlegen
1. Im [Foundry-Portal](https://ai.azure.com) ein **Projekt** anlegen (bzw. ein vorhandenes nutzen). Als Region ist Germany West Central vorgesehen (ADR-016); ob dein Abonnement dort Agent Service und Web Search anbietet, siehst du im Portal.
2. **Modell-Deployment** anlegen: `gpt-5-mini`, Bereitstellungstyp **Standard** (kein Global). Für `gpt-5`-Modelle kann eine Registrierung nötig sein.
3. **Agent** anlegen (Agents → Neuer Agent) mit Namen `company-enrichment` (der Name kommt unten in `Ai__AgentName`):
   - **Modell:** das Deployment aus Schritt 2
   - **Instructions:** Text aus der Referenzdefinition unten
   - **Tool:** **Web Search** hinzufügen (läuft über Web IQ, siehe Datenschutz)
   - Keine Wissensquellen, kein Memory, keine weiteren Tools
4. Im Playground testen, z. B. mit „Firma: Beispiel GmbH, Domain: beispiel.de“. Die Antwort muss JSON sein und Quellen enthalten.

### Referenzdefinition des Agents
Diese Vorlage ist die versionierte Soll-Konfiguration. Ändere sie hier, wenn du den Agent in Foundry änderst.

| Einstellung | Wert |
|---|---|
| Name | `company-enrichment` |
| Modell | `gpt-5-mini` (Standard-Deployment) |
| Tools | Web Search |
| Wissen, Memory | keine |
| Antwortschema | steht **nicht** im Agent: die App schickt es mit jeder Anfrage mit und prüft die Antwort selbst (`EnrichmentSchema.cs`) |

**Instructions:**
```text
Du recherchierst öffentliche Firmendaten für ein CRM.

- Nutze die Websuche. Bevorzuge das Impressum und die eigene Website der Firma, danach das Handelsregister und seriöse Firmenverzeichnisse.
- Erfinde nichts. Ist ein Wert nicht belegt, lasse ihn leer (null).
- Gib zu jedem Wert die Seiten an, die du tatsächlich gefunden und gelesen hast.
- Recherchiere nur Firmendaten. Keine privaten Profile, keine Daten zu Privatpersonen außer Geschäftsführern aus dem Impressum.
- Antworte genau im Format, das die Anfrage vorgibt, ohne zusätzlichen Text.
```

Die Anfrage der App enthält Firmenname, Domain, Antwortformat (JSON-Schema) und die Regeln für Quellen und Konfidenz. Mehr sendet die App nicht.

## 1. App in Entra registrieren (Service Principal)
1. Im [Entra Admin Center](https://entra.microsoft.com) → **Entra Id → App-Registrierungen → Neue Registrierung**.
2. **Name:** z. B. „SoloCRM Anreicherung“. Eine eigene Registrierung hält die Rechte getrennt vom E-Mail-Logging.
3. **Unterstützte Kontotypen:** „Nur Konten in diesem Organisationsverzeichnis“. Eine Umleitungs-URI ist nicht nötig.
4. Registrieren. Auf der Übersicht stehen **Anwendungs-ID (Client)** und **Verzeichnis-ID (Mandant)**. Das sind `AZURE_CLIENT_ID` und `AZURE_TENANT_ID`.

## 2. Client-Secret erzeugen
**Zertifikate & Geheimnisse → Neuer geheimer Clientschlüssel**. Den **Wert** (nicht die Geheimnis-ID) sofort kopieren; er wird nur einmal angezeigt. Das ist `AZURE_CLIENT_SECRET`. Notiere dir das Ablaufdatum: Danach muss ein neuer Schlüssel erzeugt und in Coolify ausgetauscht werden.

## 3. Zugriff auf das Foundry-Projekt geben
Im [Azure-Portal](https://portal.azure.com) bei der Foundry-Ressource bzw. dem Projekt: **Zugriffssteuerung (IAM) → Rollenzuweisung hinzufügen**, Rolle **Azure AI User**, zugewiesen an die App-Registrierung aus Schritt 1. Ohne diese Rolle gelingt die Anmeldung, der Aufruf des Agents scheitert aber mit 401/403.

## 4. Umgebungsvariablen setzen
| Variable | Wert |
|---|---|
| `Ai__ProjectEndpoint` | Projekt-Endpunkt aus dem Foundry-Portal, `https://<ressource>.services.ai.azure.com/api/projects/<projekt>` |
| `Ai__AgentName` | Name des Agents im Projekt (`company-enrichment`) |
| `Enrichment__MaxRequestsPerDay` | optional: Tageslimit für Anreicherungen, Default 20 (weitere Optionen: `deploy/coolify.md`) |
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` | Werte aus Schritt 1 und 2 |

Sind `Ai__ProjectEndpoint` und `Ai__AgentName` nicht beide gesetzt, ist die Anreicherung in der UI deaktiviert; die App startet trotzdem. Die Anmeldung wird beim Start nicht geprüft: Fehlen die `AZURE_*`-Werte oder die Rolle, scheitert erst der Lauf, und die Anfrage steht auf „Fehlgeschlagen“ (Fehlertext am Request).

In Coolify setzt du die Variablen wie in `deploy/coolify.md` beschrieben. Die Werte gehören nie ins Repo.

## 5. Ausprobieren
Organisation öffnen → Karte **Anreicherung** → „Anreichern“. Nach ein bis zwei Minuten erscheinen die Vorschläge. Schlägt der Lauf fehl, steht der Grund in der Karte. Prüfe beim ersten Lauf außerdem:
- Die Antwort enthält gültiges JSON mit Quellen (sonst steht „fehlgeschlagen“ in der Karte).
- Bei Microsoft bleibt nichts gespeichert (`store: false`), z. B. keine Responses oder Conversations im Portal.

## Lokal entwickeln
Lokal brauchst du die `AZURE_*`-Werte nicht: Mit `az login` und deinem eigenen Konto funktioniert `DefaultAzureCredential`, wenn dein Konto die Rolle **Azure AI User** auf dem Projekt hat. `Properties/launchSettings.json` setzt dafür `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, damit nur die Azure-CLI-Anmeldung versucht wird; sonst probiert die Kette weitere Anmeldearten und der Aufruf kann sehr lange hängen. `Ai__ProjectEndpoint` und `Ai__AgentName` setzt du per `dotnet user-secrets` (Projekt `src/SoloCrm.Web`).
