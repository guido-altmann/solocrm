# SoloCRM mit n8n verbinden

Diese Anleitung zeigt zwei typische Abläufe:

1. SoloCRM meldet Ereignisse per Webhook an n8n. n8n prüft die Signatur und holt die Details per REST-API.
2. n8n legt Leads per REST-API in SoloCRM an.

Referenz: SPEC Kap. 5, US-17, US-18.

## Voraussetzungen

- **API-Key:** *Einstellungen → API-Keys → Erzeugen*. Der Key (`scrm_…`) wird nur einmal angezeigt.
- **Webhook:** *Einstellungen → Webhooks → Neuer Webhook*. URL ist die Production-URL des n8n-Webhook-Knotens, dazu die gewünschten Ereignisse auswählen. Das Secret wird nur einmal angezeigt.
- **n8n-Instanz** (self-hosted) mit diesen Umgebungsvariablen:
  - `NODE_FUNCTION_ALLOW_BUILTIN=crypto`, damit der Code-Knoten HMAC berechnen darf
  - `SOLOCRM_WEBHOOK_SECRET=<Secret aus SoloCRM>`
  - `N8N_BLOCK_ENV_ACCESS_IN_NODE=false`, damit `$env` im Code-Knoten lesbar ist
- **`http`-Ziele:** Läuft n8n im selben Docker-Netz und wird per `http://n8n:5678/…` angesprochen, muss der Host in SoloCRM freigegeben sein: `Webhooks__AllowedHttpHosts=n8n`. Sonst sind nur `https`-URLs erlaubt.

## Was SoloCRM sendet

```http
POST /webhook/solocrm HTTP/1.1
Content-Type: application/json; charset=utf-8
X-SoloCrm-Event: opportunity.stage_changed
X-SoloCrm-Timestamp: 1759310000
X-SoloCrm-Signature: sha256=5f0c…

{"id":"0199…","type":"opportunity.stage_changed","occurredAt":"2026-10-01T09:12:44Z","data":{"toStageId":"…","fromStageId":"…","opportunityId":"…","toStageStatus":"Won"}}
```

Die Signatur ist `HMAC-SHA256(secret, "<X-SoloCrm-Timestamp>.<roher Body>")` als Hex mit Präfix `sha256=`. Der Body muss **byte-genau** geprüft werden: Wird das JSON vorher geparst und neu serialisiert, passt die Signatur nicht mehr.

Zustellung:

- Erfolg ist jede Antwort mit HTTP 2xx. Alles andere wird bis zu sechsmal wiederholt (nach 1 min, 5 min, 30 min, 2 h und 12 h); jeder Versuch steht im Versandprotokoll.
- Die Zustellung ist *at-least-once*. Ein Ereignis kann also mehrfach ankommen; dedupliziert wird über `id`.
- `data` enthält nur Ids und Status, keine personenbezogenen Daten. Details liefert die REST-API.

## Ablauf 1: Webhook empfangen und prüfen

```
Webhook ─► Code „Signatur prüfen“ ─► IF valid ─┬─► Respond 204 ─► Remove Duplicates (id) ─► HTTP Request (Details) ─► …
                                               └─► Respond 401
```

1. **Webhook**
   - HTTP Method `POST`, Path z. B. `solocrm`
   - *Respond:* „Using 'Respond to Webhook' Node“
   - *Options → Raw Body:* **an** (sonst fehlt der rohe Body für die Signatur)
2. **Code** (JavaScript, „Run Once for All Items“):

   ```js
   const crypto = require('crypto');

   const secret = $env.SOLOCRM_WEBHOOK_SECRET;
   const headers = $input.first().json.headers;
   const body = (await this.helpers.getBinaryDataBuffer(0, 'data')).toString('utf8');

   const timestamp = headers['x-solocrm-timestamp'] ?? '';
   const signature = headers['x-solocrm-signature'] ?? '';
   const expected = 'sha256=' + crypto.createHmac('sha256', secret).update(`${timestamp}.${body}`).digest('hex');

   const signatureOk = signature.length === expected.length
     && crypto.timingSafeEqual(Buffer.from(signature), Buffer.from(expected));
   const fresh = Math.abs(Date.now() / 1000 - Number(timestamp)) < 300; // höchstens 5 Minuten alt (Replay-Schutz)

   return [{ json: { valid: signatureOk && fresh, event: JSON.parse(body) } }];
   ```

3. **IF** `{{ $json.valid }}` ist `true`
   - *true:* **Respond to Webhook** mit Code `204`, danach weiter
   - *false:* **Respond to Webhook** mit Code `401`. SoloCRM protokolliert den Fehlversuch; nach sechs Versuchen gibt es auf.
4. **Remove Duplicates**: „Remove Items Processed in Previous Executions“, Wert `{{ $json.event.id }}`
5. **HTTP Request**, um Details zu holen, z. B. für `opportunity.stage_changed`:
   - `GET https://crm.example.de/api/v1/opportunities/{{ $json.event.data.opportunityId }}`
   - *Authentication:* Generic Credential Type → **Header Auth** mit Name `X-Api-Key` und Wert `scrm_…`

Testen: In SoloCRM *Test senden* beim Webhook klicken. Das sendet ein signiertes `webhook.ping` mit `data.subscriptionId`.

## Ablauf 2: Lead per REST-API anlegen

**HTTP Request** mit `POST https://crm.example.de/api/v1/contacts`, Header-Auth wie oben, *Body Content Type* JSON:

```json
{
  "firstName": "{{ $json.firstName }}",
  "lastName": "{{ $json.lastName }}",
  "email": "{{ $json.email }}",
  "organizationName": "{{ $json.company }}",
  "source": "Website"
}
```

Antworten:

- `201` mit dem angelegten Kontakt.
- `organizationName` übernimmt eine bestehende Organisation gleichen Namens (Groß-/Kleinschreibung egal); sonst wird eine neue angelegt.
- `409` mit `code: "Contact.DuplicateEmail"`, wenn die E-Mail schon existiert. In n8n „Continue On Fail“ aktivieren und den Fall gezielt behandeln.
- `400` liefert Validierungsfehler je Feld, z. B. `errors.email`.

Weitere Endpunkte (Organisationen, Anfragen mit Phasenwechsel per `PATCH`, Aufgaben, Aktivitäten) beschreibt die API-Referenz unter `/scalar/v1`. Sie ist nur nach dem Login erreichbar.

## Grenzen

- 60 Anfragen pro Minute und API-Key. Darüber antwortet die API mit `429` und `Retry-After`; in n8n hilft „Retry On Fail“ mit Wartezeit.
- Geht das Data-Protection-Volume `/app/keys` verloren, sind die Webhook-Secrets nicht mehr lesbar. Die Zustellung schlägt dann mit „Secret nicht lesbar“ fehl. Abhilfe: *Secret neu erzeugen* und in n8n eintragen.
