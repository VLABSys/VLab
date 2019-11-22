﻿/*
DisplayCalibration.cs is part of the Experica.
Copyright (c) 2016 Li Alex Zhang and Contributors

Permission is hereby granted, free of charge, to any person obtaining a 
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the 
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included 
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF 
OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System;
using OxyPlot;
using OxyPlot.Series;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;

namespace Experica
{
    /// <summary>
    /// Present R,G,B colors and measure Intensity, so that a color lookup table can
    /// be constructed to linearize R,G,B Intensity.
    /// 
    /// Then, the linearized R,G,B colors Spectral can be measured, so that Cone excitations
    /// can be calculated using the Spectral and Cone Fundamentals.
    /// 
    /// Required experiment custom parameters:
    /// 
    /// PRModel: model name of the spectroradiometer from Photo Research, Inc. (e.g., PR701)
    /// COM: COM port of the spectroradiometer. (e.g., COM2)
    /// Measure: type of measurement, Spectral or Intensity.
    /// PlotMeasure: if plot measurement. (Bool)
    /// FitType: function used to fit the intensity measurement, Gamma, LinearSpline or CubicSpline.
    /// </summary>
    public class DisplayCalibration : ExperimentLogic
    {
        protected ISpectroRadioMeter spectroradiometer;
        Dictionary<string, List<object>> imeasurement = new Dictionary<string, List<object>>();
        Dictionary<string, List<object>> smeasurement = new Dictionary<string, List<object>>();
        IntensityMeasurementPlot iplot; SpectralMeasurementPlot splot;

        protected override void OnStart()
        {
            spectroradiometer = new PR(ex.GetParam("COM").Convert<string>(), ex.GetParam("PRModel").Convert<string>());
        }

        protected override void OnStartExperiment()
        {
            if (string.IsNullOrEmpty(ex.Display_ID))
            {
                Extension.WarningDialog("Display_ID is not set!");
            }
            SetEnvActiveParam("Visible", false);
            spectroradiometer?.Connect(1000);
            // Setup Measurement: Primary Lens, Add On Lens 1, Add On Lens 2, Aperture, Photometric Units(1=Metric), 
            // Detector Exposure Time(1000ms), Capture Mode(0=Single Capture), Number of Measure to Average(1=No Average), 
            // Power or Energy(0=Power), Trigger Mode(0=Internal Trigger), View Shutter(0=Open), CIE Observer(0=2°)
            spectroradiometer?.Setup("S,,,,1,1000,0,1,0,0,0,0", 1000);
            iplot?.Dispose();
            iplot = new IntensityMeasurementPlot();
            splot?.Dispose();
            splot = new SpectralMeasurementPlot();
        }

        protected override void OnStopExperiment()
        {
            SetEnvActiveParam("Visible", false);
            spectroradiometer?.Close();
            if (config.Display == null)
            {
                config.Display = new Dictionary<string, Display>();
            }
            if (string.IsNullOrEmpty(ex.Display_ID))
            {
                Extension.WarningDialog("Display_ID is not set!");
                return;
            }

            if (Extension.YesNoDialog("Save Measurement to Configuration?"))
            {
                if (config.Display.ContainsKey(ex.Display_ID))
                {
                    if (imeasurement.Count > 0)
                    {
                        config.Display[ex.Display_ID].IntensityMeasurement = imeasurement;
                    }
                    if (smeasurement.Count > 0)
                    {
                        config.Display[ex.Display_ID].SpectralMeasurement = smeasurement;
                    }
                }
                else
                {
                    config.Display[ex.Display_ID] = new Display() { ID = ex.Display_ID, IntensityMeasurement = imeasurement, SpectralMeasurement = smeasurement };
                }
            }
            if (Extension.YesNoDialog("Save Measurement Data?"))
            {
                var path = Extension.SaveFile("Save Measurement Data ...");
                if (!string.IsNullOrEmpty(path))
                {
                    var ds = new Dictionary<string, Display>(config.Display);
                    ds[ex.Display_ID] = new Display() { ID = ex.Display_ID, IntensityMeasurement = imeasurement, SpectralMeasurement = smeasurement };
                    path.WriteYamlFile(ds);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            iplot?.Dispose();
            splot?.Dispose();
        }

        protected override void Logic()
        {
            switch (CondState)
            {
                case CONDSTATE.NONE:
                    CondState = CONDSTATE.PREICI;
                    break;
                case CONDSTATE.PREICI:
                    if (PreICIHold >= ex.PreICI)
                    {
                        CondState = CONDSTATE.COND;
                        SetEnvActiveParam("Visible", true);
                    }
                    break;
                case CONDSTATE.COND:
                    if (CondHold >= ex.CondDur)
                    {
                        switch ((string)ex.GetParam("Measure"))
                        {
                            case "Intensity":
                                // Measure Intensity Y, CIE x, y
                                var m1 = spectroradiometer?.Measure("1", 8000) as Dictionary<string, double>;
                                if (m1 != null)
                                {
                                    foreach (var f in m1.Keys)
                                    {
                                        if (imeasurement.ContainsKey(f))
                                        {
                                            imeasurement[f].Add(m1[f]);
                                        }
                                        else
                                        {
                                            imeasurement[f] = new List<object>() { m1[f] };
                                        }
                                    }
                                    var color = condmanager.finalcond["Color"][condmanager.condidx];
                                    if (imeasurement.ContainsKey("Color"))
                                    {
                                        imeasurement["Color"].Add(color);
                                    }
                                    else
                                    {
                                        imeasurement["Color"] = new List<object>() { color };
                                    }
                                }
                                break;
                            case "Spectral":
                                // Measure Peak λ, Integrated Spectral, Integrated Photon, λs, λ Intensities
                                var m5 = spectroradiometer?.Measure("5", 10000) as Dictionary<string, object>;
                                if (m5 != null)
                                {
                                    foreach (var f in m5.Keys)
                                    {
                                        if (smeasurement.ContainsKey(f))
                                        {
                                            smeasurement[f].Add(m5[f]);
                                        }
                                        else
                                        {
                                            smeasurement[f] = new List<object>() { m5[f] };
                                        }
                                    }
                                    var color = condmanager.finalcond["Color"][condmanager.condidx];
                                    if (smeasurement.ContainsKey("Color"))
                                    {
                                        smeasurement["Color"].Add(color);
                                    }
                                    else
                                    {
                                        smeasurement["Color"] = new List<object>() { color };
                                    }
                                }
                                break;
                        }
                        CondState = CONDSTATE.SUFICI;
                        if (ex.PreICI != 0 || ex.SufICI != 0)
                        {
                            SetEnvActiveParam("Visible", false);
                        }
                    }
                    break;
                case CONDSTATE.SUFICI:
                    if (SufICIHold >= ex.SufICI)
                    {
                        // Plot Measurement
                        if (ex.GetParam("PlotMeasure").Convert<bool>())
                        {
                            switch ((string)ex.GetParam("Measure"))
                            {
                                case "Intensity":
                                    iplot.Visualize(imeasurement, ex.GetParam("FitType"));
                                    break;
                                case "Spectral":
                                    splot.Visualize(smeasurement);
                                    break;
                            }
                        }
                        CondState = CONDSTATE.NONE;
                    }
                    break;
            }
        }
    }

    public class IntensityMeasurementPlot : OxyPlotForm
    {
        public IntensityMeasurementPlot()
        {
            Width = 400;
            Height = 400;
            Text = "Display Intensity and Calibration";
        }

        public override void Visualize(params object[] data)
        {
            var m = (Dictionary<string, List<object>>)data[0];
            var fittype = data[1].Convert<DisplayFitType>();
            if (m.Count == 0) return;
            Dictionary<string, double[]> x = null, y = null;
            var xx = Generate.LinearSpaced(100, 0, 1);
            double[] ryy = xx, gyy = xx, byy = xx, riy = xx, giy = xx, biy = xx;
            switch (fittype)
            {
                case DisplayFitType.Gamma:
                    m.GetRGBIntensityMeasurement(out x, out y, false, true);
                    double rgamma, ra, rc, ggamma, ga, gc, bgamma, ba, bc;
                    Extension.GammaFit(x["R"], y["R"], out rgamma, out ra, out rc);
                    Extension.GammaFit(x["G"], y["G"], out ggamma, out ga, out gc);
                    Extension.GammaFit(x["B"], y["B"], out bgamma, out ba, out bc);

                    ryy = Generate.Map(xx, i => Extension.GammaFunc(i, rgamma, ra, rc));
                    gyy = Generate.Map(xx, i => Extension.GammaFunc(i, ggamma, ga, gc));
                    byy = Generate.Map(xx, i => Extension.GammaFunc(i, bgamma, ba, bc));
                    riy = Generate.Map(xx, i => Extension.InverseGammaFunc(i, rgamma, ra, rc));
                    giy = Generate.Map(xx, i => Extension.InverseGammaFunc(i, ggamma, ga, gc));
                    biy = Generate.Map(xx, i => Extension.InverseGammaFunc(i, bgamma, ba, bc));
                    break;
                case DisplayFitType.LinearSpline:
                case DisplayFitType.CubicSpline:
                    m.GetRGBIntensityMeasurement(out x, out y, true, true);
                    IInterpolation ri, gi, bi;
                    if (Extension.SplineFit(x["R"], y["R"], out ri, fittype))
                    {
                        ryy = Generate.Map(xx, i => ri.Interpolate(i));
                    }
                    if (Extension.SplineFit(x["G"], y["G"], out gi, fittype))
                    {
                        gyy = Generate.Map(xx, i => gi.Interpolate(i));
                    }
                    if (Extension.SplineFit(x["B"], y["B"], out bi, fittype))
                    {
                        byy = Generate.Map(xx, i => bi.Interpolate(i));
                    }
                    IInterpolation rii, gii, bii;
                    if (Extension.SplineFit(y["R"], x["R"], out rii, fittype))
                    {
                        riy = Generate.Map(xx, i => rii.Interpolate(i));
                    }
                    if (Extension.SplineFit(y["G"], x["G"], out gii, fittype))
                    {
                        giy = Generate.Map(xx, i => gii.Interpolate(i));
                    }
                    if (Extension.SplineFit(y["B"], x["B"], out bii, fittype))
                    {
                        biy = Generate.Map(xx, i => bii.Interpolate(i));
                    }
                    break;
            }
            plotview.Visualize(x, y, null, new Dictionary<string, OxyColor>() { { "R", OxyColors.Red }, { "G", OxyColors.Green }, { "B", OxyColors.Blue } },
                new Dictionary<string, Type>() { { "R", typeof(ScatterSeries) }, { "G", typeof(ScatterSeries) }, { "B", typeof(ScatterSeries) } },
                new Dictionary<string, double>() { { "R", 2 }, { "G", 2 }, { "B", 2 } },
                new Dictionary<string, LineStyle>() { { "R", LineStyle.Automatic }, { "G", LineStyle.Automatic }, { "B", LineStyle.Automatic } },
                "", "Color Component Value", "Intensity", legendposition: LegendPosition.LeftTop);
            plotview.Visualize(xx, new Dictionary<string, double[]>() { { "RFit", ryy }, { "GFit", gyy }, { "BFit", byy } },
                null, new Dictionary<string, OxyColor>() { { "RFit", OxyColors.Red }, { "GFit", OxyColors.Green }, { "BFit", OxyColors.Blue } },
                new Dictionary<string, Type>() { { "RFit", typeof(LineSeries) }, { "GFit", typeof(LineSeries) }, { "BFit", typeof(LineSeries) } },
                new Dictionary<string, double>() { { "RFit", 1 }, { "GFit", 1 }, { "BFit", 1 } },
                new Dictionary<string, LineStyle>() { { "RFit", LineStyle.Solid }, { "GFit", LineStyle.Solid }, { "BFit", LineStyle.Solid } },
                isclear: false);
            plotview.Visualize(xx, new Dictionary<string, double[]>() { { "RCorr", riy }, { "GCorr", giy }, { "BCorr", biy } },
                null, new Dictionary<string, OxyColor>() { { "RCorr", OxyColors.Red }, { "GCorr", OxyColors.Green }, { "BCorr", OxyColors.Blue } },
                new Dictionary<string, Type>() { { "RCorr", typeof(LineSeries) }, { "GCorr", typeof(LineSeries) }, { "BCorr", typeof(LineSeries) } },
                new Dictionary<string, double>() { { "RCorr", 1 }, { "GCorr", 1 }, { "BCorr", 1 } },
                new Dictionary<string, LineStyle>() { { "RCorr", LineStyle.Dash }, { "GCorr", LineStyle.Dash }, { "BCorr", LineStyle.Dash } },
                isclear: false);
        }
    }

    public class SpectralMeasurementPlot : OxyPlotForm
    {
        public SpectralMeasurementPlot()
        {
            Width = 400;
            Height = 400;
            Text = "Display Spectral";
        }

        public override void Visualize(params object[] data)
        {
            var m = (Dictionary<string, List<object>>)data[0];
            if (m.Count == 0) return;
            Dictionary<string, double[]> x = null;
            Dictionary<string, double[][]> yi = null;
            Dictionary<string, double[][]> y = null;
            m.GetRGBSpectralMeasurement(out x, out yi, out y);

            plotview.Model.IsLegendVisible = false;
            plotview.Clear();
            foreach (var f in x.Keys)
            {
                OxyColor c = OxyColors.Black;
                switch (f)
                {
                    case "R":
                        c = OxyColors.Red;
                        break;
                    case "G":
                        c = OxyColors.Green;
                        break;
                    case "B":
                        c = OxyColors.Blue;
                        break;
                }
                for (var i = 0; i < yi[f].Length; i++)
                {
                    var color = OxyColor.FromAColor((byte)(x[f][i] * 127.5 + 127.5), c);
                    plotview.Visualize(yi[f][i], y[f][i], null, color, typeof(LineSeries), 1, LineStyle.Solid,
                "", "Wavelength", "Intensity", isclear: false);
                }

            }
        }
    }
}