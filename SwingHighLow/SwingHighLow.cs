namespace ATAS.Indicators.Technical;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators.Drawing;
using OFT.Rendering.Context;
using OFT.Rendering.Settings;
using Color = System.Drawing.Color;
using System.Text.RegularExpressions;
using Utils.Common.Logging;

[Category("TradingApez")]
[DisplayName("Swing High Low")]
[Description("Swing High/Low Indicator")]
public class SwingHighLow : Indicator
{
    #region Nested Types

    public enum TimeFrameScale
    {
        Chart = 0,
        M1 = 1,
        M5 = 5,
        M15 = 15,
        M30 = 30,
        H1 = 60,
        H4 = 240
    }

    public enum SwingType
    {
        None = 0,
        High = 1,
        Low = 2
    }

    internal class TFPeriod
    {
        private int startBar;
        private int endBar;
        private decimal open;
        private decimal high = decimal.MinValue;
        private decimal low = decimal.MaxValue;
        private decimal close;
        private decimal volume;
        private decimal curBarVolume;
        private int highBar;
        private int lowBar;
        private int lastBar = -1;

        internal int StartBar => startBar;
        internal int EndBar => endBar;
        internal decimal Open => open;
        internal decimal High => high;
        internal decimal Low => low;
        internal decimal Close => close;
        internal decimal Volume => volume + curBarVolume;
        internal int HighBar => highBar;
        internal int LowBar => lowBar;

        internal TFPeriod(int bar, IndicatorCandle candle)
        {
            startBar = bar;
            open = candle.Open;
            AddCandle(bar, candle);
        }

        internal void AddCandle(int bar, IndicatorCandle candle)
        {
            if (candle.High > high)
            {
                high = candle.High;
                highBar = bar;
            }

            if (candle.Low < low)
            {
                low = candle.Low;
                lowBar = bar;
            }

            close = candle.Close;
            endBar = bar;

            if (bar != lastBar)
                volume += curBarVolume;

            curBarVolume = candle.Volume;
            lastBar = bar;
        }
    }

    internal class TimeFrameObj
    {
        private readonly TimeFrameScale timeframe;
        private readonly int secondsPerTframe;

        private readonly List<TFPeriod> periods = [];
        private readonly Func<int, IndicatorCandle> GetCandle;

        internal readonly List<Signal> swingSignals = [];
        private bool isNewPeriod;

        internal TFPeriod this[int index]
        {
            get => periods[Count - 1 - index];
            set => periods[Count - 1 - index] = value;
        }

        internal int Count => periods.Count;
        internal bool IsNewPeriod => isNewPeriod;
        internal int SecondsPerTframe => secondsPerTframe;

        internal TimeFrameObj(TimeFrameScale timeFrame, Func<int, IndicatorCandle> getCandle)
        {
            timeframe = timeFrame;
            secondsPerTframe = 60 * (int)timeFrame;
            GetCandle = getCandle;
        }

        internal void AddBar(int bar)
        {
            isNewPeriod = false;
            var candle = GetCandle(bar);

            if (bar == 0)
                CreateNewPeriod(bar, candle);

            var beginTime = GetBeginTime(candle.Time, timeframe);
            var isNewBar = false;
            var isCustomPeriod = false;
            var endBar = periods.Last().EndBar;

            if (isNewBar || !isCustomPeriod && (beginTime >= GetCandle(endBar).LastTime))
            {
                if (!periods.Exists(p => p.StartBar == bar))
                    CreateNewPeriod(bar, candle);
            }
            else
                periods.Last().AddCandle(bar, candle);
        }

        private void CreateNewPeriod(int bar, IndicatorCandle candle)
        {
            periods.Add(new TFPeriod(bar, candle));
            isNewPeriod = true;
        }

        private DateTime GetBeginTime(DateTime time, TimeFrameScale period)
        {
            var tim = time;
            tim = tim.AddMilliseconds(-tim.Millisecond);
            tim = tim.AddSeconds(-tim.Second);

            var begin = (tim - new DateTime()).TotalMinutes % (int)period;
            var res = tim.AddMinutes(-begin);
            return res;
        }
    }

    internal class Signal
    {
        internal int StartBar { get; set; }
        internal int EndBar { get; set; }
        internal decimal PriceLevel { get; set; }
        internal SwingType SignalType { get; set; }
    }

