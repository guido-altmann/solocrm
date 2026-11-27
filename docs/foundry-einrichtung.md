# Datenanreicherung einrichten (Azure AI Foundry)

> Hintergrund und Entscheidungen: ADR-016. Die Anbindung ist bisher nur gegen die Microsoft-Doku umgesetzt, nicht gegen einen echten Agent geprüft; die Menübezeichnungen im Portal können leicht abweichen. Die Anleitung zum Agent selbst (Modell-Deployment, Web-Search-Tool, Referenzdefinition) folgt mit SOL-46.

Die Anreicherung ruft einen Agent im Azure AI Foundry Agent Service auf. Die App meldet sich dort mit `DefaultAzureCredential` an. Diese Anleitung beschreibt die Anmeldung und die beiden App-Einstellungen.

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
| `Ai__AgentName` | Name des Agents im Projekt |
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` | Werte aus Schritt 1 und 2 |

Sind `Ai__ProjectEndpoint` und `Ai__AgentName` nicht beide gesetzt, ist die Anreicherung in der UI deaktiviert; die App startet trotzdem. Die Anmeldung wird beim Start nicht geprüft: Fehlen die `AZURE_*`-Werte oder die Rolle, scheitert erst der Lauf, und die Anfrage steht auf „Fehlgeschlagen“ (Fehlertext am Request).

In Coolify setzt du die Variablen wie in `deploy/coolify.md` beschrieben. Die Werte gehören nie ins Repo.

## Lokal entwickeln
Lokal brauchst du die `AZURE_*`-Werte nicht: Mit `az login` und deinem eigenen Konto funktioniert `DefaultAzureCredential`, wenn dein Konto die Rolle **Azure AI User** auf dem Projekt hat. `Ai__ProjectEndpoint` und `Ai__AgentName` setzt du per `dotnet user-secrets` (Projekt `src/SoloCrm.Web`).
