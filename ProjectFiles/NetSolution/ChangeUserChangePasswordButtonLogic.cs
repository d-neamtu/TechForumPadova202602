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
#endregion

public class ChangeUserChangePasswordButtonLogic : BaseNetLogic
{
    /// <summary>
    /// This method changes the password for the current user. It verifies that the new and confirm passwords are the same, and if they are, it proceeds to update the password. It also handles cases where the user has an expired password by updating the browse name accordingly. If the password change is successful, it closes the parent dialog.
    /// </summary>
    /// <param name="oldPassword">The current password of the user.</param>
    /// <param name="newPassword">The new password to set.</param>
    /// <param name="confirmPassword">The confirmation of the new password.</param>
    /// <param name="resultCode">An output parameter that receives the result code (0 for success, 7 for password mismatch).</param>
    [ExportMethod]
    public void PerformChangePassword(string oldPassword, string newPassword, string confirmPassword, out int resultCode)
    {
        if (newPassword != confirmPassword)
        {
            resultCode = 7;
            return;
        }

        var username = Session.User.BrowseName;
        try
        {
            var userWithExpiredPassword = Owner.GetAlias("UserWithExpiredPassword");
            if (userWithExpiredPassword != null)
                username = userWithExpiredPassword.BrowseName;
        }
        catch
        {
        }

        var result = Session.ChangePassword(username, newPassword, oldPassword);
        resultCode = (int)result.ResultCode;

        var parentDialog = Owner.Owner?.Owner?.Owner as Dialog;
        if (parentDialog != null && result.Success)
            parentDialog.Close();
    }
}
