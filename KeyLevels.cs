using System;
using System.Collections.Generic;
using System.Linq;
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

    // Origin-Bars: wo die jeweilige Periode begann -> Linien-Start (wie Big-Trade-Levels).
    private int _curDayStartBar, _prevDayStartBar = -1;
    private readonly int[] _sStartBar = new int[3];
    private int _ibStartBar = -1;
    private int _curWeekStartBar, _prevWeekStartBar = -1;
    private int _curMonthStartBar, _prevMonthStartBar = -1;

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

    // TPO-Profil: pro Preis die Anzahl verschiedener 30-Min-Brackets, die ihn beruehrt haben.
    private readonly Dictionary<decimal, int> _tpoCount = new();
    private readonly Dictionary<decimal, int> _tpoLastBr = new();
    private decimal _pTpoc, _pTvah, _pTval; private bool _pHaveTpo;
    private decimal _dTpoc, _dTvah, _dTval; private bool _dHaveTpo;
    private decimal _pBt, _pSt; private bool _pHaveBt, _pHaveSt;   // Buy/Sell Tail (Vortag)
    private decimal _dBt, _dSt; private bool _dHaveBt, _dHaveSt;   // aktueller Tag

    private RenderFont _font;
    // Sammel-Liste je Frame: Linien zuerst, dann Labels mit Kollisionsvermeidung.
    private readonly List<(decimal Price, int OriginBar, string Label, Color Col, bool Broken)> _levels = new();

    // Master-Slave-Sync: ein Master publiziert seine Level, Slaves spiegeln sie.
    public enum KLRole { Standalone, Master, Slave }
    private KLRole _role = KLRole.Standalone;
    private string _syncKey = "A";
    private static readonly object _sharedLock = new();
    private static readonly Dictionary<string, List<(decimal Price, string Label, Color Col, bool Broken)>> _shared = new();

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
    private Color _cCurWeek = Color.FromArgb(255, 230, 150, 60);
    private Color _cCurMonth = Color.FromArgb(255, 150, 120, 220);

    // IB-Multiples
    private bool _ibM50 = true, _ibM100 = true, _ibM150 = false, _ibM200 = false;
    private Color _cIbMult = Color.FromArgb(255, 120, 170, 200);

    // TPO
    private int _tpoPeriodMin = 30;
    private int _tpoMinTail = 2;
    private bool _pTpVah = true, _pTpVal = true, _pTpPoc = true, _pTpBt = true, _pTpSt = true;
    private bool _cTpVah = true, _cTpVal = true, _cTpPoc = true, _cTpBt = true, _cTpSt = true;
    private Color _cPTpo = Color.FromArgb(255, 210, 140, 90);
    private Color _cPTail = Color.FromArgb(255, 240, 90, 200);
    private Color _cCTpo = Color.FromArgb(255, 235, 165, 110);
    private Color _cCTail = Color.FromArgb(255, 255, 120, 220);
    private string _lPTvah = "pTVAH", _lPTval = "pTVAL", _lPTpoc = "pTPOC", _lPBt = "pBT", _lPSt = "pST";
    private string _lCTvah = "TVAH", _lCTval = "TVAL", _lCTpoc = "TPOC", _lCBt = "BT", _lCSt = "ST";

    // Darstellung
    private int _fontSize = 9;
    private int _lineWidth = 1;
    private bool _labelLeft = true;
    public enum KLBroken { Gestrichelt, Ausblenden, Normal }
    private KLBroken _brokenMode = KLBroken.Gestrichelt;
    private bool _brokenUseColor = false;
    private Color _brokenColor = Color.FromArgb(255, 150, 150, 150);

    // Editierbare Level-Namen (Default = Kuerzel)
    private string _lPdH = "pH", _lPdL = "pL", _lPdO = "pO", _lPdC = "pC";
    private string _lCdH = "H", _lCdL = "L", _lCdO = "O", _lCdEq = "EQ";
    private string _lAsiaH = "Asia H", _lAsiaL = "Asia L", _lEuH = "EU H", _lEuL = "EU L", _lUsH = "US H", _lUsL = "US L";
    private string _lIbH = "IBH", _lIbL = "IBL";
    private string _lPVah = "pVAH", _lPVal = "pVAL", _lPVpoc = "pVPOC";
    private string _lCVah = "VAH", _lCVal = "VAL", _lCVpoc = "VPOC";
    private string _lPVwap = "pVWAP", _lPWvah = "pWVAH", _lPWval = "pWVAL";
    private string _lCVwap = "VWAP", _lCWvah = "WVAH", _lCWval = "WVAL";
    private string _lPwH = "pwH", _lPwL = "pwL", _lPwO = "pwO", _lPwC = "pwC", _lCwH = "wH", _lCwL = "wL";
    private string _lPmH = "pmH", _lPmL = "pmL", _lPmO = "pmO", _lPmC = "pmC", _lCmH = "mH", _lCmL = "mL";

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
        _prevDayStartBar = _ibStartBar = _prevWeekStartBar = _prevMonthStartBar = -1;
        _curDayStartBar = _curWeekStartBar = _curMonthStartBar = 0;
        for (int i = 0; i < 3; i++) _sStartBar[i] = -1;
        _tpoCount.Clear(); _tpoLastBr.Clear();
        _pHaveTpo = _dHaveTpo = _pHaveBt = _pHaveSt = _dHaveBt = _dHaveSt = false;
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
                _prevDayStartBar = _curDayStartBar;
                // Vortag-Volumen-Profil aus dem abgeschlossenen Tag.
                _pHaveVp = ComputeVA(_curVol, out _pVpoc, out _pVah, out _pVal);
                // Vortag-VWAP + Baender.
                _pHaveW = ComputeVWAP(_curVol, out _pWvwap, out var pSig);
                if (_pHaveW) { _pWvah = _pWvwap + _wStdFactor * pSig; _pWval = _pWvwap - _wStdFactor * pSig; }
                // Vortag-TPO + Tails.
                _pHaveTpo = ComputeTpo(out _pTpoc, out _pTvah, out _pTval);
                ComputeTails(out _pBt, out _pHaveBt, out _pSt, out _pHaveSt);
            }
            _curDate = d;
            _curOpen = c.Open; _curHigh = c.High; _curLow = c.Low; _curClose = c.Close;
            _haveCur = true;
            _curDayStartBar = bar;
            for (int i = 0; i < 3; i++) _sHas[i] = false;
            _ibHas = false;
            _curVol.Clear();
            _tpoCount.Clear(); _tpoLastBr.Clear();
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
            if (!_sHas[sidx]) { _sHigh[sidx] = c.High; _sLow[sidx] = c.Low; _sHas[sidx] = true; _sStartBar[sidx] = bar; }
            else { if (c.High > _sHigh[sidx]) _sHigh[sidx] = c.High; if (c.Low < _sLow[sidx]) _sLow[sidx] = c.Low; }
        }

        // Initial Balance (erste _ibMinutes ab _ibStart am Tag).
        var tod = eff.TimeOfDay;
        var ibEnd = _ibStart + TimeSpan.FromMinutes(_ibMinutes);
        if (tod >= _ibStart && tod < ibEnd)
        {
            if (!_ibHas) { _ibH = c.High; _ibL = c.Low; _ibHas = true; _ibStartBar = bar; }
            else { if (c.High > _ibH) _ibH = c.High; if (c.Low < _ibL) _ibL = c.Low; }
        }

        // Volumen je Preislevel (Value Area) + TPO-Bracket-Zaehlung (Market Profile).
        int br = _tpoPeriodMin > 0 ? (int)(eff.TimeOfDay.TotalMinutes / _tpoPeriodMin) : 0;
        foreach (var pv in c.GetAllPriceLevels())
        {
            decimal p = pv.Price;
            _curVol[p] = (_curVol.TryGetValue(p, out var v) ? v : 0m) + pv.Volume;
            // Preis pro Bracket nur einmal zaehlen (TPO-Count = Anzahl verschiedener Brackets).
            if (!_tpoLastBr.TryGetValue(p, out var lb) || lb != br)
            {
                _tpoCount[p] = (_tpoCount.TryGetValue(p, out var cnt) ? cnt : 0) + 1;
                _tpoLastBr[p] = br;
            }
        }

        // Woche (Montag als Schluessel).
        DateTime wk = MondayOf(eff.Date);
        if (wk != _curWeekKey)
        {
            if (_haveWeek) { _pwO = _wOpen; _pwH = _wHigh; _pwL = _wLow; _pwC = _wClose; _havePrevWeek = true; _prevWeekStartBar = _curWeekStartBar; }
            _curWeekKey = wk; _wOpen = c.Open; _wHigh = c.High; _wLow = c.Low; _haveWeek = true; _curWeekStartBar = bar;
        }
        else { if (c.High > _wHigh) _wHigh = c.High; if (c.Low < _wLow) _wLow = c.Low; }
        _wClose = c.Close;

        // Monat.
        DateTime mo = new DateTime(eff.Year, eff.Month, 1);
        if (mo != _curMonthKey)
        {
            if (_haveMonth) { _pmO = _mOpen; _pmH = _mHigh; _pmL = _mLow; _pmC = _mClose; _havePrevMonth = true; _prevMonthStartBar = _curMonthStartBar; }
            _curMonthKey = mo; _mOpen = c.Open; _mHigh = c.High; _mLow = c.Low; _haveMonth = true; _curMonthStartBar = bar;
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

        // Aktueller-Tag-TPO + Tails (aus committed Bracket-Zaehlung).
        _dHaveTpo = ComputeTpo(out _dTpoc, out _dTvah, out _dTval);
        ComputeTails(out _dBt, out _dHaveBt, out _dSt, out _dHaveSt);

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

    // TPO Value Area aus der Bracket-Zaehlung (POC = meiste Brackets, VA-Expansion).
    private bool ComputeTpo(out decimal poc, out decimal vah, out decimal val)
    {
        poc = vah = val = 0m;
        if (_tpoCount.Count == 0)
            return false;
        var tmp = new Dictionary<decimal, decimal>();
        foreach (var kv in _tpoCount) tmp[kv.Key] = kv.Value;
        return ComputeVA(tmp, out poc, out vah, out val);
    }

    // Buying/Selling Tails: Reihe von Single Prints (TPO-Count == 1) am unteren bzw.
    // oberen Profil-Rand. Level = Basis der Reihe (innere Kante = S/R).
    private void ComputeTails(out decimal bt, out bool hasBt, out decimal st, out bool hasSt)
    {
        bt = st = 0m; hasBt = hasSt = false;
        if (_tpoCount.Count == 0)
            return;
        var prices = _tpoCount.Keys.ToList();
        prices.Sort();
        // Buy Tail unten: von index 0 hoch, solange Single Print.
        int i = 0;
        while (i < prices.Count && _tpoCount[prices[i]] == 1) i++;
        if (i >= _tpoMinTail) { bt = prices[i - 1]; hasBt = true; }
        // Sell Tail oben: von oben runter.
        int j = prices.Count - 1;
        while (j >= 0 && _tpoCount[prices[j]] == 1) j--;
        if ((prices.Count - 1) - j >= _tpoMinTail) { st = prices[j + 1]; hasSt = true; }
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
        _levels.Clear();
        DrawSessionShading(context, cont, region);
        if (_role == KLRole.Slave)
            BuildSlaveLevels();
        else
        {
            CollectLocalLevels();
            if (_role == KLRole.Master)
                PublishLevels();
        }
        FlushLevels(context, cont, region);
    }

    // Sammelt alle lokal berechneten Level (Master/Standalone).
    private void CollectLocalLevels()
    {
        bool haveDayRange = _haveCur;
        decimal dLo = haveDayRange ? _dCurLow : 0m, dHi = haveDayRange ? _dCurHigh : 0m;

        // Vortag (kann "gebrochen" sein, wenn der heutige Bereich durchhandelt).
        if (_havePrev)
        {
            if (_pdH) Level(_pH, _lPdH, _cPdH, Broken(_pH, dLo, dHi, haveDayRange), _prevDayStartBar);
            if (_pdL) Level(_pL, _lPdL, _cPdL, Broken(_pL, dLo, dHi, haveDayRange), _prevDayStartBar);
            if (_pdO) Level(_pO, _lPdO, _cPdO, Broken(_pO, dLo, dHi, haveDayRange), _prevDayStartBar);
            if (_pdC) Level(_pC, _lPdC, _cPdC, Broken(_pC, dLo, dHi, haveDayRange), _prevDayStartBar);
        }

        // Aktueller Tag (Extrema koennen nicht "gebrochen" sein).
        if (_haveCur)
        {
            if (_cdH) Level(_dCurHigh, _lCdH, _cCdH, false, _curDayStartBar);
            if (_cdL) Level(_dCurLow, _lCdL, _cCdL, false, _curDayStartBar);
            if (_cdO) Level(_curOpen, _lCdO, _cCdO, Broken(_curOpen, dLo, dHi, true), _curDayStartBar);
            if (_cdEq) Level((_dCurHigh + _dCurLow) / 2m, _lCdEq, _cCdEq, false, _curDayStartBar);
        }

        // Sessions H/L.
        if (_showAsia && _sHas[0]) { Level(_dSHigh[0], _lAsiaH, _cAsia, Broken(_dSHigh[0], dLo, dHi, haveDayRange), _sStartBar[0]); Level(_dSLow[0], _lAsiaL, _cAsia, Broken(_dSLow[0], dLo, dHi, haveDayRange), _sStartBar[0]); }
        if (_showEu && _sHas[1]) { Level(_dSHigh[1], _lEuH, _cEu, Broken(_dSHigh[1], dLo, dHi, haveDayRange), _sStartBar[1]); Level(_dSLow[1], _lEuL, _cEu, Broken(_dSLow[1], dLo, dHi, haveDayRange), _sStartBar[1]); }
        if (_showUs && _sHas[2]) { Level(_dSHigh[2], _lUsH, _cUs, Broken(_dSHigh[2], dLo, dHi, haveDayRange), _sStartBar[2]); Level(_dSLow[2], _lUsL, _cUs, Broken(_dSLow[2], dLo, dHi, haveDayRange), _sStartBar[2]); }

        // Initial Balance.
        if (_showIb && _ibHas)
        {
            Level(_dIbH, _lIbH, _cIb, Broken(_dIbH, dLo, dHi, haveDayRange), _ibStartBar);
            Level(_dIbL, _lIbL, _cIb, Broken(_dIbL, dLo, dHi, haveDayRange), _ibStartBar);
        }

        // Volumen-Profil Vortag (kann gebrochen sein).
        if (_pHaveVp)
        {
            if (_pdVah) Level(_pVah, _lPVah, _cPdVah, Broken(_pVah, dLo, dHi, haveDayRange), _prevDayStartBar);
            if (_pdVal) Level(_pVal, _lPVal, _cPdVal, Broken(_pVal, dLo, dHi, haveDayRange), _prevDayStartBar);
            if (_pdVpoc) Level(_pVpoc, _lPVpoc, _cPdVpoc, Broken(_pVpoc, dLo, dHi, haveDayRange), _prevDayStartBar);
        }

        // Volumen-Profil aktueller Tag.
        if (_dHaveVp)
        {
            if (_cdVah) Level(_dVah, _lCVah, _cCdVah, false, _curDayStartBar);
            if (_cdVal) Level(_dVal, _lCVal, _cCdVal, false, _curDayStartBar);
            if (_cdVpoc) Level(_dVpoc, _lCVpoc, _cCdVpoc, false, _curDayStartBar);
        }

        // VWAP Vortag (kann gebrochen sein).
        if (_pHaveW)
        {
            if (_pdWpoc) Level(_pWvwap, _lPVwap, _cPdWpoc, Broken(_pWvwap, dLo, dHi, haveDayRange), _prevDayStartBar);
            if (_pdWvah) Level(_pWvah, _lPWvah, _cPdWEdge, Broken(_pWvah, dLo, dHi, haveDayRange), _prevDayStartBar);
            if (_pdWval) Level(_pWval, _lPWval, _cPdWEdge, Broken(_pWval, dLo, dHi, haveDayRange), _prevDayStartBar);
        }

        // VWAP aktueller Tag.
        if (_dHaveW)
        {
            if (_cdWpoc) Level(_dWvwap, _lCVwap, _cCdWpoc, false, _curDayStartBar);
            if (_cdWvah) Level(_dWvah, _lCWvah, _cCdWEdge, false, _curDayStartBar);
            if (_cdWval) Level(_dWval, _lCWval, _cCdWEdge, false, _curDayStartBar);
        }

        // Vorwoche OHLC (gebrochen moeglich).
        if (_havePrevWeek)
        {
            if (_showPwH) Level(_pwH, _lPwH, _cWeek, Broken(_pwH, dLo, dHi, haveDayRange), _prevWeekStartBar);
            if (_showPwL) Level(_pwL, _lPwL, _cWeek, Broken(_pwL, dLo, dHi, haveDayRange), _prevWeekStartBar);
            if (_showPwO) Level(_pwO, _lPwO, _cWeek, Broken(_pwO, dLo, dHi, haveDayRange), _prevWeekStartBar);
            if (_showPwC) Level(_pwC, _lPwC, _cWeek, Broken(_pwC, dLo, dHi, haveDayRange), _prevWeekStartBar);
        }
        // Aktuelle Woche High/Low.
        if (_haveWeek)
        {
            if (_showCwH) Level(_dwHigh, _lCwH, _cCurWeek, false, _curWeekStartBar);
            if (_showCwL) Level(_dwLow, _lCwL, _cCurWeek, false, _curWeekStartBar);
        }

        // Vormonat OHLC.
        if (_havePrevMonth)
        {
            if (_showPmH) Level(_pmH, _lPmH, _cMonth, Broken(_pmH, dLo, dHi, haveDayRange), _prevMonthStartBar);
            if (_showPmL) Level(_pmL, _lPmL, _cMonth, Broken(_pmL, dLo, dHi, haveDayRange), _prevMonthStartBar);
            if (_showPmO) Level(_pmO, _lPmO, _cMonth, Broken(_pmO, dLo, dHi, haveDayRange), _prevMonthStartBar);
            if (_showPmC) Level(_pmC, _lPmC, _cMonth, Broken(_pmC, dLo, dHi, haveDayRange), _prevMonthStartBar);
        }
        // Aktueller Monat High/Low.
        if (_haveMonth)
        {
            if (_showCmH) Level(_dmHigh, _lCmH, _cCurMonth, false, _curMonthStartBar);
            if (_showCmL) Level(_dmLow, _lCmL, _cCurMonth, false, _curMonthStartBar);
        }

        // IB-Multiples (Projektionen aus der IB-Range).
        if (_showIb && _ibHas)
        {
            decimal r = _dIbH - _dIbL;
            if (r > 0)
            {
                if (_ibM50) { Level(_dIbH + 0.5m * r, "IB+50", _cIbMult, false, _ibStartBar); Level(_dIbL - 0.5m * r, "IB-50", _cIbMult, false, _ibStartBar); }
                if (_ibM100) { Level(_dIbH + 1.0m * r, "IB+100", _cIbMult, false, _ibStartBar); Level(_dIbL - 1.0m * r, "IB-100", _cIbMult, false, _ibStartBar); }
                if (_ibM150) { Level(_dIbH + 1.5m * r, "IB+150", _cIbMult, false, _ibStartBar); Level(_dIbL - 1.5m * r, "IB-150", _cIbMult, false, _ibStartBar); }
                if (_ibM200) { Level(_dIbH + 2.0m * r, "IB+200", _cIbMult, false, _ibStartBar); Level(_dIbL - 2.0m * r, "IB-200", _cIbMult, false, _ibStartBar); }
            }
        }

        // TPO Vortag (gebrochen moeglich).
        if (_pHaveTpo)
        {
            if (_pTpPoc) Level(_pTpoc, _lPTpoc, _cPTpo, Broken(_pTpoc, dLo, dHi, haveDayRange), _prevDayStartBar);
            if (_pTpVah) Level(_pTvah, _lPTvah, _cPTpo, Broken(_pTvah, dLo, dHi, haveDayRange), _prevDayStartBar);
            if (_pTpVal) Level(_pTval, _lPTval, _cPTpo, Broken(_pTval, dLo, dHi, haveDayRange), _prevDayStartBar);
        }
        if (_pTpBt && _pHaveBt) Level(_pBt, _lPBt, _cPTail, Broken(_pBt, dLo, dHi, haveDayRange), _prevDayStartBar);
        if (_pTpSt && _pHaveSt) Level(_pSt, _lPSt, _cPTail, Broken(_pSt, dLo, dHi, haveDayRange), _prevDayStartBar);

        // TPO aktueller Tag.
        if (_dHaveTpo)
        {
            if (_cTpPoc) Level(_dTpoc, _lCTpoc, _cCTpo, false, _curDayStartBar);
            if (_cTpVah) Level(_dTvah, _lCTvah, _cCTpo, false, _curDayStartBar);
            if (_cTpVal) Level(_dTval, _lCTval, _cCTpo, false, _curDayStartBar);
        }
        if (_cTpBt && _dHaveBt) Level(_dBt, _lCBt, _cCTail, false, _curDayStartBar);
        if (_cTpSt && _dHaveSt) Level(_dSt, _lCSt, _cCTail, false, _curDayStartBar);
    }

    private static bool Broken(decimal level, decimal lo, decimal hi, bool have)
        => have && level > lo && level < hi;   // heutiger Bereich hat das Level durchhandelt

    // Master-Slave-Sync.
    private string SyncKey() => (InstrumentInfo?.Instrument ?? "") + "|" + _syncKey;

    private void PublishLevels()
    {
        var snap = _levels.Select(l => (l.Price, l.Label, l.Col, l.Broken)).ToList();
        lock (_sharedLock) { _shared[SyncKey()] = snap; }
    }

    private void BuildSlaveLevels()
    {
        List<(decimal Price, string Label, Color Col, bool Broken)> snap = null;
        lock (_sharedLock) { _shared.TryGetValue(SyncKey(), out snap); }
        if (snap == null) return;
        foreach (var s in snap)
            _levels.Add((s.Price, -1, s.Label, s.Col, s.Broken));   // OriginBar -1 = volle Breite
    }

    // Sammelt ein Level (preisbasiert) fuer diesen Frame.
    private void Level(decimal price, string label, Color col, bool broken, int originBar)
        => _levels.Add((price, originBar, label, col, broken));

    // Rechnet Screen-Koordinaten (Preis->Y, Origin-Bar->X), zeichnet Linien und danach
    // Labels mit Kollisionsvermeidung (gleiche Hoehe -> nebeneinander).
    private void FlushLevels(RenderContext ctx, IChartContainer cont, Rectangle region)
    {
        var items = new List<(int Y, int X1, string Label, Color Col, bool Broken)>();
        foreach (var lv in _levels)
        {
            // Broken-Level nur ausblenden, wenn der User das ausdruecklich will (Opt-in).
            if (lv.Broken && _brokenMode == KLBroken.Ausblenden) continue;
            int y;
            try { y = cont.GetYByPrice(lv.Price, false); }
            catch { continue; }
            if (y < region.Top - 2 || y > region.Bottom + 2) continue;
            int x1 = region.Left;
            if (lv.OriginBar >= 0)
            {
                try { int ox = cont.GetXByBar(lv.OriginBar, false); if (ox > x1) x1 = ox; }
                catch { }
            }
            if (x1 > region.Right) x1 = region.Left;
            var col = (lv.Broken && _brokenUseColor) ? _brokenColor : lv.Col;
            items.Add((y, x1, lv.Label, col, lv.Broken));
        }

        foreach (var d in items)
        {
            var pen = d.Broken && _brokenMode == KLBroken.Gestrichelt
                ? new RenderPen(d.Col, _lineWidth, System.Drawing.Drawing2D.DashStyle.Dot)
                : new RenderPen(d.Col, _lineWidth);
            ctx.DrawLine(pen, d.X1, d.Y, region.Right, d.Y);
        }

        var occupied = new List<Rectangle>();
        const int gap = 4;
        foreach (var d in items.OrderBy(x => x.Y).ThenBy(x => x.X1))
        {
            var sz = ctx.MeasureString(d.Label, _font);
            int w = sz.Width, h = sz.Height;
            int ty = d.Y - h - 1;
            int tx = _labelLeft ? d.X1 + 3 : region.Right - w - 3;
            var rect = new Rectangle(tx, ty, w, h);
            int guard = 0;
            while (guard++ < 60 && occupied.Any(o => o.IntersectsWith(rect)))
            {
                if (_labelLeft) tx = occupied.Where(o => o.IntersectsWith(rect)).Max(o => o.Right) + gap;
                else tx = occupied.Where(o => o.IntersectsWith(rect)).Min(o => o.Left) - w - gap;
                rect = new Rectangle(tx, ty, w, h);
            }
            ctx.DrawString(d.Label, _font, d.Col, tx, ty);
            occupied.Add(rect);
        }
        _levels.Clear();
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
    [Display(Name = "High (pH)", GroupName = "Level", Order = 10)]
    public bool PdHigh { get => _pdH; set { _pdH = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Low (pL)", GroupName = "Level", Order = 20)]
    public bool PdLow { get => _pdL; set { _pdL = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Open (pO)", GroupName = "Level", Order = 30)]
    public bool PdOpen { get => _pdO; set { _pdO = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Close (pC)", GroupName = "Level", Order = 40)]
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
    [Display(Name = "High", GroupName = "Level", Order = 10)]
    public bool CdHigh { get => _cdH; set { _cdH = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Low", GroupName = "Level", Order = 20)]
    public bool CdLow { get => _cdL; set { _cdL = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Open", GroupName = "Level", Order = 30)]
    public bool CdOpen { get => _cdO; set { _cdO = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Equilibrium (50%)", GroupName = "Level", Order = 40)]
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
    [Display(Name = "EU High/Low", GroupName = "Level", Order = 20)]
    public bool ShowEu { get => _showEu; set { _showEu = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "US High/Low", GroupName = "Level", Order = 30)]
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
    [Display(Name = "IB High/Low anzeigen", GroupName = "Level", Order = 10)]
    public bool ShowIb { get => _showIb; set { _showIb = value; RedrawChart(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB Start (Uhrzeit)", GroupName = "Level", Order = 20, Description = "Beginn der Initial Balance. Default 15:30 (US/RTH-Open).")]
    public TimeSpan IbStart { get => _ibStart; set { _ibStart = value; RecalculateValues(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB Dauer (Minuten)", GroupName = "Level", Order = 21)]
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
    [Display(Name = "Broken-Modus", GroupName = "Broken (durchgehandelt)", Order = 4,
        Description = "Wie mit Leveln umgehen, durch die der heutige Bereich schon durchgehandelt hat (Preis war ober- UND unterhalb). Gestrichelt = sichtbar, gepunktet (Default). Ausblenden = Linie verschwindet (Opt-in, Vorsicht: auch VPOC etc. weg). Normal = ignorieren, immer durchgezogen.")]
    public KLBroken BrokenModus { get => _brokenMode; set { _brokenMode = value; RedrawChart(); } }
    [Tab(TabName = "Darstellung", TabOrder = 5)]
    [Display(Name = "Broken einfaerben", GroupName = "Broken (durchgehandelt)", Order = 5,
        Description = "An = durchgehandelte Level werden in der Broken-Farbe (statt ihrer eigenen) gezeichnet, bleiben aber sichtbar. Zum dezenten Abheben ohne sie zu verlieren.")]
    public bool BrokenUseColor { get => _brokenUseColor; set { _brokenUseColor = value; RedrawChart(); } }
    [Tab(TabName = "Darstellung", TabOrder = 5)]
    [Display(Name = "Broken-Farbe", GroupName = "Broken (durchgehandelt)", Order = 6)]
    public Color BrokenColor { get => _brokenColor; set { _brokenColor = value; RedrawChart(); } }

    // ── Volumen-Profil ──
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Value-Area Anteil (%)", GroupName = "Allgemein", Order = 1, Description = "Anteil des Tagesvolumens in der Value Area (Standard 70). VAH/VAL = Raender, VPOC = Preis mit max. Volumen.")]
    [Range(30, 95)]
    public int ValueAreaPct { get => _valueAreaPct; set { _valueAreaPct = Math.Clamp(value, 30, 95); RecalculateValues(); } }

    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Vortag VAH", GroupName = "Vortag", Order = 10)]
    public bool PdVah { get => _pdVah; set { _pdVah = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Vortag VAL", GroupName = "Vortag", Order = 20)]
    public bool PdVal { get => _pdVal; set { _pdVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Vortag VPOC", GroupName = "Vortag", Order = 30)]
    public bool PdVpoc { get => _pdVpoc; set { _pdVpoc = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Farbe VAH/VAL", GroupName = "Vortag", Order = 40)]
    public Color CPdVaEdge { get => _cPdVah; set { _cPdVah = value; _cPdVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Farbe VPOC", GroupName = "Vortag", Order = 41)]
    public Color CPdVpoc { get => _cPdVpoc; set { _cPdVpoc = value; RedrawChart(); } }

    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Aktueller Tag VAH", GroupName = "Aktueller Tag", Order = 10)]
    public bool CdVah { get => _cdVah; set { _cdVah = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Aktueller Tag VAL", GroupName = "Aktueller Tag", Order = 20)]
    public bool CdVal { get => _cdVal; set { _cdVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Aktueller Tag VPOC", GroupName = "Aktueller Tag", Order = 30)]
    public bool CdVpoc { get => _cdVpoc; set { _cdVpoc = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Farbe VAH/VAL", GroupName = "Aktueller Tag", Order = 40)]
    public Color CCdVaEdge { get => _cCdVah; set { _cCdVah = value; _cCdVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Farbe VPOC", GroupName = "Aktueller Tag", Order = 41)]
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
    [Display(Name = "Vortag WVAH (+sigma)", GroupName = "Vortag", Order = 20)]
    public bool PdWvah { get => _pdWvah; set { _pdWvah = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Vortag WVAL (-sigma)", GroupName = "Vortag", Order = 30)]
    public bool PdWval { get => _pdWval; set { _pdWval = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Farbe VWAP", GroupName = "Vortag", Order = 40)]
    public Color CPdWpoc { get => _cPdWpoc; set { _cPdWpoc = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Farbe Baender", GroupName = "Vortag", Order = 41)]
    public Color CPdWEdge { get => _cPdWEdge; set { _cPdWEdge = value; RedrawChart(); } }

    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Aktueller Tag VWAP (POC)", GroupName = "Aktueller Tag", Order = 10)]
    public bool CdWpoc { get => _cdWpoc; set { _cdWpoc = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Aktueller Tag WVAH", GroupName = "Aktueller Tag", Order = 20)]
    public bool CdWvah { get => _cdWvah; set { _cdWvah = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Aktueller Tag WVAL", GroupName = "Aktueller Tag", Order = 30)]
    public bool CdWval { get => _cdWval; set { _cdWval = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Farbe VWAP", GroupName = "Aktueller Tag", Order = 40)]
    public Color CCdWpoc { get => _cCdWpoc; set { _cCdWpoc = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Farbe Baender", GroupName = "Aktueller Tag", Order = 41)]
    public Color CCdWEdge { get => _cCdWEdge; set { _cCdWEdge = value; RedrawChart(); } }

    // ── Woche / Monat ──
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche High", GroupName = "Woche", Order = 10)]
    public bool ShowPwH { get => _showPwH; set { _showPwH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche Low", GroupName = "Woche", Order = 20)]
    public bool ShowPwL { get => _showPwL; set { _showPwL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche Open", GroupName = "Woche", Order = 30)]
    public bool ShowPwO { get => _showPwO; set { _showPwO = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche Close", GroupName = "Woche", Order = 40)]
    public bool ShowPwC { get => _showPwC; set { _showPwC = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Woche High", GroupName = "Aktuelle Woche/Monat", Order = 10)]
    public bool ShowCwH { get => _showCwH; set { _showCwH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Woche Low", GroupName = "Aktuelle Woche/Monat", Order = 20)]
    public bool ShowCwL { get => _showCwL; set { _showCwL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Farbe Woche", GroupName = "Woche", Order = 50)]
    public Color CWeek { get => _cWeek; set { _cWeek = value; RedrawChart(); } }

    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat High", GroupName = "Monat", Order = 10)]
    public bool ShowPmH { get => _showPmH; set { _showPmH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat Low", GroupName = "Monat", Order = 20)]
    public bool ShowPmL { get => _showPmL; set { _showPmL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat Open", GroupName = "Monat", Order = 30)]
    public bool ShowPmO { get => _showPmO; set { _showPmO = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat Close", GroupName = "Monat", Order = 40)]
    public bool ShowPmC { get => _showPmC; set { _showPmC = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Monat High", GroupName = "Aktuelle Woche/Monat", Order = 30)]
    public bool ShowCmH { get => _showCmH; set { _showCmH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Monat Low", GroupName = "Aktuelle Woche/Monat", Order = 40)]
    public bool ShowCmL { get => _showCmL; set { _showCmL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Farbe akt. Woche", GroupName = "Aktuelle Woche/Monat", Order = 50)]
    public Color CCurWeek { get => _cCurWeek; set { _cCurWeek = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Farbe akt. Monat", GroupName = "Aktuelle Woche/Monat", Order = 51)]
    public Color CCurMonth { get => _cCurMonth; set { _cCurMonth = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Farbe Monat", GroupName = "Monat", Order = 50)]
    public Color CMonth { get => _cMonth; set { _cMonth = value; RedrawChart(); } }

    // ── Beschriftung (editierbare Namen) ──
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Vortag High", GroupName = "Level", Order = 11)] public string LblPdH { get => _lPdH; set { _lPdH = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Vortag Low", GroupName = "Level", Order = 21)] public string LblPdL { get => _lPdL; set { _lPdL = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Vortag Open", GroupName = "Level", Order = 31)] public string LblPdO { get => _lPdO; set { _lPdO = value; RedrawChart(); } }
    [Tab(TabName = "Vortag", TabOrder = 1)]
    [Display(Name = "Vortag Close", GroupName = "Level", Order = 41)] public string LblPdC { get => _lPdC; set { _lPdC = value; RedrawChart(); } }

    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Tag High", GroupName = "Level", Order = 11)] public string LblCdH { get => _lCdH; set { _lCdH = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Tag Low", GroupName = "Level", Order = 21)] public string LblCdL { get => _lCdL; set { _lCdL = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Tag Open", GroupName = "Level", Order = 31)] public string LblCdO { get => _lCdO; set { _lCdO = value; RedrawChart(); } }
    [Tab(TabName = "Aktueller Tag", TabOrder = 2)]
    [Display(Name = "Equilibrium", GroupName = "Level", Order = 41)] public string LblCdEq { get => _lCdEq; set { _lCdEq = value; RedrawChart(); } }

    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Asia High", GroupName = "Level", Order = 11)] public string LblAsiaH { get => _lAsiaH; set { _lAsiaH = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "Asia Low", GroupName = "Level", Order = 12)] public string LblAsiaL { get => _lAsiaL; set { _lAsiaL = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "EU High", GroupName = "Level", Order = 21)] public string LblEuH { get => _lEuH; set { _lEuH = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "EU Low", GroupName = "Level", Order = 22)] public string LblEuL { get => _lEuL; set { _lEuL = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "US High", GroupName = "Level", Order = 31)] public string LblUsH { get => _lUsH; set { _lUsH = value; RedrawChart(); } }
    [Tab(TabName = "Sessions", TabOrder = 3)]
    [Display(Name = "US Low", GroupName = "Level", Order = 32)] public string LblUsL { get => _lUsL; set { _lUsL = value; RedrawChart(); } }

    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB High", GroupName = "Level", Order = 11)] public string LblIbH { get => _lIbH; set { _lIbH = value; RedrawChart(); } }
    [Tab(TabName = "Initial Balance", TabOrder = 4)]
    [Display(Name = "IB Low", GroupName = "Level", Order = 12)] public string LblIbL { get => _lIbL; set { _lIbL = value; RedrawChart(); } }

    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Vortag VAH", GroupName = "Vortag", Order = 11)] public string LblPVah { get => _lPVah; set { _lPVah = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Vortag VAL", GroupName = "Vortag", Order = 21)] public string LblPVal { get => _lPVal; set { _lPVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Vortag VPOC", GroupName = "Vortag", Order = 31)] public string LblPVpoc { get => _lPVpoc; set { _lPVpoc = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Tag VAH", GroupName = "Aktueller Tag", Order = 11)] public string LblCVah { get => _lCVah; set { _lCVah = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Tag VAL", GroupName = "Aktueller Tag", Order = 21)] public string LblCVal { get => _lCVal; set { _lCVal = value; RedrawChart(); } }
    [Tab(TabName = "Volumen-Profil", TabOrder = 6)]
    [Display(Name = "Tag VPOC", GroupName = "Aktueller Tag", Order = 31)] public string LblCVpoc { get => _lCVpoc; set { _lCVpoc = value; RedrawChart(); } }

    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Vortag VWAP", GroupName = "Vortag", Order = 11)] public string LblPVwap { get => _lPVwap; set { _lPVwap = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Vortag WVAH", GroupName = "Vortag", Order = 21)] public string LblPWvah { get => _lPWvah; set { _lPWvah = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Vortag WVAL", GroupName = "Vortag", Order = 31)] public string LblPWval { get => _lPWval; set { _lPWval = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Tag VWAP", GroupName = "Aktueller Tag", Order = 11)] public string LblCVwap { get => _lCVwap; set { _lCVwap = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Tag WVAH", GroupName = "Aktueller Tag", Order = 21)] public string LblCWvah { get => _lCWvah; set { _lCWvah = value; RedrawChart(); } }
    [Tab(TabName = "VWAP", TabOrder = 7)]
    [Display(Name = "Tag WVAL", GroupName = "Aktueller Tag", Order = 31)] public string LblCWval { get => _lCWval; set { _lCWval = value; RedrawChart(); } }

    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche High", GroupName = "Woche", Order = 11)] public string LblPwH { get => _lPwH; set { _lPwH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche Low", GroupName = "Woche", Order = 21)] public string LblPwL { get => _lPwL; set { _lPwL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche Open", GroupName = "Woche", Order = 31)] public string LblPwO { get => _lPwO; set { _lPwO = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vorwoche Close", GroupName = "Woche", Order = 41)] public string LblPwC { get => _lPwC; set { _lPwC = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Woche High", GroupName = "Aktuelle Woche/Monat", Order = 11)] public string LblCwH { get => _lCwH; set { _lCwH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Woche Low", GroupName = "Aktuelle Woche/Monat", Order = 21)] public string LblCwL { get => _lCwL; set { _lCwL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat High", GroupName = "Monat", Order = 11)] public string LblPmH { get => _lPmH; set { _lPmH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat Low", GroupName = "Monat", Order = 21)] public string LblPmL { get => _lPmL; set { _lPmL = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat Open", GroupName = "Monat", Order = 31)] public string LblPmO { get => _lPmO; set { _lPmO = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Vormonat Close", GroupName = "Monat", Order = 41)] public string LblPmC { get => _lPmC; set { _lPmC = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Monat High", GroupName = "Aktuelle Woche/Monat", Order = 31)] public string LblCmH { get => _lCmH; set { _lCmH = value; RedrawChart(); } }
    [Tab(TabName = "Woche/Monat", TabOrder = 8)]
    [Display(Name = "Akt. Monat Low", GroupName = "Aktuelle Woche/Monat", Order = 41)] public string LblCmL { get => _lCmL; set { _lCmL = value; RedrawChart(); } }

    // ── TPO ──
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "TPO-Periode (Minuten)", GroupName = "Allgemein", Order = 1, Description = "Laenge einer TPO-Periode (Bracket). Standard 30. Wird per Zeitstempel gebucket -> Chart-Bargroesse egal.")]
    [Range(1, 240)]
    public int TpoPeriodMin { get => _tpoPeriodMin; set { _tpoPeriodMin = Math.Clamp(value, 1, 240); RecalculateValues(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Min-Tail (Single Prints)", GroupName = "Allgemein", Order = 2, Description = "Ab so vielen aufeinanderfolgenden Single Prints am Rand zaehlt es als Buying/Selling Tail. Standard 2.")]
    [Range(1, 20)]
    public int TpoMinTail { get => _tpoMinTail; set { _tpoMinTail = Math.Clamp(value, 1, 20); RedrawChart(); } }

    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag TPOC", GroupName = "Vortag", Order = 10)] public bool PTpPoc { get => _pTpPoc; set { _pTpPoc = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag TPOC Name", GroupName = "Vortag", Order = 11)] public string LblPTpoc { get => _lPTpoc; set { _lPTpoc = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag TVAH", GroupName = "Vortag", Order = 20)] public bool PTpVah { get => _pTpVah; set { _pTpVah = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag TVAH Name", GroupName = "Vortag", Order = 21)] public string LblPTvah { get => _lPTvah; set { _lPTvah = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag TVAL", GroupName = "Vortag", Order = 30)] public bool PTpVal { get => _pTpVal; set { _pTpVal = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag TVAL Name", GroupName = "Vortag", Order = 31)] public string LblPTval { get => _lPTval; set { _lPTval = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag Buy Tail", GroupName = "Vortag", Order = 40)] public bool PTpBt { get => _pTpBt; set { _pTpBt = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag Buy Tail Name", GroupName = "Vortag", Order = 41)] public string LblPBt { get => _lPBt; set { _lPBt = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag Sell Tail", GroupName = "Vortag", Order = 50)] public bool PTpSt { get => _pTpSt; set { _pTpSt = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Vortag Sell Tail Name", GroupName = "Vortag", Order = 51)] public string LblPSt { get => _lPSt; set { _lPSt = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Farbe TPO", GroupName = "Vortag", Order = 60)] public Color CPTpo { get => _cPTpo; set { _cPTpo = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Farbe Tails", GroupName = "Vortag", Order = 61)] public Color CPTail { get => _cPTail; set { _cPTail = value; RedrawChart(); } }

    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag TPOC", GroupName = "Aktueller Tag", Order = 10)] public bool CTpPoc { get => _cTpPoc; set { _cTpPoc = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag TPOC Name", GroupName = "Aktueller Tag", Order = 11)] public string LblCTpoc { get => _lCTpoc; set { _lCTpoc = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag TVAH", GroupName = "Aktueller Tag", Order = 20)] public bool CTpVah { get => _cTpVah; set { _cTpVah = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag TVAH Name", GroupName = "Aktueller Tag", Order = 21)] public string LblCTvah { get => _lCTvah; set { _lCTvah = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag TVAL", GroupName = "Aktueller Tag", Order = 30)] public bool CTpVal { get => _cTpVal; set { _cTpVal = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag TVAL Name", GroupName = "Aktueller Tag", Order = 31)] public string LblCTval { get => _lCTval; set { _lCTval = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag Buy Tail", GroupName = "Aktueller Tag", Order = 40)] public bool CTpBt { get => _cTpBt; set { _cTpBt = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag Buy Tail Name", GroupName = "Aktueller Tag", Order = 41)] public string LblCBt { get => _lCBt; set { _lCBt = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag Sell Tail", GroupName = "Aktueller Tag", Order = 50)] public bool CTpSt { get => _cTpSt; set { _cTpSt = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Tag Sell Tail Name", GroupName = "Aktueller Tag", Order = 51)] public string LblCSt { get => _lCSt; set { _lCSt = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Farbe TPO", GroupName = "Aktueller Tag", Order = 60)] public Color CCTpo { get => _cCTpo; set { _cCTpo = value; RedrawChart(); } }
    [Tab(TabName = "TPO", TabOrder = 10)]
    [Display(Name = "Farbe Tails", GroupName = "Aktueller Tag", Order = 61)] public Color CCTail { get => _cCTail; set { _cCTail = value; RedrawChart(); } }

    // ── Sync (Master-Slave) ──
    [Tab(TabName = "Sync", TabOrder = 11)]
    [Display(Name = "Rolle", GroupName = "Master-Slave", Order = 1,
        Description = "Standalone = rechnet + zeichnet lokal (Default). Master = rechnet + publiziert die Level (auf einem Chart mit viel Historie laden). Slave = rechnet NICHT selbst, spiegelt die Level des Masters (fuer schnelle Tick/Renko-Charts mit wenig Historie).")]
    public KLRole Role { get => _role; set { _role = value; RecalculateValues(); RedrawChart(); } }

    [Tab(TabName = "Sync", TabOrder = 11)]
    [Display(Name = "Sync-Key", GroupName = "Master-Slave", Order = 2,
        Description = "Verbindet einen Master mit seinen Slaves. Master und Slave(s) desselben Instruments mit GLEICHEM Key teilen die Level. Verschiedene Keys = getrennte Gruppen. Master-Chart muss geoeffnet bleiben.")]
    public string SyncKeyStr { get => _syncKey; set { _syncKey = value; RedrawChart(); } }
}
