#region Using directives
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FTOptix.CommunicationDriver;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Recipe;
using FTOptix.UI;
using UAManagedCore;
using FTOptix.RecipeX;
using OpcUa = UAManagedCore.OpcUa;
#endregion

public class RecipesEditorLegacyUISetup : BaseNetLogic
{
    /// <summary>
    /// This method prepares the UI for the Recipes Editor.
    /// It retrieves the recipe schema, cleans the UI, configures the ComboBox, and builds the UI from the schema.
    /// </summary>
    [ExportMethod]
    public void Setup()
    {
        try
        {
            // Check if we want to create a translation key for each element of the recipes editor
            var generateTranslationKeysVariable = Owner.GetVariable("GenerateTranslationKeys");
            generateTranslationKeys = generateTranslationKeysVariable != null && (bool)generateTranslationKeysVariable.Value;
            // The localization fallback locale is created to generate the text for the translation keys
            if (generateTranslationKeys)
            {
                try
                {
                    defaultLocale = ((string[])Project.Current.GetVariable("Localization/LocaleFallbackList").Value.Value)[0];
                }
                catch
                {
                    Log.Error("RecipesEditor", "Cannot get \"Translations fallback locales\" for the current project, disabling localizations");
                    generateTranslationKeys = false;
                }
            }

            schema = GetRecipeSchema();

            var rootNode = schema.Get("Root");
            if (rootNode == null)
                throw new Exception("Root node not found in recipe schema " + schema.BrowseName);

            var controlsContainer = GetControlsContainer();
            CleanUI(controlsContainer);

            ConfigureComboBox();

            var targetNode = GetTargetNode();

            BuildUIFromSchemaRecursive(rootNode, targetNode, controlsContainer, []);
        }
        catch (Exception e)
        {
            Log.Error("RecipesEditor", e.Message);
        }
    }

    /// <summary>
    /// Retrieves the RecipeSchema object based on the provided node ID.
    /// </summary>
    /// <returns>
    /// A RecipeSchema object representing the recipe schema.
    /// </returns>
    private RecipeSchema GetRecipeSchema()
    {
        var recipeSchemaPtr = Owner.GetVariable("RecipeSchema");
        if (recipeSchemaPtr == null)
            throw new Exception("RecipeSchema variable not found");

        var nodeId = (NodeId)recipeSchemaPtr.Value;
        if (nodeId == null)
            throw new Exception("RecipeSchema not set");

        var recipeSchemaNode = InformationModel.Get(nodeId);
        if (recipeSchemaNode == null)
            throw new Exception("Recipe not found");

        // Check if it has correct type
        if (recipeSchemaNode is not RecipeSchema recipeSchema)
            throw new Exception(recipeSchemaNode.BrowseName + " is not a recipe");

        return recipeSchema;
    }

    /// <summary>
    /// This method retrieves the controls container from a scroll view.
    /// If the scroll view is not found, it throws an exception.
    /// If the ColumnLayout is not found, it also throws an exception.
    /// </summary>
    /// <returns>
    /// A ColumnLayout object representing the controls container.
    /// </returns>
    private ColumnLayout GetControlsContainer()
    {
        var scrollView = Owner.Get("ScrollView");
        if (scrollView == null)
            throw new Exception("ScrollView not found");

        var controlsContainer = scrollView.Get<ColumnLayout>("ColumnLayout");
        if (controlsContainer == null)
            throw new Exception("ColumnLayout not found");

        return controlsContainer;
    }