    #endregion

    #region Fields

    private TimeFrameScale timeframe;
    private TimeFrameObj timeFrameObj;

    private int swingPeriod = 2;
    private int lookbackPeriod = 100;
    private int transparency = 5;

    private Color swingHighColorTransp;
    private Color swingLowColorTransp;

    private readonly PenSettings swingHighColor = new() { Color = DefaultColors.Green.Convert() };
    private readonly PenSettings swingLowColor = new() { Color = DefaultColors.Red.Convert() };
    private readonly List<Signal> swingSignals = [];

    #endregion

    #region Settings Properties

    [Display(GroupName = "Settings", Name = "Time Frame", Description = "")]
    public TimeFrameScale Timeframe
    {
        get => timeframe;
        set
        {
            timeframe = value;
            RecalculateValues();
        }
    }

    [Display(GroupName = "Settings", Name = "Swing Period", Description = "")]
    [Range(1, 100000)]
    public int SwingPeriod
    {
        get => swingPeriod;
        set
        {
            swingPeriod = value;
            RecalculateValues();
        }
    }

    [Display(GroupName = "Settings", Name = "Lookback Period", Description = "")]
    [Range(1, 100000)]
    public int LookbackPeriod
    {
        get => lookbackPeriod;
        set
        {
            lookbackPeriod = value;
            RecalculateValues();
        }
    }

    [Display(GroupName = "Drawing", Name = "Hide Old", Description = "")]
    public bool HideOldSwings { get; set; }

    [Display(GroupName = "Drawing", Name = "Transparency", Description = "")]
    [Range(0, 10)]
    public int Transparency
    {
        get => transparency;
        set
        {
            transparency = value;
            swingHighColorTransp = GetColorTransparency(swingHighColor.Color.Convert(), transparency);
            swingLowColorTransp = GetColorTransparency(swingLowColor.Color.Convert(), transparency);
        }
    }

    [Display(GroupName = "Drawing", Name = "Swing High Color", Description = "")]
    public Color SwingHighColor
    {
        get => swingHighColor.Color.Convert();
        set
        {
            swingHighColor.Color = value.Convert();
            swingHighColorTransp = GetColorTransparency(swingHighColor.Color.Convert(), transparency);
        }
    }

    [Display(GroupName = "Drawing", Name = "Swing Low Color", Description = "")]
    public Color SwingLowColor
    {
        get => swingLowColor.Color.Convert();
        set
        {
            swingLowColor.Color = value.Convert();
            swingLowColorTransp = GetColorTransparency(swingLowColor.Color.Convert(), transparency);
        }
    }

    #endregion

    #region Constructor

    public SwingHighLow() : base(true)
    {
        DenyToChangePanel = true;
        DataSeries[0].IsHidden = true;
        ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;

        EnableCustomDrawing = true;
        SubscribeToDrawingEvents(DrawingLayouts.Final);

        swingHighColorTransp = GetColorTransparency(swingHighColor.Color.Convert(), transparency);
        swingLowColorTransp = GetColorTransparency(swingLowColor.Color.Convert(), transparency);
    }

    #endregion

    #region Protected Methods

    protected override void OnRecalculate()
    {
        timeFrameObj = new(Timeframe, GetCandle);
        swingSignals.Clear();
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        if (timeframe != TimeFrameScale.Chart)
        {
            TimeFrameSwings(bar);
            CheckLevelBreak(timeFrameObj.swingSignals, bar);
        }
        else
        {
            CurrentChartSwings(bar);
            CheckLevelBreak(swingSignals, bar);
        }
    }

    protected override void OnRender(RenderContext context, DrawingLayouts layout)
    {
        if (timeframe == TimeFrameScale.Chart)
            DrawSwingLines(context, swingSignals);
        else
            DrawSwingLines(context, timeFrameObj.swingSignals);
    }

    #endregion

    #region Private Methods

