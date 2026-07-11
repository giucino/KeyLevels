# Key Levels — ATAS Indikator

Eigenentwickelter ATAS-Indikator (C#) für Futures (NQ/MNQ, ES/MES …), der die
**wichtigen horizontalen Referenz-Level** automatisch berechnet und zeichnet —
Vortag/Woche/Monat-OHLC, Sessions, Initial Balance, Volumen-Profil, VWAP, TPO und
Buying/Selling Tails. Mit **Master-Slave-Sync** laufen die Level auch auf schnellen
Tick/Renko-Charts mit wenig Historie.

> Teil eines mehrstufigen Projekts (**Stufe 1 — Kontext/Struktur**). Liefert die
> Referenz-Level, an denen die Trigger-Signale (OrderflowSignal, Stufe 2) Konfluenz
> finden. Kein Handelssignal — reine Kontext-Level.

## Was er zeichnet

| Gruppe | Level |
|---|---|
| **Tag** | Vortag O/H/L/C · aktueller Tag H/L/Open · **Equilibrium** (50 %) |
| **Woche / Monat** | Vorwoche + Vormonat O/H/L/C · aktuelle Woche/Monat H/L |
| **Sessions** | Asia / EU / US High/Low + **Session-Shading** (Hintergrund) |
| **Initial Balance** | IB High/Low + **Multiples** 50/100/150/200 % |
| **Volumen-Profil** | VAH / VAL / **VPOC** (Vortag + aktueller Tag) |
| **VWAP** | VWAP + ±σ-Bänder (WVAH/WVAL) |
| **TPO (Market Profile)** | TPOC / TVAH / TVAL + **Buying / Selling Tails** |

Jedes Level ist einzeln **an-/abschaltbar**, **umbenennbar** und in der Farbe einstellbar.

## Kern-Ideen

### Chart-agnostisch (kein Zeit-Chart nötig)
Alle Level werden **aus Kerzen + Zeitstempel** berechnet — Volumen-Profil und VWAP aus
dem Tages-Histogramm (`GetAllPriceLevels`), **TPO** aus 30-Min-Brackets, die per Zeitstempel
gebildet werden. Läuft direkt auf deinen **Tick-/Renko-Charts**; auf feineren Charts ist
das TPO sogar genauer als auf einem echten 30-Min-Chart.

### Zeitzonen-Offset
Sessions, IB und TPO nutzen einen **Zeitzonen-Offset** (Stunden, Default +2), damit die
Zeiten der Chart-Anzeige entsprechen. Ein Zahlenwert – bei Bedarf ±1 justieren, bis das
Session-Shading zu den echten Sessions passt.

### Linien starten am Ursprung
Jede Linie beginnt dort, wo ihre **Periode entstand** (Tag/Woche/Monat/Session/IB-Start)
und läuft nach rechts – nicht über den ganzen Chart. Labels sitzen am Linien-Start und
werden bei **gleicher Höhe nebeneinander** versetzt (nichts überlappt).

### Broken-Handling (nichts verschwindet ungewollt)
Ein Level, durch das der heutige Bereich **durchgehandelt** hat (Preis ober- **und**
unterhalb), gilt als *broken*. Modi: **Gestrichelt** (Default, bleibt sichtbar) ·
**Einfärben** (dezent grau, bleibt sichtbar) · **Ausblenden** (Opt-in) · **Normal**.
Ein durchlaufener VPOC bleibt also standardmäßig als Referenz erhalten.

### Master-Slave-Sync
Ein **Master**-Chart mit viel Historie berechnet die Level und **publiziert** sie; beliebig
viele **Slave**-Charts (schnelle Tick/Renko mit wenig Historie) **spiegeln** sie live über
einen gemeinsamen **Sync-Key** (pro Instrument). So hast du auf dem Trigger-Chart alle
Wochen-/Monats-/TPO-Level, obwohl er selbst kaum Historie lädt. Master-Chart offen lassen.

## Einstellungen (Reiter)

Vortag · Aktueller Tag · Woche · Monat · Sessions · Initial Balance · TPO · Sync · Darstellung.
In den Level-Reitern steht das **Namensfeld direkt unter jeder Checkbox** (Enable → Name → … → Farben).

**Nach Zeitrahmen gebündelt:** Jeder Zeitrahmen hat einen eigenen Reiter mit derselben
Gruppen-Struktur — *Vorperiode (OHLC) · aktuelle Periode · Volumen-Profil · VWAP*.
**Vortag**/**Aktueller Tag** für den Tag, **Woche** und **Monat** je eigen. Keine
separaten Metrik-Reiter (Volumen-Profil/VWAP) mehr.

Wichtige Regler: **Zeitzonen-Offset**, **Value-Area %** (Default 70), **VWAP Std-Faktor**
(Default 1σ), **TPO-Periode** (Default 30 Min) + **Min-Tail**, **IB Start/Dauer**,
**Broken-Modus**, **Rolle** (Standalone/Master/Slave) + **Sync-Key**.

## Build & Installation

- TargetFramework `net10.0-windows`, ATAS-DLLs per HintPath.
- `dotnet build -c Release`, dann `KeyLevels.dll` nach `%APPDATA%\ATAS\Indicators\` (und
  `%APPDATA%\ATAS X\Indicators\`) kopieren.
- Indikator entfernen + neu hinzufügen (DLL-Reload).

## Hinweise

- Volumen-Profil / TPO brauchen **Cluster-/Footprint-Daten** (Tick/Renko haben die). Ohne
  Cluster-Daten werden diese Level nicht gezeichnet.
- Vor-Perioden-Level (Vortag/-woche/-monat, VP, VWAP, TPO) werden beim Perioden­wechsel
  **eingefroren** (kein Repaint); aktuelle Perioden laufen live mit.
- Session-Zeiten sind einstellbar; Default Asia 0–9, EU 9–15:30, US 15:30–23 (Chart-Zeit).

## Lizenz / Hinweis

Private Eigenentwicklung auf Basis allgemein verfügbarer Market-Profile-/Orderflow-Konzepte.
Kein Nachbau kommerzieller Fremdprodukte. **Kein Handelssignal, keine Anlageberatung** —
Nutzung auf eigenes Risiko.
