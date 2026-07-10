using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using ATAS.Indicators;
using OFT.Attributes;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

// Key Levels — horizontale Referenz-Level (v1: Preis-Level + Sessions).
// Vortag-OHLC, Tages-High/Low + Equilibrium, Session-High/Low (Asia/EU/US) + Shading,
// Initial Balance. Aus Kerzen/Zeit berechnet, kein TPO/VWAP (folgt in v2).
[DisplayName("Key Levels")]
[Description("Vortag-OHLC, Tages-High/Low + Equilibrium, Session-High/Low (Asia/EU/US) mit Shading, Initial Balance.")]
public class KeyLevels : Indicator
{
    // ─────────────────────────────────────────────────────────────────
    //  STATE (aus abgeschlossenen Bars) + Anzeige-Werte (inkl. Live-Bar)
    // ─────────────────────────────────────────────────────────────────
    private int _lastProcessed = -1;
    private DateTime _curDate = DateTime.MinValue;
    private bool _haveCur, _havePrev;
    private decimal _curOpen, _curHigh, _curLow, _curClose;
    private decimal _pO, _pH, _pL, _pC;

    // Sessions: 0=Asia, 1=EU, 2=US
    private readonly decimal[] _sHigh = new decimal[3];
    private readonly decimal[] _sLow = new decimal[3];
    private readonly bool[] _sHas = new bool[3];

    // Initial Balance
    private decimal _ibH, _ibL;
    private bool _ibHas;

    // Woche / Monat
    private DateTime _curWeekKey = DateTime.MinValue, _curMonthKey = DateTime.MinValue;
    private bool _haveWeek, _havePrevWeek, _haveMonth, _havePrevMonth;
    private decimal _wOpen, _wHigh, _wLow, _wClose, _pwO, _pwH, _pwL, _pwC;
    private decimal _mOpen, _mHigh, _mLow, _mClose, _pmO, _pmH, _pmL, _pmC;
    private decimal _dwHigh, _dwLow, _dmHigh, _dmLow;   // aktuelle Woche/Monat (Anzeige)

    // Anzeige-Werte (Committed + Live-Bar gefaltet)
    private decimal _dCurHigh, _dCurLow, _dCurClose;
    private readonly decimal[] _dSHigh = new decimal[3];
    private readonly decimal[] _dSLow = new decimal[3];
    private decimal _dIbH, _dIbL;

    // Volumen-Profil (Value Area) — Histogramm Preis->Volumen fuer den aktuellen Tag.
    private readonly Dictionary<decimal, decimal> _curVol = new();
    private decimal _pVah, _pVal, _pVpoc; private bool _pHaveVp;   // Vortag
    private decimal _dVah, _dVal, _dVpoc; private bool _dHaveVp;   // aktueller Tag (Anzeige)

    // VWAP + Standardabweichungs-Baender (aus demselben Tages-Histogramm).
    private decimal _pWvwap, _pWvah, _pWval; private bool _pHaveW;
    private decimal _dWvwap, _dWvah, _dWval; private bool _dHaveW;

    private RenderFont _font;

    // ─────────────────────────────────────────────────────────────────
    //  SETTINGS-FELDER
    // ─────────────────────────────────────────────────────────────────
    // Vortag
    private bool _pdH = true, _pdL = true, _pdO = true, _pdC = true;
    private Color _cPdH = Color.FromArgb(255, 123, 171, 123);
    private Color _cPdL = Color.FromArgb(255, 123, 171, 123);
    private Color _cPdO = Color.FromArgb(255, 150, 150, 150);
    private Color _cPdC = Color.FromArgb(255, 123, 171, 123);

    // Aktueller Tag
    private bool _cdH = true, _cdL = true, _cdO = true, _cdEq = true;
    private Color _cCdH = Color.FromArgb(255, 0, 200, 83);
    private Color _cCdL = Color.FromArgb(255, 235, 70, 70);
    private Color _cCdO = Color.FromArgb(255, 160, 160, 160);
    private Color _cCdEq = Color.FromArgb(255, 220, 90, 220);

    // Sessions
    private int _tzOffset = 2;   // Stunden auf die Kerzenzeit, damit die Zeiten der Chart-Anzeige entsprechen
    private TimeSpan _asiaStart = new TimeSpan(0, 0, 0), _asiaEnd = new TimeSpan(9, 0, 0);
    private TimeSpan _euStart = new TimeSpan(9, 0, 0), _euEnd = new TimeSpan(15, 30, 0);
    private TimeSpan _usStart = new TimeSpan(15, 30, 0), _usEnd = new TimeSpan(23, 0, 0);
    private bool _showAsia = true, _showEu = true, _showUs = true;
    private Color _cAsia = Color.FromArgb(255, 220, 200, 60);
    private Color _cEu = Color.FromArgb(255, 90, 200, 120);
    private Color _cUs = Color.FromArgb(255, 235, 90, 120);
    private bool _shadeAsia = true, _shadeEu = false, _shadeUs = false;
    private int _shadeAlpha = 22;

    // Initial Balance
    private bool _showIb = true;
    private TimeSpan _ibStart = new TimeSpan(15, 30, 0);
    private int _ibMinutes = 60;
    private Color _cIb = Color.FromArgb(255, 1, 158, 226);