    private void TimeFrameSwings(int bar)
    {
        timeFrameObj.AddBar(bar);

        if (!timeFrameObj.IsNewPeriod || timeFrameObj.Count <= 2 * swingPeriod)
            return;

        bool isSwingHigh = true;
        bool isSwingLow = true;

        var centerIndex = swingPeriod;
        var centerCandle = timeFrameObj[centerIndex];

        for (int i = 1; i <= swingPeriod; i++)
        {
            var prevCandle = timeFrameObj[centerIndex - i];
            var nextCandle = timeFrameObj[centerIndex + i];

            // Check swing high conditions
            if (centerCandle.High < prevCandle.High || centerCandle.High < nextCandle.High)
                isSwingHigh = false;

            // Check swing low conditions
            if (centerCandle.Low > prevCandle.Low || centerCandle.Low > nextCandle.Low)
                isSwingLow = false;

            // Early termination if both are false
            if (!isSwingHigh && !isSwingLow)
                break;
        }

        if (isSwingHigh)
            AddNewLevel(timeFrameObj.swingSignals, bar - (swingPeriod - 1), centerCandle.High, SwingType.High);
        if (isSwingLow)
            AddNewLevel(timeFrameObj.swingSignals, bar - (swingPeriod - 1), centerCandle.Low, SwingType.Low);
    }

    private void CurrentChartSwings(int bar)
    {
        if (bar <= 2 * swingPeriod)
            return;

        bool isSwingHigh = true;
        bool isSwingLow = true;

        var centerBar = bar - 1 - swingPeriod;
        var centerCandle = GetCandle(centerBar);

        for (int i = 1; i <= swingPeriod; i++)
        {
            var prevCandle = GetCandle(centerBar - i);
            var nextCandle = GetCandle(centerBar + i);

            // Check swing high conditions
            if (centerCandle.High < prevCandle.High || centerCandle.High < nextCandle.High)
                isSwingHigh = false;

            // Check swing low conditions
            if (centerCandle.Low > prevCandle.Low || centerCandle.Low > nextCandle.Low)
                isSwingLow = false;

            // Early termination if both are false
            if (!isSwingHigh && !isSwingLow)
                break;
        }

        if (isSwingHigh)
            AddNewLevel(swingSignals, bar - swingPeriod, centerCandle.High, SwingType.High);
        if (isSwingLow)
            AddNewLevel(swingSignals, bar - swingPeriod, centerCandle.Low, SwingType.Low);
    }

    private void AddNewLevel(List<Signal> swingSignals, int bar, decimal priceLevel, SwingType swingType)
    {
        var signal = new Signal()
        {
            StartBar = bar,
            PriceLevel = priceLevel,
            SignalType = swingType
        };

        swingSignals.Add(signal);
    }

    private void CheckLevelBreak(List<Signal> swingSignals, int bar)
    {
        var candle = GetCandle(bar);

        foreach (var signal in swingSignals)
        {
            if (signal.EndBar > 0)
                continue;

            if (signal.SignalType == SwingType.High && candle.High >= signal.PriceLevel)
                signal.EndBar = bar;
            else if (signal.SignalType == SwingType.Low && candle.Low <= signal.PriceLevel)
                signal.EndBar = bar;
        }

        swingSignals.RemoveAll(s => bar - s.StartBar > lookbackPeriod);
    }

    private void DrawSwingLines(RenderContext context, List<Signal> swingSignals)
    {
        if (ChartInfo is null)
            return;

        var signals = swingSignals.Where(
            s => s.StartBar <= LastVisibleBarNumber && s.EndBar == 0 ||
            s.EndBar >= FirstVisibleBarNumber).ToList();

        foreach (var signal in signals)
        {
            if (HideOldSwings && signal.EndBar > 0)
                continue;

            var x = ChartInfo.GetXByBar(signal.StartBar);
            var x2 = signal.EndBar > 0 ? ChartInfo.GetXByBar(signal.EndBar) : ChartInfo.Region.Width;
            var y = ChartInfo.GetYByPrice(signal.PriceLevel, false);
            var w = x2 - x;
            var h = ChartInfo.GetYByPrice(signal.PriceLevel, false) - y;
            var rec = new Rectangle(x, y, w, h);

            var color = signal.SignalType == SwingType.High ? swingHighColorTransp : swingLowColorTransp;
            var penSet = signal.SignalType == SwingType.High ? swingHighColor : swingLowColor;
            context.DrawFillRectangle(penSet.RenderObject, color, rec);
        }
    }

    private Color GetColorTransparency(Color color, int tr = 5) => Color.FromArgb((byte)(tr * 25), color.R, color.G, color.B);

    #endregion
}