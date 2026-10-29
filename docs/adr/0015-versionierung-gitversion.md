# ADR-015: Versionierung mit GitVersion

**Status:** Accepted · **Datum:** 2026-10-29 · **Entscheider:** Guido Altmann

## Kontext
Die laufende App soll ihre Version anzeigen (Drawer, SOL-39), ohne dass die Version von Hand in Projektdateien gepflegt wird. Releases werden als Git-Tags ohne Präfix gesetzt (`1.11.0`, `1.12.0`). Der Docker-Build hat bewusst kein `.git` (`.dockerignore`), und `dotnet ef migrations bundle` baut das Projekt im Image ein weiteres Mal.

## Entscheidung
- `GitVersion.MsBuild` (Central Package Management, nur im Projekt `SoloCrm.Web`, `PrivateAssets=all`) berechnet die Version aus den Git-Tags und schreibt sie als `AssemblyInformationalVersion`. Konfiguration: `GitVersion.yml` (`ContinuousDelivery`, Tags ohne Präfix).
- `AppVersion` (Web) liest die Version zur Laufzeit, schneidet die Build-Metadaten nach `+` ab und fällt auf `dev` zurück.
- Im Docker-Build ist die GitVersion-Task abgeschaltet (`ENV DisableGitVersionTask=true`, gilt auch für den Build durch `dotnet ef`). Die Version kommt als Build-Arg `APP_VERSION`, den `docker.yml` aus GitVersion berechnet (`dotnet msbuild -t:GetVersion -getProperty:GitVersion_FullSemVer`). Ohne Argument zeigt die App `dev`.
- Alle CI-Jobs, die GitVersion brauchen, checken mit `fetch-depth: 0` aus.

## Betrachtete Optionen
### Option A: Version von Hand in `Directory.Build.props`
**Pro:** keine Abhängigkeit, kein Git nötig · **Contra:** wird vergessen, weicht von den Tags ab
### Option B: GitVersion.MsBuild + Build-Arg für Docker (gewählt)
**Pro:** Tag ist die einzige Quelle, `main` zählt automatisch hoch · **Contra:** volle Historie in CI nötig, Sonderweg für Docker
### Option C: `.git` ins Docker-Build-Context kopieren
**Pro:** ein einheitlicher Weg · **Contra:** größerer Context, Cache-Invalidierung bei jedem Commit, shallow Clone in CI müsste vertieft werden

## Konsequenzen
- Leichter: Release = Tag setzen; die Anzeige in der UI stimmt mit dem Tag überein.
- Schwerer: Lokal und in CI braucht der Checkout alle Tags (`git fetch --tags`); ein flacher Clone bricht den Build.
- Später prüfen: Builds auf `main` zeigen Zwischenstände wie `1.12.1-3`; falls Releases nur noch exakt getaggt angezeigt werden sollen, `GitVersion.yml` anpassen.