    /// <summary>
    /// This method clears the children of the provided controls container and sets its height to 0, with horizontal alignment set to stretch.
    /// </summary>
    /// <param name="controlsContainer">The ColumnLayout object whose children are to be cleared.</param>
    private void CleanUI(ColumnLayout controlsContainer)
    {
        controlsContainer.Children.Clear();
        controlsContainer.Height = 0;
        controlsContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>
    /// This method configures a ComboBox by setting its model and query based on the schema.
    /// If the "Recipes ComboBox" is not found, an exception is thrown. If the "Store" property of the schema is not set, an exception is thrown.
    /// </summary>
    /// <remarks>
    /// The method sets the model of the ComboBox to the "Store" property of the schema, and sets the query to select the "Name" column from the appropriate table.
    /// </remarks>
    private void ConfigureComboBox()
    {
        // Set store as model for ComboBox
        var recipesComboBox = Owner.Get<ComboBox>("RecipesComboBox");
        if (recipesComboBox == null)
            throw new Exception("Recipes ComboBox not found");

        if (schema.Store == null)
            throw new Exception("Store of schema " + schema.BrowseName + " is not set");

        recipesComboBox.Model = schema.Store;

        // Set query of combobox with correct table name
        string tableName = !string.IsNullOrEmpty(schema.TableName) ? schema.TableName : schema.BrowseName;
        recipesComboBox.Query = "SELECT Name FROM \"" + tableName + "\"";
    }

    /// <summary>
    /// This method retrieves the target node from the schema and returns it.
    /// If the target node is not found or is not set, an exception is thrown.
    /// </summary>
    /// <returns>
    /// The target node from the schema, or throws an exception if not found or not set.
    /// </returns>
    private IUANode GetTargetNode()
    {
        var targetNode = schema.GetVariable("TargetNode");
        if (targetNode == null)
            throw new Exception("Target Node variable not found in schema " + schema.BrowseName);

        if ((NodeId)targetNode.Value == NodeId.Empty)
            throw new Exception("Target Node variable not set in schema " + schema.BrowseName);

        var target = InformationModel.Get(targetNode.Value);
        if (target == null)
            throw new Exception("Target " + targetNode.Value + " not found");

        return target;
    }

    /// <summary>
    /// This method builds the UI recursively based on the schema and the target node.
    /// It iterates through the children of the edit model node and creates controls for each variable.
    /// If the target node is not found, it continues to the next child.
    /// If the edit model child has children, it calls itself recursively to build the UI for those children.
    /// </summary>
    /// <param name="editModelNode">The IUANode representing the edit model node.</param>
    /// <param name="targetNode">The IUANode representing the target node.</param>
    /// <param name="controlsContainer">The Item representing the controls container.</param>
    /// <param name="browsePath">The list of strings representing the browse path.</param>
    private void BuildUIFromSchemaRecursive(IUANode editModelNode, IUANode targetNode, Item controlsContainer, List<string> browsePath)
    {
        foreach (var editModelChild in editModelNode.Children)
        {
            var targetChild = targetNode?.Get(editModelChild.BrowseName);
            var currentBrowsePath = browsePath.ToList();
            currentBrowsePath.Add(editModelChild.BrowseName);

            if (editModelChild.NodeClass == NodeClass.Variable &&
                ((targetChild != null && targetChild is not TagStructure) ||
                 (targetChild == null)))
            {
                var variable = (IUAVariable)editModelChild;
                var controls = BuildControl(variable, currentBrowsePath);
                foreach (var control in controls)
                {
                    controlsContainer.Height += control.Height;
                    controlsContainer.Add(control);
                }
            }

            if (editModelChild.Children.Count > 0)
                BuildUIFromSchemaRecursive(editModelChild, targetChild, controlsContainer, currentBrowsePath);
        }
    }

    /// <summary>
    /// Builds a list of UI controls based on the data type and dimensions of an IUAVariable.
    /// It creates appropriate controls (spinbox, switch, duration picker, text box) based on the data type and array dimensions.
    /// </summary>
    /// <param name="variable">The IUAVariable object representing the data source.</param>
    /// <param name="browsePath">The browse path for the variable, used to generate UI elements.</param>
    /// <returns>
    /// A list of UI controls (Item objects) based on the variable's data type and dimensions.
    /// If the array dimensions are unsupported, an error is logged.
    /// </returns>
    private List<Item> BuildControl(IUAVariable variable, List<string> browsePath)
    {
        var result = new List<Item>();

        var dataType = variable.Context.GetDataType(variable.DataType);
        uint[] arrayDimensions = variable.ArrayDimensions;

        if (arrayDimensions.Length == 0)
        {
            if (dataType.IsSubTypeOf(OpcUa.DataTypes.Integer) || dataType.IsSubTypeOf(OpcUa.DataTypes.UInteger))
                result.Add(BuildSpinbox(browsePath));
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Boolean))
                result.Add(BuildSwitch(browsePath));
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Duration))
                result.Add(BuildDurationPicker(browsePath));
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Float) || dataType.IsSubTypeOf(OpcUa.DataTypes.Double))
                result.Add(BuildSpinbox(browsePath, true));
            else
                result.Add(BuildTextBox(browsePath));
        }
        else if (arrayDimensions.Length == 1)
        {

            if (dataType.IsSubTypeOf(OpcUa.DataTypes.Integer) || dataType.IsSubTypeOf(OpcUa.DataTypes.UInteger))
            {
                foreach (var item in BuildSpinBoxArray(variable, browsePath))
                    result.Add(item);
            }
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Boolean))
            {
                foreach (var item in BuildSwitchArray(variable, browsePath))
                    result.Add(item);
            }
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Duration))
            {
                foreach (var item in BuildDurationPickerArray(variable, browsePath))
                    result.Add(item);
            }
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Float) || dataType.IsSubTypeOf(OpcUa.DataTypes.Double))
            {
                foreach (var item in BuildSpinBoxArray(variable, browsePath, true))
                    result.Add(item);
            }
            else
            {
                foreach (var item in BuildTextBoxArray(variable, browsePath))
                    result.Add(item);
            }
        }
        else
            Log.Error("RecipesEditor", "Unsupported multi-dimensional array parameter " + Log.Node(variable));

        return result;
    }

    /// <summary>
    /// This method builds a control panel for a given browse path and optional indexes.
    /// It creates a panel with a label and a text variable, and links them to the target node.
    /// The label text is set based on the browse path and indexes, and translations are handled if needed.
    /// The panel is styled with specific height and alignment properties.
    /// </summary>
    /// <param name="browsePath">A list of strings representing the browse path.</param>
    /// <param name="indexes">An optional array of indexes for the variable.</param>
    /// <returns>
    /// A panel object containing the label and text variable.
    /// </returns>
    private Item BuildControlPanel(List<string> browsePath, uint[] indexes = null)
    {
        var panel = InformationModel.MakeObject<Panel>(BrowsePathToBrowseName(browsePath));
        panel.Height = 40;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;

        var label = InformationModel.MakeObject<Label>("Path");
        if (!generateTranslationKeys)
        {
            label.Text = BrowsePathToNodePath(browsePath);
            if (indexes != null)
                label.Text += "_" + indexes[0];
        }
        else
        {
            if (indexes != null)
            {
                var labelTextArray = new LocalizedText(BrowsePathToNodePath(browsePath) + "_" + indexes[0], BrowsePathToNodePath(browsePath) + "_" + indexes[0], defaultLocale);
                if (!InformationModel.LookupTranslation(labelTextArray).HasTranslation)
                {
                    InformationModel.AddTranslation(labelTextArray);
                }
                label.LocalizedText = labelTextArray;
            }
            else
            {
                var labelText = new LocalizedText(BrowsePathToNodePath(browsePath), BrowsePathToNodePath(browsePath), defaultLocale);
                if (!InformationModel.LookupTranslation(labelText).HasTranslation)
                {
                    InformationModel.AddTranslation(labelText);
                }
                label.LocalizedText = labelText;
            }
        }

        label.LeftMargin = 20;
        label.VerticalAlignment = VerticalAlignment.Center;
        panel.Add(label);

        var target = GetTargetNode();
        var node = target;
        foreach (string nodeBrowseName in browsePath)
        {
            if (node == null)
            {
                Log.Error("RecipesEditor", "Node " + BrowsePathToNodePath(browsePath) + " not found in target " + target.BrowseName);
                continue;
            }

            node = node.Get(nodeBrowseName);
        }

        var variableTarget = (IUAVariable)node;

        var label2 = InformationModel.MakeObject<Label>("CurrentValue");
        if (indexes == null)
            label2.TextVariable.SetDynamicLink(variableTarget);
        else
            label2.TextVariable.SetDynamicLink(variableTarget, indexes[0]);

        label2.VerticalAlignment = VerticalAlignment.Center;
        label2.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Add(label2);

        return panel;
    }

    /// <summary>
    /// This method builds a duration picker control based on a given browse path.
    /// It constructs a panel, adds a duration picker component with specified layout and styling, and links it to a node path.
    /// </summary>
    /// <param name="browsePath">A list of strings representing the browse path.</param>
    /// <returns>
    /// A panel object containing the built duration picker control.
    /// </returns>
    private Item BuildDurationPicker(List<string> browsePath)
    {
        var panel = BuildControlPanel(browsePath);

        var durationPicker = InformationModel.MakeObject<DurationPicker>("DurationPicker");
        durationPicker.VerticalAlignment = VerticalAlignment.Center;
        durationPicker.HorizontalAlignment = HorizontalAlignment.Right;
        durationPicker.RightMargin = 100;
        durationPicker.Width = 100;

        string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
        MakeDynamicLink(durationPicker.GetVariable("Value"), aliasRelativeNodePath);
        panel.Add(durationPicker);

        return panel;
    }

    /// <summary>
    /// This method builds a list of control panels, each containing a DurationPicker widget.
    /// The DurationPicker is configured with specific alignment and margin settings, and a dynamic link is created
    /// to a node path relative to the browse path. Each panel represents a dimension of the array defined by the
    /// IUAVariable.
    /// </summary>
    /// <param name="variable">The IUAVariable object representing the array structure.</param>
    /// <param name="browsePath">A list of strings representing the browse path for node references.</param>
    /// <returns>
    /// A list of control panels, each containing a DurationPicker widget. The list is populated based on the
    /// dimensions of the array defined by the input variable.
    /// </returns>
    private List<Item> BuildDurationPickerArray(IUAVariable variable, List<string> browsePath)
    {
        var result = new List<Item>();

        uint[] arrayDimensions = variable.ArrayDimensions;
        for (uint index = 0; index < arrayDimensions[0]; ++index)
        {
            var panel = BuildControlPanel(browsePath, new uint[] { index });

            var durationPicker = InformationModel.MakeObject<DurationPicker>("DurationPicker");
            durationPicker.VerticalAlignment = VerticalAlignment.Center;
            durationPicker.HorizontalAlignment = HorizontalAlignment.Right;
            durationPicker.RightMargin = 100;
            durationPicker.Width = 100;

            string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
            MakeDynamicLink(durationPicker.GetVariable("Value"), aliasRelativeNodePath, index);
            panel.Add(durationPicker);

            result.Add(panel);
        }

        return result;
    }

    /// <summary>
    /// This method creates a Spinbox control with specified properties and integrates it into a panel.
    /// The Spinbox is configured to display a number with either a floating-point format (n6) or a
    /// fixed-number format (n0) based on the 'isFloat' parameter. It also sets up a dynamic link
    /// between the Spinbox's "Value" property and a relative node path in the browse path.
    /// </summary>
    /// <param name="browsePath">A list of strings representing the node path.</param>
    /// <param name="isFloat">A boolean indicating whether the Spinbox should use a floating-point format.</param>
    /// <returns>
    /// A panel containing the configured Spinbox control.
    /// </returns>
    private Item BuildSpinbox(List<string> browsePath, bool isFloat = false)
    {
        var panel = BuildControlPanel(browsePath);

        var spinbox = InformationModel.MakeObject<SpinBox>("SpinBox");
        spinbox.VerticalAlignment = VerticalAlignment.Center;
        spinbox.HorizontalAlignment = HorizontalAlignment.Right;
        spinbox.RightMargin = 100;
        spinbox.Width = 100;

        spinbox.Format = isFloat ? "n6" : "n0";

        string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
        MakeDynamicLink(spinbox.GetVariable("Value"), aliasRelativeNodePath);
        panel.Add(spinbox);

        return panel;
    }

    /// <summary>
    /// This method builds a list of spinbox controls based on an IUAVariable and a browse path.
    /// It creates a panel for each dimension of the array and adds a spinbox control to it.
    /// The spinbox is configured with appropriate formatting and linked to a node path.
    /// </summary>
    /// <param name="variable">The IUAVariable object representing the array to process.</param>
    /// <param name="browsePath">A list of strings representing the node path for the alias.</param>
    /// <param name="isFloat">A boolean indicating whether the spinbox should use floating-point formatting.</param>
    /// <returns>
    /// A list of panels, each containing a spinbox control. The spinbox is linked to the corresponding node in the browse path.
    /// </returns>
    private List<Item> BuildSpinBoxArray(IUAVariable variable, List<string> browsePath, bool isFloat = false)
    {
        var result = new List<Item>();

        uint[] arrayDimensions = variable.ArrayDimensions;
        for (uint index = 0; index < arrayDimensions[0]; ++index)
        {
            var panel = BuildControlPanel(browsePath, new uint[] { index });

            var spinbox = InformationModel.MakeObject<SpinBox>("SpinBox");
            spinbox.VerticalAlignment = VerticalAlignment.Center;
            spinbox.HorizontalAlignment = HorizontalAlignment.Right;
            spinbox.RightMargin = 100;
            spinbox.Width = 100;

            spinbox.Format = isFloat ? "n6" : "n0";

            string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
            MakeDynamicLink(spinbox.GetVariable("Value"), aliasRelativeNodePath, index);
            panel.Add(spinbox);

            result.Add(panel);
        }

        return result;
    }

    /// <summary>
    /// This method creates a text box with specific styling and links it to a relative node path.
    /// The text box is added to a control panel and returned.
    /// </summary>
    /// <param name="browsePath">A list of strings representing the browse path.</param>
    /// <returns>
    /// A panel containing the text box, which is styled and linked to a relative node path.
    /// </returns>
    /// <param name="browsePath">The browse path used to construct the relative node path.</param>
    private Item BuildTextBox(List<string> browsePath)
    {
        var panel = BuildControlPanel(browsePath);

        var textbox = InformationModel.MakeObject<TextBox>("Textbox");
        textbox.VerticalAlignment = VerticalAlignment.Center;
        textbox.HorizontalAlignment = HorizontalAlignment.Right;
        textbox.RightMargin = 100;
        textbox.Width = 100;

        string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
        MakeDynamicLink(textbox.GetVariable("Text"), aliasRelativeNodePath);
        panel.Add(textbox);

        return panel;
    }

    /// <summary>
    /// This method builds an array of control panels, each containing a text box, based on the dimensions of an IUAVariable.
    /// Each panel is created for each dimension in the variable's array, and a text box is added to the panel with specific styling and linking.
    /// </summary>
    /// <param name="variable">The IUAVariable object representing the variable to use for building the array.</param>
    /// <param name="browsePath">A list of strings representing the browse path for node identification.</param>
    /// <returns>
    /// A list of control panels, each containing a text box, representing the array structure based on the variable's dimensions.
    /// </returns>
    private List<Item> BuildTextBoxArray(IUAVariable variable, List<string> browsePath)
    {
        var result = new List<Item>();

        uint[] arrayDimensions = variable.ArrayDimensions;
        for (uint index = 0; index < arrayDimensions[0]; ++index)
        {
            var panel = BuildControlPanel(browsePath, new uint[] { index });

            var textbox = InformationModel.MakeObject<TextBox>("Textbox");
            textbox.VerticalAlignment = VerticalAlignment.Center;
            textbox.HorizontalAlignment = HorizontalAlignment.Right;
            textbox.RightMargin = 100;
            textbox.Width = 100;

            string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
            MakeDynamicLink(textbox.GetVariable("Text"), aliasRelativeNodePath, index);
            panel.Add(textbox);

            result.Add(panel);
        }

        return result;
    }

    /// <summary>
    /// This method creates a Switch control based on a browse path, sets its properties, and adds it to a panel.
    /// </summary>
    /// <param name="browsePath">A list of strings representing the browse path.</param>
    /// <returns>
    /// A panel containing the configured Switch control.
    /// </returns>
    /// <remarks>
    /// The method constructs a Switch control, sets its alignment and margin, creates a dynamic link to a node path,
    /// and adds the switch to a panel for display.
    /// </remarks>
    private Item BuildSwitch(List<string> browsePath)
    {
        var panel = BuildControlPanel(browsePath);

        var switchControl = InformationModel.MakeObject<Switch>("Switch");
        switchControl.VerticalAlignment = VerticalAlignment.Center;
        switchControl.HorizontalAlignment = HorizontalAlignment.Right;
        switchControl.RightMargin = 100;
        switchControl.Width = 60;

        string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
        MakeDynamicLink(switchControl.GetVariable("Checked"), aliasRelativeNodePath);
        panel.Add(switchControl);

        return panel;
    }

    /// <summary>
    /// This method builds an array of control panels, each representing a switch in a given IUA variable.
    /// Each control panel is created with a switch element that is dynamically linked to a node in the browse path.
    /// </summary>
    /// <param name="variable">The IUA variable from which to retrieve the array dimensions.</param>
    /// <param name="browsePath">A list of strings representing the browse path relative to the alias.</param>
    /// <returns>
    /// A list of control panels, each containing a switch element. Each switch is dynamically linked to a node in the browse path.
    /// </returns>
    private List<Item> BuildSwitchArray(IUAVariable variable, List<string> browsePath)
    {
        var result = new List<Item>();

        uint[] arrayDimensions = variable.ArrayDimensions;
        for (uint index = 0; index < arrayDimensions[0]; ++index)
        {
            var panel = BuildControlPanel(browsePath, new uint[] { index });

            var switchControl = InformationModel.MakeObject<Switch>("Switch");
            switchControl.VerticalAlignment = VerticalAlignment.Center;
            switchControl.HorizontalAlignment = HorizontalAlignment.Right;
            switchControl.RightMargin = 100;
            switchControl.Width = 60;

            string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
            MakeDynamicLink(switchControl.GetVariable("Checked"), aliasRelativeNodePath, index);
            panel.Add(switchControl);

            result.Add(panel);
        }

        return result;
    }

    /// <summary>
    /// Creates a dynamic link between a node and a path, and adds it as a reference to the parent.
    /// </summary>
    /// <param name="parent">The parent variable to which the dynamic link is added.</param>
    /// <param name="nodePath">The path to assign to the dynamic link.</param>
    private void MakeDynamicLink(IUAVariable parent, string nodePath)
    {
        var dynamicLink = InformationModel.MakeVariable<DynamicLink>("DynamicLink", FTOptix.Core.DataTypes.NodePath);
        dynamicLink.Value = nodePath;
        dynamicLink.Mode = DynamicLinkMode.ReadWrite;
        parent.Refs.AddReference(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink, dynamicLink);
    }

    /// <summary>
    /// This method creates a dynamic link by appending the index to the node path.
    /// </summary>
    /// <param name="parent">The parent IUAVariable object.</param>
    /// <param name="nodePath">The original node path string.</param>
    /// <param name="index">The index to append to the node path.</param>
    private void MakeDynamicLink(IUAVariable parent, string nodePath, uint index)
    {
        MakeDynamicLink(parent, nodePath + "[" + index.ToString() + "]");
    }

    /// <summary>
    /// This method constructs a path string by combining a node path relative to an alias with a browse path.
    /// The method escapes the browse name and appends it to the path, then appends the browse path.
    /// </summary>
    /// <param name="browsePath">A list of strings representing the browse path.</param>
    /// <returns>
    /// A string representing the node path relative to the alias, formatted as "{escapedBrowseName}/browsePath".
    /// </returns>
    private string MakeNodePathRelativeToAlias(List<string> browsePath)
    {
        return "{" + NodePath.EscapeNodePathBrowseName(schema.BrowseName) + "}/" + BrowsePathToNodePath(browsePath);
    }

    /// <summary>
    /// This method constructs a node path from a list of browse path entries.
    /// It escapes each entry and joins them with slashes.
    /// </summary>
    /// <param name="browsePath">A list of strings representing the browse path.</param>
    /// <returns>
    /// A string representing the escaped node path, with entries separated by slashes.
    /// </returns>
    private string BrowsePathToNodePath(List<string> browsePath)
    {
        if (browsePath.Count == 1)
            return NodePath.EscapeNodePathBrowseName(browsePath[0]);

        var result = new StringBuilder();

        for (int i = 0; i < browsePath.Count; ++i)
        {
            _ = result.Append(NodePath.EscapeNodePathBrowseName(browsePath[i]));
            if (i != browsePath.Count - 1)
                _ = result.Append('/');
        }

        return result.ToString();
    }

    /// <summary>
    /// This method constructs a node path by escaping each part of the browse path and joining them with underscores.
    /// If the browse path contains only one element, it returns the escaped name directly.
    /// </summary>
    private string BrowsePathToBrowseName(List<string> browsePath)
    {
        if (browsePath.Count == 1)
            return NodePath.EscapeNodePathBrowseName(browsePath[0]);

        var result = new StringBuilder();

        for (int i = 0; i < browsePath.Count; ++i)
        {
            _ = result.Append(NodePath.EscapeNodePathBrowseName(browsePath[i]));
            if (i != browsePath.Count - 1)
                _ = result.Append('_');
        }

        return result.ToString();
    }

    private RecipeSchema schema;
    private bool generateTranslationKeys;
    private string defaultLocale;
}
