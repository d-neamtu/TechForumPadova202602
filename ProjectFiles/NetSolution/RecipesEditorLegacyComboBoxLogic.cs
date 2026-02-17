#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.HMIProject;
using FTOptix.Retentivity;
using FTOptix.NativeUI;
using FTOptix.NetLogic;
using FTOptix.UI;
using FTOptix.Recipe;
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.SQLiteStore;
using FTOptix.Store;
using FTOptix.RecipeX;
#endregion

public class RecipesEditorLegacyComboBoxLogic : BaseNetLogic
{
	/// <summary>
	/// This method sets up the ComboBox to listen for variable changes and loads the selected recipe data.
	/// </summary>
	/// <remarks>
	/// The method casts the Owner to a ComboBox, attaches an event handler for the SelectedValueVariable variable change, and calls the LoadSelectedRecipeData() method to populate the data.
	/// </remarks>
	public override void Start()
	{
		comboBox = (ComboBox)Owner;
		comboBox.SelectedValueVariable.VariableChange += SelectedValueVariable_VariableChange;
		LoadSelectedRecipeData();
	}

	/// <summary>
	/// This method is called when the selected variable changes, and it loads the data for the selected recipe.
	/// </summary>
	/// <param name="sender">The object that triggered the event.</param>
	/// <param name="e">Event arguments containing information about the variable change.</param>
	private void SelectedValueVariable_VariableChange(object sender, VariableChangeEventArgs e)
	{
		LoadSelectedRecipeData();
	}

	/// <summary>
	/// This method loads selected recipe data based on the selected item in the combo box.
	/// It retrieves the recipe schema information, copies the recipe data to the edit model node,
	/// and handles potential null or empty conditions.
	/// </summary>
	/// <remarks>
	/// The method handles cases where the selected item is null or the text is empty by returning early.
	/// It retrieves the recipe schema object, then copies the recipe data to the edit model node.
	/// </remarks>
	private void LoadSelectedRecipeData()
	{
		LocalizedText selectedText = (LocalizedText)comboBox.SelectedValue;
		if (selectedText == null || string.IsNullOrEmpty(selectedText.Text))
			return;

		var recipeSchemaEditor = Owner.Owner;
		var recipeSchemaVariable = recipeSchemaEditor.GetVariable("RecipeSchema");
		if (recipeSchemaVariable == null)
			return;

		var recipeSchemaNodeId = (NodeId)recipeSchemaVariable.Value.Value;

		var recipeSchemaObject = (RecipeSchema)InformationModel.Get(recipeSchemaNodeId);
		if (recipeSchemaObject == null)
			return;

		var editModelNode = recipeSchemaObject.GetObject("EditModel");
		if (editModelNode == null)
			return;

		recipeSchemaObject.CopyFromStoreRecipe(comboBox.Text, editModelNode.NodeId, CopyErrorPolicy.BestEffortCopy);
	}

	public override void Stop()
	{
		comboBox.SelectedValueVariable.VariableChange -= SelectedValueVariable_VariableChange;
	}

	private ComboBox comboBox;
}
