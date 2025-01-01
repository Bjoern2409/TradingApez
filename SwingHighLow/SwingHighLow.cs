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
using Utils.Common.Logging;
using Color = System.Drawing.Color;
using static ATAS.Indicators.Technical.SwingHighLow;

[DisplayName("Swing High Low")]
[Description("Template implementation")]
public class SwingHighLow : Indicator
{
    #region Nested Types

    public enum Swing
    {
        None = 0,
        High = 1,
        Low = 2
    }

    internal class Signal
    {
        internal int StartBar { get; set; }
        internal int EndBar { get; set; }
        internal decimal PriceLevel { get; set; }
        internal Swing SignalType { get; set; }
    }

    #endregion

    #region Fields

    private bool hideOldSwings = false;
    private int swingPeriod = 2;
    private int lookbackPeriod = 100;
    private int transparency = 5;

    private Color swingHighColorTransp;
    private Color swingLowColorTransp;

    private readonly PenSettings swingHighColor = new() { Color = DefaultColors.Green.Convert() };
    private readonly PenSettings swingLowColor = new() { Color = DefaultColors.Red.Convert() };
    internal readonly List<Signal> swingSignals = [];

    #endregion    

    #region Settings Properties

    [Display(GroupName = "Settings", Name = "Swing Period", Description = "")]
    [Range(1, 10000)]
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
    [Range(1, 10000)]
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
    public bool HideOldSwings
    {
        get => hideOldSwings;
        set
        {
            hideOldSwings = value;
            RecalculateValues();
        }
    }

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
        swingSignals.Clear();
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar <= swingPeriod * 2)
            return;

        CheckSwingPoints(bar);
        TryCloseSwingPoints(swingSignals, bar);
    }

    protected override void OnRender(RenderContext context, DrawingLayouts layout)
    {
        var swingHighSignals = swingSignals.Where(signal => signal.SignalType == Swing.High).ToList();
        DrawSwingLine(context, swingHighSignals, swingHighColorTransp, swingHighColor);

        var swingLowSignals = swingSignals.Where(signal => signal.SignalType == Swing.Low).ToList();
        DrawSwingLine(context, swingLowSignals, swingLowColorTransp, swingLowColor);
    }

    #endregion

    #region Private Methods

    private void CheckSwingPoints(int bar)
    {
        bool isSwingHigh = true;
        bool isSwingLow = true;

        var centerBar = bar - 1 - swingPeriod;
        var candle = GetCandle(centerBar);
        var swingHigh = candle.High;
        var swingLow = candle.Low;

        for (int i = 1; i <= swingPeriod; i++)
        {
            var prevCandle = GetCandle(centerBar - i);
            var nextCandle = GetCandle(centerBar + i);

            // Check swing high conditions
            if (prevCandle.High > swingHigh || nextCandle.High > swingHigh)
                isSwingHigh = false;

            // Check swing low conditions
            if (prevCandle.Low < swingLow || nextCandle.Low < swingLow)
                isSwingLow = false;

            // Early termination if both are false
            if (!isSwingHigh && !isSwingLow)
                break;
        }

        if (isSwingHigh)
            AddNewSwingLine(swingSignals, centerBar, swingHigh, Swing.High);
        else if (isSwingLow)
            AddNewSwingLine(swingSignals, centerBar, swingLow, Swing.Low);
    }

    private void AddNewSwingLine(List<Signal> swingSignals, int currBar, decimal priceLevel, Swing swingType)
    {
        //var signal = swingSignals.FirstOrDefault(p => p.PriceLevel == priceLevel);

        var signal = new Signal()
        {
            StartBar = currBar,
            PriceLevel = priceLevel,
            SignalType = swingType
        };

        swingSignals.Add(signal);
    }

    private void TryCloseSwingPoints(List<Signal> swingSignals, int bar)
    {
        var candle = GetCandle(bar);

        foreach (var signal in swingSignals)
        {
            if (signal.EndBar > 0)
                continue;

            if (signal.SignalType == Swing.High && candle.High >= signal.PriceLevel)
                signal.EndBar = bar;
            else if (signal.SignalType == Swing.Low && candle.Low <= signal.PriceLevel)
                signal.EndBar = bar;
        }

        swingSignals.RemoveAll(s => bar - s.StartBar > lookbackPeriod);

        if (hideOldSwings)
            swingSignals.RemoveAll(s => s.EndBar == bar);
    }

    private void DrawSwingLine(RenderContext context, List<Signal> swingSignals, Color color, PenSettings penSet)
    {
        if (ChartInfo is null)
            return;

        var signals = swingSignals.Where(
            s => s.StartBar <= LastVisibleBarNumber && s.EndBar == 0 ||
            s.EndBar >= FirstVisibleBarNumber).ToList();

        foreach (var signal in signals)
        {
            var x = ChartInfo.GetXByBar(signal.StartBar);
            var x2 = signal.EndBar > 0 ? ChartInfo.GetXByBar(signal.EndBar) : ChartInfo.Region.Width;
            var y = ChartInfo.GetYByPrice(signal.PriceLevel, false);
            var w = x2 - x;
            var h = ChartInfo.GetYByPrice(signal.PriceLevel, false) - y;
            var rec = new Rectangle(x, y, w, h);
            context.DrawFillRectangle(penSet.RenderObject, color, rec);
        }
    }

    private Color GetColorTransparency(Color color, int tr = 5) => Color.FromArgb((byte)(tr * 25), color.R, color.G, color.B);

    #endregion
}
