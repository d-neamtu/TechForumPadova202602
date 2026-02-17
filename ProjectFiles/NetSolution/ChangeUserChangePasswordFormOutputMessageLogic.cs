#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.NetLogic;
using FTOptix.NativeUI;
using FTOptix.HMIProject;
using FTOptix.UI;
using FTOptix.CoreBase;
using FTOptix.Core;
using FTOptix.Retentivity;
using FTOptix.RecipeX;
using FTOptix.OPCUAServer;
#endregion

public class ChangeUserChangePasswordFormOutputMessageLogic : BaseNetLogic
{
    public override void Start()
    {
        messageVariable = Owner.GetVariable("Message");
        task = new DelayedTask(() =>
        {
            if (messageVariable == null)
            {
                Log.Error("ChangeUserFormOutputMessageLogic", "Unable to find variable Message in LoginFormOutputMessage label");
                return;
            }

            messageVariable.Value = "";
            taskStarted = false;
        }, 10000, LogicObject);
    }

    /// <summary>
    /// This method sets the output message for a form, replacing the current value with the provided message.
    /// If the message variable is null, it logs an error and returns immediately.
    /// It also cancels the ongoing task if one is in progress and marks it as completed.
    /// Then it starts a new task to continue processing.
    /// </summary>
    /// <param name="message">The message to set as the output message.</param>
    /// <remarks>
    /// If <see cref="messageVariable"/> is null, an error is logged and the method returns immediately.
    /// The method also cancels any ongoing task and starts a new one to continue processing.
    /// </remarks>
    [ExportMethod]
    public void SetOutputMessage(string message)
    {
        if (messageVariable == null)
        {
            Log.Error("ChangeUserPasswordFormOutputMessageLogic", "Unable to find variable Message in ChangePasswordFormOutputMessage label");
            return;
        }

        messageVariable.Value = message;

        if (taskStarted)
        {
            task?.Cancel();
            taskStarted = false;
        }

        task.Start();
        taskStarted = true;
    }

    private DelayedTask task;
    private bool taskStarted = false;
    private IUAVariable messageVariable;
}
