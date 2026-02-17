#region Using directives
using System;
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.DataLogger;
using FTOptix.Store;
using FTOptix.SQLiteStore;
using FTOptix.ODBCStore;
#endregion

public class VariablesSimulator : BaseNetLogic
{
    public override void Start()
    {
        // Get local variables
        runVariable = LogicObject.GetVariable("RunSimulation");
        sine = LogicObject.GetVariable("Sine");
        ramp = LogicObject.GetVariable("Ramp");
        cosine = LogicObject.GetVariable("Cosine");
        // Start simulation
        simulationTask = new PeriodicTask(Simulation, 250, LogicObject);
        simulationTask.Start();
    }

    /// <summary>
    /// Simulates some operations based on boolean run variable.
    /// Increases integer counter up to 99, resets it otherwise.
    /// Increments decimal counter by 0.05.
    /// Updates ramp, sine, and cosine values accordingly.
    /// </summary>
    /// <remarks>
    /// - Integer counter increases until reaching 99; then resets.
    /// - Decimal counter increments by 0.05 each iteration.
    /// - Ramp value updates based on integer counter.
    /// - Sine value is calculated using decimal counter multiplied by 100.
    /// - Cosine value is calculated using decimal counter multiplied by 50.
    /// </remarks>
    private void Simulation()
    {
        if (runVariable.Value)
        {
            if (integerCounter <= 99)
                integerCounter++;
            else
                integerCounter = 0;

            decimalCounter = decimalCounter + 0.05;

            ramp.Value = integerCounter;
            sine.Value = Math.Sin(decimalCounter) * 100;
            cosine.Value = Math.Cos(decimalCounter) * 50;
        }
    }

    public override void Stop()
    {
        simulationTask?.Dispose();
    }

    private PeriodicTask simulationTask;
    private int integerCounter;
    private double decimalCounter;
    private IUAVariable runVariable;
    private IUAVariable sine;
    private IUAVariable cosine;
    private IUAVariable ramp;
}