    // Volumen-Profil
    private int _valueAreaPct = 70;
    private bool _pdVah = true, _pdVal = true, _pdVpoc = true;
    private bool _cdVah = true, _cdVal = true, _cdVpoc = true;
    private Color _cPdVah = Color.FromArgb(255, 123, 171, 123);
    private Color _cPdVal = Color.FromArgb(255, 123, 171, 123);
    private Color _cPdVpoc = Color.FromArgb(255, 255, 180, 60);
    private Color _cCdVah = Color.FromArgb(255, 90, 180, 220);
    private Color _cCdVal = Color.FromArgb(255, 90, 180, 220);
    private Color _cCdVpoc = Color.FromArgb(255, 255, 140, 0);

    // VWAP
    private decimal _wStdFactor = 1.0m;
    private bool _pdWvah = true, _pdWval = true, _pdWpoc = true;
    private bool _cdWvah = true, _cdWval = true, _cdWpoc = true;
    private Color _cPdWEdge = Color.FromArgb(255, 160, 110, 230);
    private Color _cPdWpoc = Color.FromArgb(255, 210, 130, 255);
    private Color _cCdWEdge = Color.FromArgb(255, 130, 160, 255);
    private Color _cCdWpoc = Color.FromArgb(255, 245, 245, 245);

    // Woche / Monat
    private bool _showPwH = true, _showPwL = true, _showPwO = true, _showPwC = true;
    private bool _showCwH = true, _showCwL = true;
    private bool _showPmH = true, _showPmL = true, _showPmO = true, _showPmC = true;
    private bool _showCmH = true, _showCmL = true;
    private Color _cWeek = Color.FromArgb(255, 230, 150, 60);
    private Color _cMonth = Color.FromArgb(255, 150, 120, 220);

    // IB-Multiples
    private bool _ibM50 = true, _ibM100 = true, _ibM150 = false, _ibM200 = false;
    private Color _cIbMult = Color.FromArgb(255, 120, 170, 200);

    // Darstellung
    private int _fontSize = 9;
    private int _lineWidth = 1;
    private bool _labelLeft = true;
    private bool _brokenDotted = true;

    public KeyLevels() : base(true)
    {
        EnableCustomDrawing = true;
        SubscribeToDrawingEvents(DrawingLayouts.Historical | DrawingLayouts.Final);
        DrawAbovePrice = true;
        DataSeries[0].IsHidden = true;
        BuildFont();
    }

    private void BuildFont() => _font = new RenderFont("Arial", _fontSize);

