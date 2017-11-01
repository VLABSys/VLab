﻿/*
ParallelPortPinSignal.cs is part of the VLAB project.
Copyright (c) 2017 Li Alex Zhang and Contributors

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
using VLab;
using System;

public class ParallelPortPinSignal : ExperimentLogic
{
    ParallelPort pport;
    ParallelPortWave ppw;

    public override void OnStart()
    {
        pport = new ParallelPort((int)config[VLCFG.ParallelPort2]);
        ppw = new ParallelPortWave(pport);
    }

    public override void PrepareCondition(bool regenerateconditon=true)
    {
        for (var i = 0; i < 8; i++)
        {
            ppw.bitlatency_ms[i] = 0;
            ppw.SetBitFreq(i, Math.Pow(2, i));
        }
    }

    protected override void StartExperiment()
    {
        base.StartExperiment();
        ppw.Start(0, 1, 2, 3, 4, 5, 6, 7);
    }

    protected override void StopExperiment()
    {
        ppw.Stop(0, 1, 2, 3, 4, 5, 6, 7);
        base.StopExperiment();
    }
}
