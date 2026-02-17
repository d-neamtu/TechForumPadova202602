#region Using directives
using System;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.NetLogic;
using FTOptix.UI;
using FTOptix.OPCUAServer;
using FTOptix.RecipeX;
#endregion

public class ChangeUserFormOutputMessageLogic : BaseNetLogic
{
    public override void Start()
    {
        HideMessageLabel();
        loginResultCodeVariable = Owner.GetVariable("LoginResultCode");

        task = new DelayedTask(() =>
        {
            HideMessageLabel();
            taskStarted = false;
        }, 10000, LogicObject);
    }

    public override void Stop()
    {
        task?.Dispose();
    }

    /// <summary>
    /// This method sets the output message based on the provided result code.
    /// It updates the login result code variable, shows the message label, and
    /// manages the task lifecycle to ensure proper execution.
    /// </summary>
    /// <param name="resultCode">The result code to set in the login result code variable.</param>
    /// <remarks>
    /// The method handles task cancellation and initiation to ensure proper execution flow.
    /// </remarks>
    [ExportMethod]
    public void SetOutputMessage(int resultCode)
    {
        if (loginResultCodeVariable == null)
        {
            Log.Error("ChangeUserOutputMessageLogic", "Unable to find LoginResultCode variable in ChangeUserFormOutputMessage label");
            return;
        }

        loginResultCodeVariable.Value = resultCode;
        ShowMessageLabel();

        if (taskStarted)
        {
            task?.Cancel();
            taskStarted = false;
        }

        task.Start();
        taskStarted = true;
    }

    /// <summary>
    /// This method sets the visibility of the message label to visible.
    /// </summary>
    /// <remarks>
    /// The method retrieves the <see cref="Label"/> control from the owner and sets its <see cref="Visible"/> property to true.
    /// </remarks>
    private void ShowMessageLabel()
    {
        var messageLabel = (Label)Owner;
        messageLabel.Visible = true;
    }

    /// <summary>
    /// This method hides the message label by setting its visibility to false.
    /// </summary>
    /// <remarks>
    /// The method retrieves the label from the owner and hides it.
    /// </remarks>
    private void HideMessageLabel()
    {
        var messageLabel = (Label)Owner;
        messageLabel.Visible = false;
    }

    private DelayedTask task;
    private bool taskStarted = false;
    private IUAVariable loginResultCodeVariable;
}