    // ─────────────────────────────────────────────────────────────────
    //  BERECHNUNG
    // ─────────────────────────────────────────────────────────────────
    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar == 0)
            ResetState();

        // Abgeschlossene Bars fest verrechnen (bis CurrentBar-2).
        int lastClosed = CurrentBar - 2;
        while (_lastProcessed < lastClosed)
        {
            _lastProcessed++;
            ProcessBar(_lastProcessed, commit: true);
        }

        if (bar != CurrentBar - 1)
            return;

        ComputeDisplay();
        RedrawChart();
    }

    private void ResetState()
    {
        _lastProcessed = -1;
        _curDate = DateTime.MinValue;
        _haveCur = _havePrev = false;
        for (int i = 0; i < 3; i++) { _sHas[i] = false; _sHigh[i] = 0; _sLow[i] = 0; }
        _ibHas = false;
        _curVol.Clear();
        _pHaveVp = _dHaveVp = false;
        _pHaveW = _dHaveW = false;
        _curWeekKey = _curMonthKey = DateTime.MinValue;
        _haveWeek = _havePrevWeek = _haveMonth = _havePrevMonth = false;
    }

    // Einen Bar in den Tages-/Session-/IB-State einrechnen.
    private void ProcessBar(int bar, bool commit)
    {
        var c = GetCandle(bar);
        if (c == null)
            return;
        DateTime eff = c.Time.AddHours(_tzOffset);   // Chart-Zeit
        DateTime d = eff.Date;

        if (d != _curDate)
        {
            // Tageswechsel: aktuellen Tag als Vortag sichern, neuen Tag starten.
            if (_haveCur)
            {
                _pO = _curOpen; _pH = _curHigh; _pL = _curLow; _pC = _curClose;
                _havePrev = true;
                // Vortag-Volumen-Profil aus dem abgeschlossenen Tag.
                _pHaveVp = ComputeVA(_curVol, out _pVpoc, out _pVah, out _pVal);
                // Vortag-VWAP + Baender.
                _pHaveW = ComputeVWAP(_curVol, out _pWvwap, out var pSig);
                if (_pHaveW) { _pWvah = _pWvwap + _wStdFactor * pSig; _pWval = _pWvwap - _wStdFactor * pSig; }
            }
            _curDate = d;
            _curOpen = c.Open; _curHigh = c.High; _curLow = c.Low; _curClose = c.Close;
            _haveCur = true;
            for (int i = 0; i < 3; i++) _sHas[i] = false;
            _ibHas = false;
            _curVol.Clear();
        }
        else
        {
            if (c.High > _curHigh) _curHigh = c.High;
            if (c.Low < _curLow) _curLow = c.Low;
            _curClose = c.Close;
        }

        // Session-Extrema.
        int sidx = SessionOf(eff.TimeOfDay);
        if (sidx >= 0)
        {
            if (!_sHas[sidx]) { _sHigh[sidx] = c.High; _sLow[sidx] = c.Low; _sHas[sidx] = true; }
            else { if (c.High > _sHigh[sidx]) _sHigh[sidx] = c.High; if (c.Low < _sLow[sidx]) _sLow[sidx] = c.Low; }
        }

        // Initial Balance (erste _ibMinutes ab _ibStart am Tag).
        var tod = eff.TimeOfDay;
        var ibEnd = _ibStart + TimeSpan.FromMinutes(_ibMinutes);
        if (tod >= _ibStart && tod < ibEnd)
        {
            if (!_ibHas) { _ibH = c.High; _ibL = c.Low; _ibHas = true; }
            else { if (c.High > _ibH) _ibH = c.High; if (c.Low < _ibL) _ibL = c.Low; }
        }

        // Volumen je Preislevel in das Tages-Histogramm (fuer die Value Area).
        foreach (var pv in c.GetAllPriceLevels())
            _curVol[pv.Price] = (_curVol.TryGetValue(pv.Price, out var v) ? v : 0m) + pv.Volume;

        // Woche (Montag als Schluessel).
        DateTime wk = MondayOf(eff.Date);
        if (wk != _curWeekKey)
        {
            if (_haveWeek) { _pwO = _wOpen; _pwH = _wHigh; _pwL = _wLow; _pwC = _wClose; _havePrevWeek = true; }
            _curWeekKey = wk; _wOpen = c.Open; _wHigh = c.High; _wLow = c.Low; _haveWeek = true;
        }
        else { if (c.High > _wHigh) _wHigh = c.High; if (c.Low < _wLow) _wLow = c.Low; }
        _wClose = c.Close;

        // Monat.
        DateTime mo = new DateTime(eff.Year, eff.Month, 1);
        if (mo != _curMonthKey)
        {
            if (_haveMonth) { _pmO = _mOpen; _pmH = _mHigh; _pmL = _mLow; _pmC = _mClose; _havePrevMonth = true; }
            _curMonthKey = mo; _mOpen = c.Open; _mHigh = c.High; _mLow = c.Low; _haveMonth = true;
        }
        else { if (c.High > _mHigh) _mHigh = c.High; if (c.Low < _mLow) _mLow = c.Low; }
        _mClose = c.Close;
    }

    private static DateTime MondayOf(DateTime d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    // Anzeige-Werte = Committed-State + Live-Bar (CurrentBar-1) eingefaltet.
    private void ComputeDisplay()
    {
        _dCurHigh = _curHigh; _dCurLow = _curLow; _dCurClose = _curClose;
        for (int i = 0; i < 3; i++) { _dSHigh[i] = _sHigh[i]; _dSLow[i] = _sLow[i]; }
        _dIbH = _ibH; _dIbL = _ibL;

        // Aktuelles-Tag-Volumen-Profil (aus committed Histogramm; 1 Bar Lag, vernachlaessigbar).
        _dHaveVp = ComputeVA(_curVol, out _dVpoc, out _dVah, out _dVal);
        // Aktueller-Tag-VWAP + Baender.
        _dHaveW = ComputeVWAP(_curVol, out _dWvwap, out var dSig);
        if (_dHaveW) { _dWvah = _dWvwap + _wStdFactor * dSig; _dWval = _dWvwap - _wStdFactor * dSig; }

        _dwHigh = _wHigh; _dwLow = _wLow; _dmHigh = _mHigh; _dmLow = _mLow;

        var lc = GetCandle(CurrentBar - 1);
        if (lc == null || !_haveCur)
            return;

        // Nur falten, wenn der Live-Bar noch zum selben (Chart-)Tag gehoert.
        DateTime leff = lc.Time.AddHours(_tzOffset);
        if (leff.Date == _curDate)
        {
            if (lc.High > _dCurHigh) _dCurHigh = lc.High;
            if (lc.Low < _dCurLow) _dCurLow = lc.Low;
            _dCurClose = lc.Close;

            int sidx = SessionOf(leff.TimeOfDay);
            if (sidx >= 0)
            {
                if (!_sHas[sidx]) { _dSHigh[sidx] = lc.High; _dSLow[sidx] = lc.Low; }
                else { if (lc.High > _dSHigh[sidx]) _dSHigh[sidx] = lc.High; if (lc.Low < _dSLow[sidx]) _dSLow[sidx] = lc.Low; }
            }

            var tod = leff.TimeOfDay;
            var ibEnd = _ibStart + TimeSpan.FromMinutes(_ibMinutes);
            if (tod >= _ibStart && tod < ibEnd)
            {
                if (!_ibHas) { _dIbH = lc.High; _dIbL = lc.Low; }
                else { if (lc.High > _dIbH) _dIbH = lc.High; if (lc.Low < _dIbL) _dIbL = lc.Low; }
            }
        }

        // Live-Bar in aktuelle Woche/Monat-Extrema (fast immer selbe Woche/Monat).
        if (_haveWeek)
        {
            if (lc.High > _dwHigh) _dwHigh = lc.High;
            if (lc.Low < _dwLow) _dwLow = lc.Low;
        }
        if (_haveMonth)
        {
            if (lc.High > _dmHigh) _dmHigh = lc.High;
            if (lc.Low < _dmLow) _dmLow = lc.Low;
        }
    }

    // Value Area aus einem Preis->Volumen-Histogramm: POC (max Volumen) + beidseitige
    // Expansion, bis _valueAreaPct des Gesamtvolumens erfasst sind (Market-Profile-Standard).
    private bool ComputeVA(Dictionary<decimal, decimal> hist, out decimal poc, out decimal vah, out decimal val)
    {
        poc = vah = val = 0m;
        if (hist.Count == 0)
            return false;
        var prices = new List<decimal>(hist.Keys);
        prices.Sort();
        decimal total = 0m, maxV = -1m; int pocI = 0;
        for (int i = 0; i < prices.Count; i++)
        {
            decimal v = hist[prices[i]];
            total += v;
            if (v > maxV) { maxV = v; pocI = i; }
        }
        if (total <= 0m)
            return false;
        poc = prices[pocI];
        decimal target = total * (_valueAreaPct / 100m);
        decimal acc = hist[prices[pocI]];
        int lo = pocI, hi = pocI;
        while (acc < target && (lo > 0 || hi < prices.Count - 1))
        {
            decimal below = lo > 0 ? hist[prices[lo - 1]] : -1m;
            decimal above = hi < prices.Count - 1 ? hist[prices[hi + 1]] : -1m;
            if (above >= below)
            {
                if (hi < prices.Count - 1) { hi++; acc += hist[prices[hi]]; }
                else if (lo > 0) { lo--; acc += hist[prices[lo]]; }
                else break;
            }
            else
            {
                if (lo > 0) { lo--; acc += hist[prices[lo]]; }
                else if (hi < prices.Count - 1) { hi++; acc += hist[prices[hi]]; }
                else break;
            }
        }
        val = prices[lo];
        vah = prices[hi];
        return true;
    }

    // Volumen-gewichteter VWAP + Standardabweichung aus dem Preis->Volumen-Histogramm.
    private static bool ComputeVWAP(Dictionary<decimal, decimal> hist, out decimal vwap, out decimal sigma)
    {
        vwap = sigma = 0m;
        decimal vol = 0m, pv = 0m, pv2 = 0m;
        foreach (var kv in hist)
        {
            vol += kv.Value;
            pv += kv.Key * kv.Value;
            pv2 += kv.Key * kv.Key * kv.Value;
        }
        if (vol <= 0m)
            return false;
        vwap = pv / vol;
        decimal variance = pv2 / vol - vwap * vwap;
        if (variance < 0m) variance = 0m;
        sigma = (decimal)Math.Sqrt((double)variance);
        return true;
    }

    // Session-Index fuer eine Uhrzeit (angenommen Start < Ende, kein Mitternachts-Wrap).
    private int SessionOf(TimeSpan t)
    {
        if (t >= _asiaStart && t < _asiaEnd) return 0;
        if (t >= _euStart && t < _euEnd) return 1;
        if (t >= _usStart && t < _usEnd) return 2;
        return -1;
    }

    // ─────────────────────────────────────────────────────────────────
    //  ZEICHNEN
    // ─────────────────────────────────────────────────────────────────
    protected override void OnRender(RenderContext context, DrawingLayouts layout)
    {
        if (_font == null || ChartInfo?.PriceChartContainer is not { } cont)
            return;
        var region = cont.Region;

        DrawSessionShading(context, cont, region);

        bool haveDayRange = _haveCur;
        decimal dLo = haveDayRange ? _dCurLow : 0m, dHi = haveDayRange ? _dCurHigh : 0m;

        // Vortag (kann "gebrochen" sein, wenn der heutige Bereich durchhandelt).
        if (_havePrev)
        {
            if (_pdH) Level(context, region, cont, _pH, "pH", _cPdH, Broken(_pH, dLo, dHi, haveDayRange));
            if (_pdL) Level(context, region, cont, _pL, "pL", _cPdL, Broken(_pL, dLo, dHi, haveDayRange));
            if (_pdO) Level(context, region, cont, _pO, "pO", _cPdO, Broken(_pO, dLo, dHi, haveDayRange));
            if (_pdC) Level(context, region, cont, _pC, "pC", _cPdC, Broken(_pC, dLo, dHi, haveDayRange));
        }

        // Aktueller Tag (Extrema koennen nicht "gebrochen" sein).
        if (_haveCur)
        {
            if (_cdH) Level(context, region, cont, _dCurHigh, "H", _cCdH, false);
            if (_cdL) Level(context, region, cont, _dCurLow, "L", _cCdL, false);
            if (_cdO) Level(context, region, cont, _curOpen, "O", _cCdO, Broken(_curOpen, dLo, dHi, true));
            if (_cdEq) Level(context, region, cont, (_dCurHigh + _dCurLow) / 2m, "EQ", _cCdEq, false);
        }

        // Sessions H/L.
        if (_showAsia && _sHas[0]) { Level(context, region, cont, _dSHigh[0], "Asia H", _cAsia, Broken(_dSHigh[0], dLo, dHi, haveDayRange)); Level(context, region, cont, _dSLow[0], "Asia L", _cAsia, Broken(_dSLow[0], dLo, dHi, haveDayRange)); }
        if (_showEu && _sHas[1]) { Level(context, region, cont, _dSHigh[1], "EU H", _cEu, Broken(_dSHigh[1], dLo, dHi, haveDayRange)); Level(context, region, cont, _dSLow[1], "EU L", _cEu, Broken(_dSLow[1], dLo, dHi, haveDayRange)); }
        if (_showUs && _sHas[2]) { Level(context, region, cont, _dSHigh[2], "US H", _cUs, Broken(_dSHigh[2], dLo, dHi, haveDayRange)); Level(context, region, cont, _dSLow[2], "US L", _cUs, Broken(_dSLow[2], dLo, dHi, haveDayRange)); }

        // Initial Balance.
        if (_showIb && _ibHas)
        {
            Level(context, region, cont, _dIbH, "IBH", _cIb, Broken(_dIbH, dLo, dHi, haveDayRange));
            Level(context, region, cont, _dIbL, "IBL", _cIb, Broken(_dIbL, dLo, dHi, haveDayRange));
        }

        // Volumen-Profil Vortag (kann gebrochen sein).
        if (_pHaveVp)
        {
            if (_pdVah) Level(context, region, cont, _pVah, "pVAH", _cPdVah, Broken(_pVah, dLo, dHi, haveDayRange));
            if (_pdVal) Level(context, region, cont, _pVal, "pVAL", _cPdVal, Broken(_pVal, dLo, dHi, haveDayRange));
            if (_pdVpoc) Level(context, region, cont, _pVpoc, "pVPOC", _cPdVpoc, Broken(_pVpoc, dLo, dHi, haveDayRange));
        }

        // Volumen-Profil aktueller Tag.
        if (_dHaveVp)
        {
            if (_cdVah) Level(context, region, cont, _dVah, "VAH", _cCdVah, false);
            if (_cdVal) Level(context, region, cont, _dVal, "VAL", _cCdVal, false);
            if (_cdVpoc) Level(context, region, cont, _dVpoc, "VPOC", _cCdVpoc, false);
        }

        // VWAP Vortag (kann gebrochen sein).
        if (_pHaveW)
        {
            if (_pdWpoc) Level(context, region, cont, _pWvwap, "pVWAP", _cPdWpoc, Broken(_pWvwap, dLo, dHi, haveDayRange));
            if (_pdWvah) Level(context, region, cont, _pWvah, "pWVAH", _cPdWEdge, Broken(_pWvah, dLo, dHi, haveDayRange));
            if (_pdWval) Level(context, region, cont, _pWval, "pWVAL", _cPdWEdge, Broken(_pWval, dLo, dHi, haveDayRange));
        }

        // VWAP aktueller Tag.
        if (_dHaveW)
        {
            if (_cdWpoc) Level(context, region, cont, _dWvwap, "VWAP", _cCdWpoc, false);
            if (_cdWvah) Level(context, region, cont, _dWvah, "WVAH", _cCdWEdge, false);
            if (_cdWval) Level(context, region, cont, _dWval, "WVAL", _cCdWEdge, false);
        }

        // Vorwoche OHLC (gebrochen moeglich).
        if (_havePrevWeek)
        {
            if (_showPwH) Level(context, region, cont, _pwH, "pwH", _cWeek, Broken(_pwH, dLo, dHi, haveDayRange));
            if (_showPwL) Level(context, region, cont, _pwL, "pwL", _cWeek, Broken(_pwL, dLo, dHi, haveDayRange));
            if (_showPwO) Level(context, region, cont, _pwO, "pwO", _cWeek, Broken(_pwO, dLo, dHi, haveDayRange));
            if (_showPwC) Level(context, region, cont, _pwC, "pwC", _cWeek, Broken(_pwC, dLo, dHi, haveDayRange));
        }
        // Aktuelle Woche High/Low.
        if (_haveWeek)
        {
            if (_showCwH) Level(context, region, cont, _dwHigh, "wH", _cWeek, false);
            if (_showCwL) Level(context, region, cont, _dwLow, "wL", _cWeek, false);
        }

        // Vormonat OHLC.
        if (_havePrevMonth)
        {
            if (_showPmH) Level(context, region, cont, _pmH, "pmH", _cMonth, Broken(_pmH, dLo, dHi, haveDayRange));
            if (_showPmL) Level(context, region, cont, _pmL, "pmL", _cMonth, Broken(_pmL, dLo, dHi, haveDayRange));
            if (_showPmO) Level(context, region, cont, _pmO, "pmO", _cMonth, Broken(_pmO, dLo, dHi, haveDayRange));
            if (_showPmC) Level(context, region, cont, _pmC, "pmC", _cMonth, Broken(_pmC, dLo, dHi, haveDayRange));
        }
        // Aktueller Monat High/Low.
        if (_haveMonth)
        {
            if (_showCmH) Level(context, region, cont, _dmHigh, "mH", _cMonth, false);
            if (_showCmL) Level(context, region, cont, _dmLow, "mL", _cMonth, false);
        }

        // IB-Multiples (Projektionen aus der IB-Range).
        if (_showIb && _ibHas)
        {
            decimal r = _dIbH - _dIbL;
            if (r > 0)
            {
                if (_ibM50) { Level(context, region, cont, _dIbH + 0.5m * r, "IB+50", _cIbMult, false); Level(context, region, cont, _dIbL - 0.5m * r, "IB-50", _cIbMult, false); }
                if (_ibM100) { Level(context, region, cont, _dIbH + 1.0m * r, "IB+100", _cIbMult, false); Level(context, region, cont, _dIbL - 1.0m * r, "IB-100", _cIbMult, false); }
                if (_ibM150) { Level(context, region, cont, _dIbH + 1.5m * r, "IB+150", _cIbMult, false); Level(context, region, cont, _dIbL - 1.5m * r, "IB-150", _cIbMult, false); }
                if (_ibM200) { Level(context, region, cont, _dIbH + 2.0m * r, "IB+200", _cIbMult, false); Level(context, region, cont, _dIbL - 2.0m * r, "IB-200", _cIbMult, false); }
            }
        }
    }

    private static bool Broken(decimal level, decimal lo, decimal hi, bool have)
        => have && level > lo && level < hi;   // heutiger Bereich hat das Level durchhandelt

    private void Level(RenderContext ctx, Rectangle region, IChartContainer cont, decimal price, string label, Color col, bool broken)
    {
        int y;
        try { y = cont.GetYByPrice(price, false); }
        catch { return; }
        if (y < region.Top - 2 || y > region.Bottom + 2)
            return;

        var pen = broken && _brokenDotted
            ? new RenderPen(col, _lineWidth, System.Drawing.Drawing2D.DashStyle.Dot)
            : new RenderPen(col, _lineWidth);
        ctx.DrawLine(pen, region.Left, y, region.Right, y);

        string txt = label;
        var sz = ctx.MeasureString(txt, _font);
        int tx = _labelLeft ? region.Left + 3 : region.Right - sz.Width - 3;
        ctx.DrawString(txt, _font, col, tx, y - sz.Height - 1);
    }

    // Session-Hintergrund als vertikale Baender (zusammenhaengende Bars gleicher Session).
    private void DrawSessionShading(RenderContext ctx, IChartContainer cont, Rectangle region)
    {
        if (!_shadeAsia && !_shadeEu && !_shadeUs)
            return;
        int from = Math.Max(0, FirstVisibleBarNumber);
        int to = Math.Min(CurrentBar - 1, LastVisibleBarNumber);
        if (to < from)
            return;

        int runStart = -1, runSess = -2;
        for (int b = from; b <= to + 1; b++)
        {
            int s = -1;
            if (b <= to)
            {
                var c = GetCandle(b);
                if (c != null) s = ShadeSessionOf(c.Time.AddHours(_tzOffset).TimeOfDay);
            }
            if (s != runSess)
            {
                if (runSess >= 0 && runStart >= 0)
                    FillSession(ctx, cont, region, runStart, b - 1, runSess);
                runSess = s; runStart = b;
            }
        }
    }

    private int ShadeSessionOf(TimeSpan t)
    {
        int s = SessionOf(t);
        if (s == 0 && _shadeAsia) return 0;
        if (s == 1 && _shadeEu) return 1;
        if (s == 2 && _shadeUs) return 2;
        return -1;
    }

    private void FillSession(RenderContext ctx, IChartContainer cont, Rectangle region, int b0, int b1, int sess)
    {
        int x0, x1;
        try { x0 = cont.GetXByBar(b0, false); x1 = cont.GetXByBar(b1 + 1, false); }
        catch { return; }
        x0 = Math.Max(x0, region.Left);
        x1 = Math.Min(x1, region.Right);
        if (x1 <= x0) return;
        Color baseCol = sess == 0 ? _cAsia : sess == 1 ? _cEu : _cUs;
        var col = Color.FromArgb(_shadeAlpha, baseCol.R, baseCol.G, baseCol.B);
        ctx.FillRectangle(col, new Rectangle(x0, region.Top, x1 - x0, region.Height));
    }

    // ─────────────────────────────────────────────────────────────────
    //  PROPERTIES
    // ─────────────────────────────────────────────────────────────────
    // ── Vortag ──
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "High (pH)", GroupName = "Level", Order = 1)]
    public bool PdHigh { get => _pdH; set { _pdH = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Low (pL)", GroupName = "Level", Order = 2)]
    public bool PdLow { get => _pdL; set { _pdL = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Open (pO)", GroupName = "Level", Order = 3)]
    public bool PdOpen { get => _pdO; set { _pdO = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Close (pC)", GroupName = "Level", Order = 4)]
    public bool PdClose { get => _pdC; set { _pdC = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Farbe High", GroupName = "Farben", Order = 10)]
    public Color CPdHigh { get => _cPdH; set { _cPdH = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Farbe Low", GroupName = "Farben", Order = 11)]
    public Color CPdLow { get => _cPdL; set { _cPdL = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Farbe Open", GroupName = "Farben", Order = 12)]
    public Color CPdOpen { get => _cPdO; set { _cPdO = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Farbe Close", GroupName = "Farben", Order = 13)]
    public Color CPdClose { get => _cPdC; set { _cPdC = value; RedrawChart(); } }

    // ── Aktueller Tag ──
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "High", GroupName = "Level", Order = 1)]
    public bool CdHigh { get => _cdH; set { _cdH = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Low", GroupName = "Level", Order = 2)]
    public bool CdLow { get => _cdL; set { _cdL = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Open", GroupName = "Level", Order = 3)]
    public bool CdOpen { get => _cdO; set { _cdO = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Equilibrium (50%)", GroupName = "Level", Order = 4)]
    public bool CdEq { get => _cdEq; set { _cdEq = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Farbe High", GroupName = "Farben", Order = 10)]
    public Color CCdHigh { get => _cCdH; set { _cCdH = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Farbe Low", GroupName = "Farben", Order = 11)]
    public Color CCdLow { get => _cCdL; set { _cCdL = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Farbe Open", GroupName = "Farben", Order = 12)]
    public Color CCdOpen { get => _cCdO; set { _cCdO = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Farbe Equilibrium", GroupName = "Farben", Order = 13)]
    public Color CCdEq { get => _cCdEq; set { _cCdEq = value; RedrawChart(); } }

    // ── Sessions ──
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Zeitzonen-Offset (Stunden)", GroupName = "Zeiten", Order = 0,
        Description = "Stunden, die auf die Kerzenzeit addiert werden, damit die Session-Zeiten der CHART-Anzeige entsprechen. Default 2 (Kerzenzeit 2h hinter Chart). Bei Bedarf +/- anpassen, bis das Shading zu den echten Sessions passt.")]
    [Range(-12, 12)]
    public int TzOffset { get => _tzOffset; set { _tzOffset = Math.Clamp(value, -12, 12); RecalculateValues(); } }

    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Asia Start", GroupName = "Zeiten", Order = 1, Description = "Chart-Zeit. Default 00:00.")]
    public TimeSpan AsiaStart { get => _asiaStart; set { _asiaStart = value; RecalculateValues(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Asia Ende", GroupName = "Zeiten", Order = 2)]
    public TimeSpan AsiaEnd { get => _asiaEnd; set { _asiaEnd = value; RecalculateValues(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "EU Start", GroupName = "Zeiten", Order = 3)]
    public TimeSpan EuStart { get => _euStart; set { _euStart = value; RecalculateValues(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "EU Ende", GroupName = "Zeiten", Order = 4)]
    public TimeSpan EuEnd { get => _euEnd; set { _euEnd = value; RecalculateValues(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "US Start", GroupName = "Zeiten", Order = 5)]
    public TimeSpan UsStart { get => _usStart; set { _usStart = value; RecalculateValues(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "US Ende", GroupName = "Zeiten", Order = 6)]
    public TimeSpan UsEnd { get => _usEnd; set { _usEnd = value; RecalculateValues(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Asia High/Low", GroupName = "Level", Order = 10)]
    public bool ShowAsia { get => _showAsia; set { _showAsia = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "EU High/Low", GroupName = "Level", Order = 11)]
    public bool ShowEu { get => _showEu; set { _showEu = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "US High/Low", GroupName = "Level", Order = 12)]
    public bool ShowUs { get => _showUs; set { _showUs = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Shading Asia", GroupName = "Hintergrund", Order = 20)]
    public bool ShadeAsia { get => _shadeAsia; set { _shadeAsia = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Shading EU", GroupName = "Hintergrund", Order = 21)]
    public bool ShadeEu { get => _shadeEu; set { _shadeEu = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Shading US", GroupName = "Hintergrund", Order = 22)]
    public bool ShadeUs { get => _shadeUs; set { _shadeUs = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Shading-Deckkraft", GroupName = "Hintergrund", Order = 23, Description = "0-120. Niedrig = dezent.")]
    [Range(0, 120)]
    public int ShadeAlpha { get => _shadeAlpha; set { _shadeAlpha = Math.Clamp(value, 0, 120); RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Farbe Asia", GroupName = "Farben", Order = 30)]
    public Color CAsia { get => _cAsia; set { _cAsia = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Farbe EU", GroupName = "Farben", Order = 31)]
    public Color CEu { get => _cEu; set { _cEu = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Farbe US", GroupName = "Farben", Order = 32)]
    public Color CUs { get => _cUs; set { _cUs = value; RedrawChart(); } }

    // ── Initial Balance ──
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB High/Low anzeigen", GroupName = "Level", Order = 1)]
    public bool ShowIb { get => _showIb; set { _showIb = value; RedrawChart(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB Start (Uhrzeit)", GroupName = "Level", Order = 2, Description = "Beginn der Initial Balance. Default 15:30 (US/RTH-Open).")]
    public TimeSpan IbStart { get => _ibStart; set { _ibStart = value; RecalculateValues(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB Dauer (Minuten)", GroupName = "Level", Order = 3)]
    [Range(5, 600)]
    public int IbMinutes { get => _ibMinutes; set { _ibMinutes = Math.Clamp(value, 5, 600); RecalculateValues(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "Farbe IB", GroupName = "Farben", Order = 10)]
    public Color CIb { get => _cIb; set { _cIb = value; RedrawChart(); } }

    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB 50 %", GroupName = "Multiples", Order = 20, Description = "Projektion IB-High + 50%*Range und IB-Low - 50%*Range.")]
    public bool IbM50 { get => _ibM50; set { _ibM50 = value; RedrawChart(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB 100 %", GroupName = "Multiples", Order = 21)]
    public bool IbM100 { get => _ibM100; set { _ibM100 = value; RedrawChart(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB 150 %", GroupName = "Multiples", Order = 22)]
    public bool IbM150 { get => _ibM150; set { _ibM150 = value; RedrawChart(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB 200 %", GroupName = "Multiples", Order = 23)]
    public bool IbM200 { get => _ibM200; set { _ibM200 = value; RedrawChart(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "Farbe Multiples", GroupName = "Multiples", Order = 24)]
    public Color CIbMult { get => _cIbMult; set { _cIbMult = value; RedrawChart(); } }

    // ── Darstellung ──
    [Tab(TabName = "Darstellung", TabOrder = 5)]
    [Display(Name = "Schriftgroesse", GroupName = "Darstellung", Order = 1)]
    [Range(6, 24)]
    public int FontSize { get => _fontSize; set { _fontSize = Math.Clamp(value, 6, 24); BuildFont(); RedrawChart(); } }
    [Tab(TabName = "Darstellung", TabOrder = 5)]
    [Display(Name = "Linienbreite", GroupName = "Darstellung", Order = 2)]
    [Range(1, 5)]
    public int LineWidth { get => _lineWidth; set { _lineWidth = Math.Clamp(value, 1, 5); RedrawChart(); } }
    [Tab(TabName = "Darstellung", TabOrder = 5)]
    [Display(Name = "Label links (aus = rechts)", GroupName = "Darstellung", Order = 3)]
    public bool LabelLeft { get => _labelLeft; set { _labelLeft = value; RedrawChart(); } }
    [Tab(TabName = "Darstellung", TabOrder = 5)]
    [Display(Name = "Gebrochene Level gestrichelt", GroupName = "Darstellung", Order = 4, Description = "Level, durch die der heutige Bereich schon gehandelt hat, gepunktet zeichnen.")]
    public bool BrokenDotted { get => _brokenDotted; set { _brokenDotted = value; RedrawChart(); } }

    // ── Volumen-Profil ──
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Value-Area Anteil (%)", GroupName = "Allgemein", Order = 1, Description = "Anteil des Tagesvolumens in der Value Area (Standard 70). VAH/VAL = Raender, VPOC = Preis mit max. Volumen.")]
    [Range(30, 95)]
    public int ValueAreaPct { get => _valueAreaPct; set { _valueAreaPct = Math.Clamp(value, 30, 95); RecalculateValues(); } }

    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Vortag VAH", GroupName = "Vortag", Order = 10)]
    public bool PdVah { get => _pdVah; set { _pdVah = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Vortag VAL", GroupName = "Vortag", Order = 11)]
    public bool PdVal { get => _pdVal; set { _pdVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Vortag VPOC", GroupName = "Vortag", Order = 12)]
    public bool PdVpoc { get => _pdVpoc; set { _pdVpoc = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Farbe VAH/VAL", GroupName = "Vortag", Order = 13)]
    public Color CPdVaEdge { get => _cPdVah; set { _cPdVah = value; _cPdVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Farbe VPOC", GroupName = "Vortag", Order = 14)]
    public Color CPdVpoc { get => _cPdVpoc; set { _cPdVpoc = value; RedrawChart(); } }

    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Aktueller Tag VAH", GroupName = "Aktueller Tag", Order = 20)]
    public bool CdVah { get => _cdVah; set { _cdVah = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Aktueller Tag VAL", GroupName = "Aktueller Tag", Order = 21)]
    public bool CdVal { get => _cdVal; set { _cdVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Aktueller Tag VPOC", GroupName = "Aktueller Tag", Order = 22)]
    public bool CdVpoc { get => _cdVpoc; set { _cdVpoc = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Farbe VAH/VAL", GroupName = "Aktueller Tag", Order = 23)]
    public Color CCdVaEdge { get => _cCdVah; set { _cCdVah = value; _cCdVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Farbe VPOC", GroupName = "Aktueller Tag", Order = 24)]
    public Color CCdVpoc { get => _cCdVpoc; set { _cCdVpoc = value; RedrawChart(); } }

    // ── VWAP ──
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Std-Faktor (Baender)", GroupName = "Allgemein", Order = 1, Description = "VWAP-Baender = VWAP +/- Faktor * Standardabweichung. 1.0 = 1 Sigma. VWAP + sigma aus dem Tages-Histogramm.")]
    [Range(0.1, 5.0)]
    public decimal WStdFactor { get => _wStdFactor; set { _wStdFactor = Math.Clamp(value, 0.1m, 5.0m); RecalculateValues(); } }

    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Vortag VWAP (POC)", GroupName = "Vortag", Order = 10)]
    public bool PdWpoc { get => _pdWpoc; set { _pdWpoc = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Vortag WVAH (+sigma)", GroupName = "Vortag", Order = 11)]
    public bool PdWvah { get => _pdWvah; set { _pdWvah = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Vortag WVAL (-sigma)", GroupName = "Vortag", Order = 12)]
    public bool PdWval { get => _pdWval; set { _pdWval = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Farbe VWAP", GroupName = "Vortag", Order = 13)]
    public Color CPdWpoc { get => _cPdWpoc; set { _cPdWpoc = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Farbe Baender", GroupName = "Vortag", Order = 14)]
    public Color CPdWEdge { get => _cPdWEdge; set { _cPdWEdge = value; RedrawChart(); } }

    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Aktueller Tag VWAP (POC)", GroupName = "Aktueller Tag", Order = 20)]
    public bool CdWpoc { get => _cdWpoc; set { _cdWpoc = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Aktueller Tag WVAH", GroupName = "Aktueller Tag", Order = 21)]
    public bool CdWvah { get => _cdWvah; set { _cdWvah = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Aktueller Tag WVAL", GroupName = "Aktueller Tag", Order = 22)]
    public bool CdWval { get => _cdWval; set { _cdWval = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Farbe VWAP", GroupName = "Aktueller Tag", Order = 23)]
    public Color CCdWpoc { get => _cCdWpoc; set { _cCdWpoc = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Farbe Baender", GroupName = "Aktueller Tag", Order = 24)]
    public Color CCdWEdge { get => _cCdWEdge; set { _cCdWEdge = value; RedrawChart(); } }

    // ── Woche / Monat ──
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche High", GroupName = "Vorwoche", Order = 1)]
    public bool ShowPwH { get => _showPwH; set { _showPwH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche Low", GroupName = "Vorwoche", Order = 2)]
    public bool ShowPwL { get => _showPwL; set { _showPwL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche Open", GroupName = "Vorwoche", Order = 3)]
    public bool ShowPwO { get => _showPwO; set { _showPwO = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche Close", GroupName = "Vorwoche", Order = 4)]
    public bool ShowPwC { get => _showPwC; set { _showPwC = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Woche High", GroupName = "Vorwoche", Order = 5)]
    public bool ShowCwH { get => _showCwH; set { _showCwH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Woche Low", GroupName = "Vorwoche", Order = 6)]
    public bool ShowCwL { get => _showCwL; set { _showCwL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Farbe Woche", GroupName = "Vorwoche", Order = 7)]
    public Color CWeek { get => _cWeek; set { _cWeek = value; RedrawChart(); } }

    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat High", GroupName = "Vormonat", Order = 10)]
    public bool ShowPmH { get => _showPmH; set { _showPmH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat Low", GroupName = "Vormonat", Order = 11)]
    public bool ShowPmL { get => _showPmL; set { _showPmL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat Open", GroupName = "Vormonat", Order = 12)]
    public bool ShowPmO { get => _showPmO; set { _showPmO = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat Close", GroupName = "Vormonat", Order = 13)]
    public bool ShowPmC { get => _showPmC; set { _showPmC = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Monat High", GroupName = "Vormonat", Order = 14)]
    public bool ShowCmH { get => _showCmH; set { _showCmH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Monat Low", GroupName = "Vormonat", Order = 15)]
    public bool ShowCmL { get => _showCmL; set { _showCmL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Farbe Monat", GroupName = "Vormonat", Order = 16)]
    public Color CMonth { get => _cMonth; set { _cMonth = value; RedrawChart(); } }
}
