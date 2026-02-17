#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.HMIProject;
using FTOptix.Core;
using FTOptix.NetLogic;
using FTOptix.Recipe;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using FTOptix.Store;
using FTOptix.RecipeX;

#endregion

public class RecipeControllerLegacy : BaseNetLogic
{

    public override void Start()
    {
        variableSynchronizer = new RemoteVariableSynchronizer();

        ApplyFromDBTrigger = LogicObject.GetVariable("ApplyFromDBTrigger");
        if (ApplyFromDBTrigger != null)
            variableSynchronizer.Add(ApplyFromDBTrigger);

        ApplyFromEditModelTrigger = LogicObject.GetVariable("ApplyFromEditModelTrigger");
        if (ApplyFromEditModelTrigger != null)
            variableSynchronizer.Add(ApplyFromEditModelTrigger);

        DeleteTrigger = LogicObject.GetVariable("DeleteTrigger");
        if (DeleteTrigger != null)
            variableSynchronizer.Add(DeleteTrigger);

        ExportTrigger = LogicObject.GetVariable("ExportTrigger");
        if (ExportTrigger != null)
            variableSynchronizer.Add(ExportTrigger);

        ImportTrigger = LogicObject.GetVariable("ImportTrigger");
        if (ImportTrigger != null)
            variableSynchronizer.Add(ImportTrigger);

        LoadFromPLCTrigger = LogicObject.GetVariable("LoadFromPLCTrigger");
        if (LoadFromPLCTrigger != null)
            variableSynchronizer.Add(LoadFromPLCTrigger);

        SaveToDBTrigger = LogicObject.GetVariable("SaveToDBTrigger");
        if (SaveToDBTrigger != null)
            variableSynchronizer.Add(SaveToDBTrigger);

        SelectFromDBTrigger = LogicObject.GetVariable("SelectFromDBTrigger");
        if (SelectFromDBTrigger != null)
            variableSynchronizer.Add(SelectFromDBTrigger);
    }

    public override void Stop()
    {
        lock (lockObject)
        {
            task?.Dispose();
        }
    }

    /// <summary>
    /// Selects a recipe from the database and loads it into the EditModel.
    /// </summary>
    /// <param name="RecipeName">The name of the recipe to select.</param>
    /// <param name="ErrorPolicy">The error policy to use during the selection process.</param>
    [ExportMethod]
    public void SelectFromDB(string RecipeName, CopyErrorPolicy ErrorPolicy)
    {
        if (SelectFromDBTrigger != null)
            SelectFromDBTrigger.Value = 0;

        FTOptix.Recipe.RecipeSchema schema = GetRecipeSchema();
        if (schema == null) return;

        string name = RecipeName;
        if (String.IsNullOrEmpty(RecipeName))
        {
            name = GetRecipeName();
            if (String.IsNullOrEmpty(name))
            {
                SetFeedback(2, GetLocalizedTextString("RecipeControllerLegacyEmptyRecipeName"));
                return;
            }
        }

        var editModelNode = schema.GetObject("EditModel");
        if (editModelNode == null)
            return;

        schema.CopyFromStoreRecipe(name, editModelNode.NodeId, ErrorPolicy);
    }

    /// <summary>
    /// Saves a recipe from the EditModel to the database. Creates the recipe if it doesn't exist.
    /// </summary>
    /// <param name="RecipeName">The name of the recipe to save.</param>
    /// <param name="ErrorPolicy">The error policy to use during the save process.</param>
    [ExportMethod]
    public void SaveToDB(string RecipeName, CopyErrorPolicy ErrorPolicy)
    {
        if (SaveToDBTrigger != null)
            SaveToDBTrigger.Value = 0;

        FTOptix.Recipe.RecipeSchema schema = GetRecipeSchema();
        if (schema == null) return;

        string name = RecipeName;
        if (String.IsNullOrEmpty(RecipeName))
        {
            name = GetRecipeName();
            if (String.IsNullOrEmpty(name))
            {
                SetFeedback(2, GetLocalizedTextString("RecipeControllerLegacyEmptyRecipeName"));
                return;
            }
        }

        try
        {
            var store = GetRecipeStore(schema);
            var editModel = GetEditModel(schema);

            if (store != null && editModel != null)
            {
                if (RecipeExistsInStore(store, schema, name))
                {
                    // Save Recipe
                    schema.CopyToStoreRecipe(editModel.NodeId, name, ErrorPolicy);
                    SetFeedback(1, $"{GetLocalizedTextString("RecipeControllerLegacyRecipe")} {name} {GetLocalizedTextString("RecipeControllerLegacySaved")}");
                }
                else
                {
                    // Create recipe
                    schema.CreateStoreRecipe(name);
                    // Save Recipe
                    schema.CopyToStoreRecipe(editModel.NodeId, name, ErrorPolicy);
                    SetFeedback(1, $"{GetLocalizedTextString("RecipeControllerLegacyRecipe")} {name} {GetLocalizedTextString("RecipeControllerLegacyCreatedAndSaved")}");
                }
            }
        }
        catch (Exception e)
        {
            SetFeedback(2, e.Message);
        }
    }

    /// <summary>
    /// Deletes a recipe from the database.
    /// </summary>
    /// <param name="RecipeName">The name of the recipe to delete.</param>
    [ExportMethod]
    public void Delete(string RecipeName)
    {
        if (DeleteTrigger != null)
            DeleteTrigger.Value = 0;

        FTOptix.Recipe.RecipeSchema schema = GetRecipeSchema();
        if (schema == null) return;

        string name = RecipeName;
        if (String.IsNullOrEmpty(RecipeName))
        {
            name = GetRecipeName();
            if (String.IsNullOrEmpty(name))
            {
                SetFeedback(2, GetLocalizedTextString("RecipeControllerLegacyEmptyRecipeName"));
                return;
            }
        }

        try
        {
            schema.DeleteStoreRecipe(name);
            SetFeedback(1, $"{GetLocalizedTextString("RecipeControllerLegacyRecipe")} {name} {GetLocalizedTextString("RecipeControllerLegacyDeleted")}");
        }
        catch (Exception e)
        {
            SetFeedback(2, e.Message);
        }
    }

    /// <summary>
    /// Loads the current recipe values from the PLC target into the EditModel.
    /// </summary>
    /// <param name="ErrorPolicy">The error policy to use during the copy operation.</param>
    [ExportMethod]
    public void LoadFromPLC(CopyErrorPolicy ErrorPolicy)
    {
        if (LoadFromPLCTrigger != null)
            LoadFromPLCTrigger.Value = 0;

        FTOptix.Recipe.RecipeSchema schema = GetRecipeSchema();
        if (schema == null) return;

        try
        {
            var editModel = GetEditModel(schema);
            if (editModel != null)
            {
                schema.Copy(schema.TargetNode, editModel.NodeId, ErrorPolicy);
                SetFeedback(1, GetLocalizedTextString("RecipeControllerLegacyRecipeLoaded"));
            }
        }
        catch (Exception e)
        {
            SetFeedback(2, e.Message);
        }
    }

    /// <summary>
    /// Applies the recipe values from the EditModel to the PLC target.
    /// </summary>
    /// <param name="ErrorPolicy">The error policy to use during the copy operation.</param>
    [ExportMethod]
    public void ApplyFromEditModel(CopyErrorPolicy ErrorPolicy)
    {
        if (ApplyFromEditModelTrigger != null)
            ApplyFromEditModelTrigger.Value = 0;

        FTOptix.Recipe.RecipeSchema schema = GetRecipeSchema();
        if (schema == null) return;

        try
        {
            var editModel = GetEditModel(schema);
            if (editModel != null)
            {
                schema.CopyFromEditModel(editModel.NodeId, schema.TargetNode, ErrorPolicy);
                SetFeedback(1, GetLocalizedTextString("RecipeControllerLegacyRecipeApplied"));
            }
        }
        catch (Exception e)
        {
            SetFeedback(2, e.Message);
        }
    }

    /// <summary>
    /// Applies a recipe from the database directly to the PLC target.
    /// </summary>
    /// <param name="RecipeName">The name of the recipe to apply.</param>
    /// <param name="ErrorPolicy">The error policy to use during the application process.</param>
    [ExportMethod]
    public void ApplyFromDB(string RecipeName, CopyErrorPolicy ErrorPolicy)
    {
        if (ApplyFromDBTrigger != null)
            ApplyFromDBTrigger.Value = 0;

        FTOptix.Recipe.RecipeSchema schema = GetRecipeSchema();
        if (schema == null) return;

        string name = RecipeName;
        if (String.IsNullOrEmpty(RecipeName))
        {
            name = GetRecipeName();
            if (String.IsNullOrEmpty(name))
            {
                SetFeedback(2, GetLocalizedTextString("RecipeControllerLegacyEmptyRecipeName"));
                return;
            }
        }

        try
        {
            schema.CopyFromStoreRecipe(name, schema.TargetNode, ErrorPolicy);
            SetFeedback(1, GetLocalizedTextString("RecipeControllerLegacyRecipeApplied"));
            LogicObject.GetVariable("LastAppliedRecipe").SetValueNoPermissions(name);
        }
        catch (Exception e)
        {
            SetFeedback(2, e.Message);
        }
    }

    /// <summary>
    /// Exports recipes to a CSV file, handling the file path, separator, and wrap fields.
    /// </summary>
    [ExportMethod]
    public void Export()
    {
        if (ExportTrigger != null)
            ExportTrigger.Value = 0;

        FTOptix.Recipe.RecipeSchema schema = GetRecipeSchema();
        if (schema == null) return;

        var csvPath = GetCSVFilePath();

        if (string.IsNullOrEmpty(csvPath))
        {
            SetFeedback(2, "Unable to export recipes: please specify the output CSV file");
            return;
        }

        var separator = GetSeparator();
        if (separator == '.')
        {
            SetFeedback(2, "Unable to export recipes: CSV separator " + separator + " is not supported");
            return;
        }

        bool wrapFields = GetWrapFields();

        var storeObject = GetStoreObject(schema);
        if (storeObject == null)
        {
            SetFeedback(2, "Unable to export recipes to CSV file: Store object not found");
            return;
        }

        var tableName = GetTableName(schema);

        // Retrieve all recipes from table
        object[,] resultSet;
        string[] header;
        string selectQuery = "SELECT * FROM \"" + tableName + "\"";
        storeObject.Query(selectQuery, out header, out resultSet);

        if (header == null || resultSet == null || resultSet.Length == 0)
        {
            // No recipes or wrong result
            SetFeedback(2, $"No recipes found to export. Store {storeObject.BrowseName} has no recipes or an error occurred");
            return;
        }

        // Check column names
        foreach (var columnName in header)
        {
            if (columnName.Contains(separator.ToString()))
            {
                SetFeedback(2, "Unable to export recipes to CSV file: the name of parameter " +
                    columnName + " contains the CSV separator " + separator + ". Please specify a different CSV separator");
                return;
            }
        }

        var rowCount = resultSet.GetLength(0);
        var columnCount = resultSet.GetLength(1);

        try
        {
            using (var csvWriter = new CSVFileWriter(csvPath) { FieldDelimiter = separator, WrapFields = wrapFields })
            {
                // Write header
                csvWriter.WriteLine(header);

                // For each recipe write a line to the CSV file
                for (var r = 0; r < rowCount; ++r)
                {
                    var currentRow = new string[columnCount];

                    for (var c = 0; c < columnCount; ++c)
                    {
                        var recipeParameter = Convert.ToString(resultSet[r, c], CultureInfo.InvariantCulture);
                        currentRow[c] = string.IsNullOrEmpty(recipeParameter) ? "NULL" : recipeParameter;
                    }

                    csvWriter.WriteLine(currentRow);
                }
            }
            SetFeedback(1, "Recipes successfully exported to " + csvPath);
        }
        catch (Exception e)
        {
            SetFeedback(2, "Unable to write CSV file: " + e.Message);
        }
    }

    /// <summary>
    /// Imports recipes from a CSV file, handling the file path, separator, and wrap fields.
    /// </summary>
    [ExportMethod]
    public void Import()
    {
        if (ImportTrigger != null)
            ImportTrigger.Value = 0;

        FTOptix.Recipe.RecipeSchema schema = GetRecipeSchema();
        if (schema == null) return;

        var csvPath = GetCSVFilePath();

        if (string.IsNullOrEmpty(csvPath))
        {
            SetFeedback(2, "Unable to import recipes: please specify the input CSV file");
            return;
        }

        var separator = GetSeparator();
        if (separator == '.')
        {
            SetFeedback(2, "Unable to import recipes: CSV separator . is not supported");
            return;
        }

        bool wrapFields = GetWrapFields();

        if (!File.Exists(csvPath))
        {
            SetFeedback(2, "Unable to import recipes: CSV file " + csvPath + " not found");
            return;
        }

        var storeObject = GetStoreObject(schema);
        if (storeObject == null)
        {
            SetFeedback(2, "Unable to import recipes from CSV file: Store object not found");
            return;
        }

        var tableName = GetTableName(schema);
        var tableNode = storeObject.Tables.Get<Table>(tableName);
        if (tableNode == null)
        {
            SetFeedback(2, "Unable to import recipes from CSV file: table '" + tableName + "' not found in Store " + storeObject.BrowseName);
            return;
        }

        DeleteAlreadyExistingRecipes(storeObject, tableName, csvPath, separator, wrapFields);

        try
        {
            using (var csvReader = new CSVFileReader(csvPath) { FieldDelimiter = separator, IgnoreMalformedLines = true, WrapFields = wrapFields })
            {
                if (csvReader.EndOfFile())
                {
                    SetFeedback(2, "The file " + csvPath + " is empty");
                    return;
                }

                var header = csvReader.ReadLine();
                if (header == null || header.Count == 0)
                {
                    SetFeedback(2, "Error importing recipes. Recipe header does not contain any value or CSV file has an incorrect format");
                    return;
                }

                while (!csvReader.EndOfFile())
                {
                    var parameters = csvReader.ReadLine();

                    if (parameters.Count != header.Count)
                    {
                        // invalid line
                        continue;
                    }

                    var recipeName = parameters[0];

                    var values = new object[1, header.Count];
                    values[0, 0] = recipeName;

                    for (var p = 1; p < header.Count; ++p)
                    {
                        // Remove "/" from the beginning of column name
                        var parameterBrowsePath = header[p].Substring(1);

                        if (parameters[p] == "NULL")
                        {
                            // NULL field
                            continue;
                        }

                        var recipeParameterVariable = GetVariableFromRecipeSchema(schema.Root, parameterBrowsePath);
                        if (recipeParameterVariable == null)
                        {
                            Log.Warning("RecipeManager", $"Could not find {parameterBrowsePath} in recipe {recipeName}");
                            continue;
                        }

                        if (IsVariableLinearArray(recipeParameterVariable))
                        {
                            var arraySize = recipeParameterVariable.ArrayDimensions[0];
                            ImportArrayVariable(recipeParameterVariable, p, values, parameters);
                            p += (int)arraySize - 1;
                        }
                        else
                        {
                            try
                            {
                                values[0, p] = ConvertVariableValueToObject(recipeParameterVariable, parameters[p]);
                            }
                            catch (Exception e)
                            {
                                Log.Warning("RecipeManager", "Unable to import parameter '" + parameterBrowsePath +
                                                "' of recipe '" + recipeName +
                                                "': unsupported data type " + e.Message);
                            }
                        }
                    }

                    tableNode.Insert(header.ToArray(), values);
                }

                SetFeedback(1, "Recipes successfully imported to " + tableNode.BrowseName);
            }
        }
        catch (Exception e)
        {
            SetFeedback(2, "Unable to read CSV file " + csvPath + ": " + e.Message);
        }
    }

    /// <summary>
    /// Sets feedback for the recipe controller, including the result and message.
    /// Automatically resets feedback after 5 seconds.
    /// </summary>
    /// <param name="result">The result code (0 for idle, 1 for OK, 2 for error).</param>
    /// <param name="message">The message to display.</param>
    private void SetFeedback(byte result, string message)
    {
        // result = 0 --> Idle
        // result = 1 --> Ok
        // result = 2 --> Error
        if (result == 2)
            Log.Error("RecipeControllerLegacy", message);
        LogicObject.GetVariable("Result").SetValueNoPermissions(result);
        LogicObject.GetVariable("Message").SetValueNoPermissions(message);

        lock (lockObject)
        {
            task?.Dispose();
            task = new DelayedTask(() => { ResetFeedback(); }, 5000, LogicObject);
            task.Start();
        }
    }

    /// <summary>
    /// Resets the feedback by setting the "Result" variable to 0 and the "Message" variable to an empty string.
    /// </summary>
    private void ResetFeedback()
    {
        LogicObject.GetVariable("Result").SetValueNoPermissions(0);
        LogicObject.GetVariable("Message").SetValueNoPermissions(string.Empty);
    }

    /// <summary>
    /// Retrieves the recipe schema from the owner or from a variable in the logic object.
    /// </summary>
    /// <returns>The recipe schema, or null if not found.</returns>
    private FTOptix.Recipe.RecipeSchema GetRecipeSchema()
    {
        var schema = Owner as FTOptix.Recipe.RecipeSchema;

        if (schema == null)
        {
            var recipeSchemaPtr = LogicObject.GetVariable("RecipeSchema");
            if (recipeSchemaPtr == null)
                SetFeedback(2, GetLocalizedTextString("RecipesEditorLegacyRecipeSchemaNotFound"));

            var nodeId = (NodeId)recipeSchemaPtr.Value;
            if (nodeId == null)
                SetFeedback(2, GetLocalizedTextString("RecipesEditorLegacyRecipeSchemaNotSet"));

            var recipeSchema = InformationModel.Get(nodeId);
            if (recipeSchema == null)
                SetFeedback(2, GetLocalizedTextString("RecipesEditorLegacyRecipeNotFound"));

            // Check if it has correct type
            schema = recipeSchema as FTOptix.Recipe.RecipeSchema;
            if (schema == null)
                SetFeedback(2, $"{recipeSchema.BrowseName} {GetLocalizedTextString("RecipesEditorLegacyNotARecipe")}");
        }

        return schema;
    }

    /// <summary>
    /// Retrieves the name of a recipe from the LogicObject's RecipeName variable.
    /// </summary>
    /// <returns>The name of the recipe.</returns>
    private string GetRecipeName()
    {
        var logicObjectVariable = LogicObject.GetVariable("RecipeName");
        return logicObjectVariable.Value;
    }

    /// <summary>
    /// Retrieves the recipe store from the given schema.
    /// </summary>
    /// <param name="schema">The schema from which to retrieve the store.</param>
    /// <returns>The recipe store, or null if not found.</returns>
    private FTOptix.Store.Store GetRecipeStore(FTOptix.Recipe.RecipeSchema schema)
    {
        // Check if the store is set
        if (schema.Store == NodeId.Empty)
            SetFeedback(2, $"{GetLocalizedTextString("RecipesEditorLegacyStoreOfSchema")} {schema.BrowseName} {GetLocalizedTextString("RecipesEditorLegacyNotSet")}");

        // Get store node
        var storeNode = InformationModel.GetObject(schema.Store);
        if (storeNode == null)
            SetFeedback(2, $"{GetLocalizedTextString("RecipesEditorLegacyStore")} {schema.Store} {GetLocalizedTextString("RecipesEditorLegacyNotFound")}");

        // Check that it is actually a store
        var store = storeNode as FTOptix.Store.Store;
        if (store == null)
            SetFeedback(2, $"{GetLocalizedTextString("RecipesEditorLegacyStore")} {schema.Store} {GetLocalizedTextString("RecipesEditorLegacyNotAStore")}");

        return store;
    }

    /// <summary>
    /// Retrieves the "EditModel" node from the given schema.
    /// </summary>
    /// <param name="schema">The schema from which to retrieve the "EditModel".</param>
    /// <returns>The "EditModel" node, or null if not found.</returns>
    private IUANode GetEditModel(FTOptix.Recipe.RecipeSchema schema)
    {
        var editModel = schema.Get("EditModel");
        if (editModel == null)
            SetFeedback(2, $"{GetLocalizedTextString("RecipesEditorLegacyEditModelOfSchema")} {schema.BrowseName} {GetLocalizedTextString("RecipesEditorLegacyNotFound")}");

        return editModel;
    }

    /// <summary>
    /// Checks if a recipe with the specified name already exists in the store.
    /// </summary>
    /// <param name="store">The store in which to check for the recipe.</param>
    /// <param name="schema">The schema of the recipe.</param>
    /// <param name="recipeName">The name of the recipe to check.</param>
    /// <returns>True if the recipe exists, otherwise false.</returns>
    private bool RecipeExistsInStore(FTOptix.Store.Store store, FTOptix.Recipe.RecipeSchema schema, string recipeName)
    {
        // Perform query on the store in order to check if the recipe already exists
        object[,] resultSet;
        string[] header;
        var tableName = !String.IsNullOrEmpty(schema.TableName) ? schema.TableName : schema.BrowseName;
        store.Query("SELECT * FROM \"" + tableName + "\" WHERE Name = \'" + recipeName.Replace("'", "''") + "\'", out header, out resultSet);
        var rowCount = resultSet != null ? resultSet.GetLength(0) : 0;
        return rowCount > 0;
    }

    /// <summary>
    /// Retrieves the localized text for a given text ID.
    /// </summary>
    /// <param name="textId">The ID of the text to look up.</param>
    /// <returns>A string containing the localized text.</returns>
    private string GetLocalizedTextString(string textId)
    {
        var myLocalizedText = new LocalizedText(textId);
        return InformationModel.LookupTranslation(myLocalizedText).Text;
    }


    #region ImportExport

    /// <summary>
    /// Retrieves the CSV file path from the LogicObject's CSVFile variable.
    /// </summary>
    /// <returns>The URI of the CSV file path, or an empty string if the variable is not found.</returns>
    private string GetCSVFilePath()
    {
        var csvPathVariable = LogicObject.GetVariable("CSVFile");
        if (csvPathVariable == null)
        {
            SetFeedback(2, "CSVFile variable not found");
            return "";
        }

        var finalPath = new ResourceUri(csvPathVariable.Value).Uri;

        if (string.IsNullOrEmpty(finalPath))
            throw new ArgumentNullException("CSV file path could not be generated from the provided ResourceUri");

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            finalPath = finalPath.Replace('/', '\\');
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            finalPath = finalPath.Replace('\\', '/');

        return finalPath;
    }

    /// <summary>
    /// Retrieves the CSV separator character from the LogicObject's CSVSeparator variable.
    /// </summary>
    /// <returns>The separator character, or ',' if the string length is not exactly 1.</returns>
    private char GetSeparator()
    {
        var separatorVariable = LogicObject.GetVariable("CSVSeparator");
        string separatorString = separatorVariable.Value;

        return (separatorString.Length != 1) ? ',' : separatorString[0];
    }

    /// <summary>
    /// Retrieves the value of the "WrapFields" variable from the LogicObject.
    /// </summary>
    /// <returns>The value of the WrapFields variable, or false if the variable is not found.</returns>
    private bool GetWrapFields()
    {
        var wrapFieldsVariable = LogicObject.GetVariable("WrapFields");
        if (wrapFieldsVariable == null)
        {
            Log.Error("RecipeManager", "WrapFields variable not found");
            return false;
        }

        return wrapFieldsVariable.Value;
    }

    /// <summary>
    /// Retrieves the Store object from the InformationModel based on the RecipeSchema's Store NodeId.
    /// </summary>
    /// <param name="recipeSchema">The recipe schema containing the store NodeId.</param>
    /// <returns>A Store object if found, otherwise null.</returns>
    private Store GetStoreObject(FTOptix.Recipe.RecipeSchema recipeSchema)
    {
        NodeId storeNodeId = recipeSchema.Store;
        var storeObject = InformationModel.Get(storeNodeId);
        if (storeObject == null)
            return null;

        return storeObject as Store;
    }

    /// <summary>
    /// Retrieves the table name from the provided RecipeSchema.
    /// Uses the BrowseName if TableName is empty or null.
    /// </summary>
    /// <param name="recipeSchema">The RecipeSchema object containing the table name information.</param>
    /// <returns>The table name from the schema.</returns>
    private string GetTableName(FTOptix.Recipe.RecipeSchema recipeSchema)
    {
        var tableName = recipeSchema.TableName;

        if (string.IsNullOrEmpty(tableName))
            tableName = recipeSchema.BrowseName;

        return tableName;
    }

    /// <summary>
    /// Deletes recipes from the store that match recipe names found in the CSV file.
    /// This prepares the store for importing recipes without duplicates.
    /// </summary>
    /// <param name="storeObject">The store object representing the database connection.</param>
    /// <param name="tableName">The name of the table from which recipes will be deleted.</param>
    /// <param name="inputFile">The path to the input CSV file.</param>
    /// <param name="separator">The character used to separate fields in the CSV file.</param>
    /// <param name="wrapFields">A boolean indicating whether fields are wrapped in quotes in the CSV file.</param>
    private void DeleteAlreadyExistingRecipes(Store storeObject, string tableName, string inputFile, char separator, bool wrapFields)
    {
        var recipes = GetRecipes(storeObject, tableName);

        try
        {
            using (var csvReader = new CSVFileReader(inputFile) { FieldDelimiter = separator, IgnoreMalformedLines = true, WrapFields = wrapFields })
            {
                if (csvReader.EndOfFile())
                    return;

                var header = csvReader.ReadLine();
                if (header == null || header.Count == 0)
                {
                    SetFeedback(2, "Error deleting existing recipes. Recipe header does not contain any value or CSV file has an incorrect format");
                    return;
                }

                while (!csvReader.EndOfFile())
                {
                    var parameters = csvReader.ReadLine();

                    if (parameters.Count != header.Count)
                    {
                        // invalid line
                        continue;
                    }

                    // Delete recipe if it already exists
                    var recipeName = parameters[0];
                    if (recipes.Contains(recipeName))
                        DeleteRecipeForImport(storeObject, tableName, recipeName);
                }
            }
        }
        catch (Exception e)
        {
            SetFeedback(2, "Unable to read CSV file " + inputFile + ": " + e.Message);
        }
    }

    /// <summary>
    /// Deletes a recipe from the specified table in the given store.
    /// </summary>
    /// <param name="storeObject">The store object representing the database connection.</param>
    /// <param name="tableName">The name of the table from which the recipe will be deleted.</param>
    /// <param name="recipeName">The name of the recipe to delete.</param>
    private void DeleteRecipeForImport(Store storeObject, string tableName, string recipeName)
    {
        object[,] resultSet;
        string[] header;

        string deleteQuery = "DELETE FROM \"" + tableName + "\" WHERE Name = '" + recipeName.Replace("'", "''") + "'";
        storeObject.Query(deleteQuery, out header, out resultSet);
    }

    /// <summary>
    /// Retrieves all recipe names from the specified table in the given store.
    /// </summary>
    /// <param name="storeObject">The store object representing the database connection.</param>
    /// <param name="tableName">The name of the table from which to retrieve the recipes.</param>
    /// <returns>A HashSet containing all recipe names.</returns>
    private HashSet<string> GetRecipes(Store storeObject, string tableName)
    {
        HashSet<string> result = new HashSet<string>();

        // Retrieve all recipes from table
        object[,] resultSet;
        string[] header;
        storeObject.Query("SELECT Name FROM \"" + tableName + "\"", out header, out resultSet);

        if (resultSet == null || resultSet.Length == 0)
        {
            // No recipes
            return result;
        }

        var rowCount = resultSet.GetLength(0);
        var columnCount = resultSet.GetLength(1);

        if (columnCount == 0)
            return result;

        for (var r = 0; r < rowCount; ++r)
            result.Add(resultSet[r, 0] as String);

        return result;
    }

    /// <summary>
    /// Retrieves an IUAVariable from the recipe schema root.
    /// If not found and the name contains an underscore, attempts to find the base array variable.
    /// </summary>
    /// <param name="recipeSchemaRoot">The root IUAObject of the recipe schema.</param>
    /// <param name="variableName">The name of the variable to retrieve.</param>
    /// <returns>An IUAVariable object that matches the specified name, or null if not found.</returns>
    private IUAVariable GetVariableFromRecipeSchema(IUAObject recipeSchemaRoot, string variableName)
    {
        var recipeVariable = recipeSchemaRoot.GetVariable(variableName);
        if (recipeVariable == null)
        {
            var underscoreIndex = variableName.LastIndexOf("_");
            if (underscoreIndex > -1)
            {
                var arrayVariableName = variableName.Substring(0, underscoreIndex);
                recipeVariable = recipeSchemaRoot.GetVariable(arrayVariableName);
            }
        }

        return recipeVariable;
    }

    /// <summary>
    /// Checks if the given IUAVariable is a linear array (has only one dimension).
    /// </summary>
    /// <param name="variable">The IUAVariable to check.</param>
    /// <returns>True if the variable is a one-dimensional array, otherwise false.</returns>
    private bool IsVariableLinearArray(IUAVariable variable)
    {
        return variable.ActualArrayDimensions.Length == 1;
    }

    /// <summary>
    /// Imports array variable values from the CSV parameter list into the values array.
    /// </summary>
    /// <param name="arrayVariable">The IUAVariable object representing the array variable.</param>
    /// <param name="startIndex">The starting index in the values array to begin importing.</param>
    /// <param name="values">A 2D array where the values will be assigned.</param>
    /// <param name="parameterList">A list of strings from the CSV containing the parameter values.</param>
    private void ImportArrayVariable(IUAVariable arrayVariable, int startIndex, object[,] values, List<string> parameterList)
    {
        var arraySize = arrayVariable.ActualArrayDimensions[0];
        for (int k = 0; k < arraySize; ++k)
        {
            var currentArrayIndex = startIndex + k;
            try
            {
                values[0, currentArrayIndex] = ConvertVariableValueToObject(arrayVariable, parameterList[currentArrayIndex]);
            }
            catch (Exception e)
            {
                Log.Warning("RecipeImportExport", "Unable to import parameter '" + arrayVariable.BrowseName + e.Message);
            }
        }
    }

    /// <summary>
    /// Converts a variable value to an object based on its type.
    /// </summary>
    /// <param name="recipeParameterVariable">The variable to convert.</param>
    /// <param name="parameterValue">The value to convert from string format.</param>
    /// <returns>
    /// An object representing the converted value based on the variable's type.
    /// </returns>
    private object ConvertVariableValueToObject(IUAVariable recipeParameterVariable, string parameterValue)
    {
        if (IsInteger(recipeParameterVariable))
            return Convert.ToInt64(parameterValue);
        else if (IsBool(recipeParameterVariable))
            return Convert.ToBoolean(Convert.ToInt32(parameterValue));
        else if (IsString(recipeParameterVariable))
            return parameterValue;
        else if (IsDuration(recipeParameterVariable))
            return Convert.ToDouble(parameterValue);
        else if (IsReal(recipeParameterVariable))
            return Convert.ToDouble(parameterValue, CultureInfo.InvariantCulture);
        else
            throw new Exception("Unable to convert recipe variable value, unsupported datatype");
    }

    /// <summary>
    /// Checks if the given IUAVariable is of integer type (either Integer or UInteger).
    /// </summary>
    /// <param name="variable">The IUAVariable to check.</param>
    /// <returns>True if the variable is of integer type, otherwise false.</returns>
    private bool IsInteger(IUAVariable variable)
    {
        var dataTypeNode = variable.Context.GetDataType(variable.DataType);
        if (dataTypeNode == null)
            return false;

        return dataTypeNode.IsSubTypeOf(OpcUa.DataTypes.Integer) ||
            dataTypeNode.IsSubTypeOf(OpcUa.DataTypes.UInteger);
    }

    /// <summary>
    /// Checks if the given IUAVariable is of a real data type (Float or Double).
    /// </summary>
    /// <param name="variable">The IUAVariable to check.</param>
    /// <returns>True if the variable is of Float or Double type, otherwise false.</returns>
    private bool IsReal(IUAVariable variable)
    {
        var dataTypeNode = variable.Context.GetDataType(variable.DataType);
        if (dataTypeNode == null)
            return false;

        return dataTypeNode.IsSubTypeOf(OpcUa.DataTypes.Float) ||
            dataTypeNode.IsSubTypeOf(OpcUa.DataTypes.Double);
    }

    /// <summary>
    /// Checks if the given IUAVariable is of type Boolean.
    /// </summary>
    /// <param name="variable">The IUAVariable to check.</param>
    /// <returns>True if the variable is of Boolean type, otherwise false.</returns>
    private bool IsBool(IUAVariable variable)
    {
        return variable.DataType == OpcUa.DataTypes.Boolean;
    }

    /// <summary>
    /// Checks if the given IUAVariable is of type String.
    /// </summary>
    /// <param name="variable">The IUAVariable to check.</param>
    /// <returns>True if the variable is of String type, otherwise false.</returns>
    private bool IsString(IUAVariable variable)
    {
        var dataTypeNode = variable.Context.GetDataType(variable.DataType);
        if (dataTypeNode == null)
            return false;

        return dataTypeNode.IsSubTypeOf(OpcUa.DataTypes.String);
    }

    /// <summary>
    /// Checks if the given IUAVariable is of type Duration.
    /// </summary>
    /// <param name="variable">The IUAVariable to check.</param>
    /// <returns>True if the variable is of Duration type, otherwise false.</returns>
    private bool IsDuration(IUAVariable variable)
    {
        var dataTypeNode = variable.Context.GetDataType(variable.DataType);
        if (dataTypeNode == null)
            return false;

        return dataTypeNode.IsSubTypeOf(OpcUa.DataTypes.Duration);
    }
    #endregion


    #region CSVFileReader
    private class CSVFileReader : IDisposable
    {
        public char FieldDelimiter { get; set; } = ',';

        public char QuoteChar { get; set; } = '"';

        public bool WrapFields { get; set; } = false;

        public bool IgnoreMalformedLines { get; set; } = false;

        public CSVFileReader(string filePath, System.Text.Encoding encoding)
        {
            streamReader = new StreamReader(filePath, encoding);
        }

        public CSVFileReader(string filePath)
        {
            streamReader = new StreamReader(filePath, System.Text.Encoding.UTF8);
        }

        public CSVFileReader(StreamReader streamReader)
        {
            this.streamReader = streamReader;
        }

        public bool EndOfFile()
        {
            return streamReader.EndOfStream;
        }

        /// <summary>
        /// Reads a line from a stream and processes it based on whether wrapping fields is enabled.
        /// If the end of file is reached, returns null.
        /// </summary>
        /// <returns>
        /// A list of strings containing the parsed fields, or null if at the end of file.
        /// </returns>
        public List<string> ReadLine()
        {
            if (EndOfFile())
                return null;

            var line = streamReader.ReadLine();

            var result = WrapFields ? ParseLineWrappingFields(line) : ParseLineWithoutWrappingFields(line);

            currentLineNumber++;
            return result;

        }

        public List<List<string>> ReadAll()
        {
            var result = new List<List<string>>();
            while (!EndOfFile())
                result.Add(ReadLine());

            return result;
        }

        /// <summary>
        /// Parses a CSV line by splitting it on the field delimiter without handling quoted fields.
        /// </summary>
        /// <param name="line">The input string to be split.</param>
        /// <returns>A list of strings representing the fields in the line.</returns>
        private List<string> ParseLineWithoutWrappingFields(string line)
        {
            if (string.IsNullOrEmpty(line) && !IgnoreMalformedLines)
                throw new FormatException($"Error processing line {currentLineNumber}. Line cannot be empty");

            return line.Split(FieldDelimiter).ToList();
        }

        /// <summary>
        /// Parses a CSV line handling quoted fields and escaped quote characters.
        /// </summary>
        /// <param name="line">The line of text to parse.</param>
        /// <returns>A list of strings representing the parsed fields, or null if the line is malformed and IgnoreMalformedLines is true.</returns>
        private List<string> ParseLineWrappingFields(string line)
        {
            var fields = new List<string>();
            var buffer = new StringBuilder("");
            var fieldParsing = false;

            int i = 0;
            while (i < line.Length)
            {
                if (!fieldParsing)
                {
                    if (IsWhiteSpace(line, i))
                    {
                        ++i;
                        continue;
                    }

                    // Line and column numbers must be 1-based for messages to user
                    var lineErrorMessage = $"Error processing line {currentLineNumber}";
                    if (i == 0)
                    {
                        // A line must begin with the quotation mark
                        if (!IsQuoteChar(line, i))
                        {
                            if (IgnoreMalformedLines)
                                return null;
                            else
                                throw new FormatException($"{lineErrorMessage}. Expected quotation marks at column {i + 1}");
                        }

                        fieldParsing = true;
                    }
                    else
                    {
                        if (IsQuoteChar(line, i))
                            fieldParsing = true;
                        else if (!IsFieldDelimiter(line, i))
                        {
                            if (IgnoreMalformedLines)
                                return null;
                            else
                                throw new FormatException($"{lineErrorMessage}. Wrong field delimiter at column {i + 1}");
                        }
                    }

                    ++i;
                }
                else
                {
                    if (IsEscapedQuoteChar(line, i))
                    {
                        i += 2;
                        buffer.Append(QuoteChar);
                    }
                    else if (IsQuoteChar(line, i))
                    {
                        fields.Add(buffer.ToString());
                        buffer.Clear();
                        fieldParsing = false;
                        ++i;
                    }
                    else
                    {
                        buffer.Append(line[i]);
                        ++i;
                    }
                }
            }

            return fields;
        }

        private bool IsEscapedQuoteChar(string line, int i)
        {
            return line[i] == QuoteChar && i != line.Length - 1 && line[i + 1] == QuoteChar;
        }

        /// <summary>
        /// Checks if the character at the specified index is a quote character.
        /// </summary>
        /// <param name="line">The string to check.</param>
        /// <param name="i">The index in the string to check.</param>
        /// <returns>True if the character is a quote character, otherwise false.</returns>
        private bool IsQuoteChar(string line, int i)
        {
            return line[i] == QuoteChar;
        }

        /// <summary>
        /// Checks if the character at the specified index is the field delimiter.
        /// </summary>
        /// <param name="line">The string to check.</param>
        /// <param name="i">The index in the string to check.</param>
        /// <returns>True if the character is the field delimiter, otherwise false.</returns>
        private bool IsFieldDelimiter(string line, int i)
        {
            return line[i] == FieldDelimiter;
        }

        /// <summary>
        /// Checks if the character at the specified position is a whitespace character.
        /// </summary>
        /// <param name="line">The string to check.</param>
        /// <param name="i">The index of the character to check.</param>
        /// <returns>True if the character is a whitespace, otherwise false.</returns>
        private bool IsWhiteSpace(string line, int i)
        {
            return Char.IsWhiteSpace(line[i]);
        }

        private StreamReader streamReader;
        private int currentLineNumber = 1;

        #region IDisposable support
        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
                streamReader.Dispose();

            disposed = true;
        }

        /// <summary>
        /// Releases all resources used by the CSVFileReader.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }

    private class CSVFileWriter : IDisposable
    {
        public char FieldDelimiter { get; set; } = ',';

        public char QuoteChar { get; set; } = '"';

        public bool WrapFields { get; set; } = false;

        public CSVFileWriter(string filePath)
        {
            streamWriter = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        }

        public CSVFileWriter(string filePath, System.Text.Encoding encoding)
        {
            streamWriter = new StreamWriter(filePath, false, encoding);
        }

        public CSVFileWriter(StreamWriter streamWriter)
        {
            this.streamWriter = streamWriter;
        }

        public void WriteLine(string[] fields)
        {
            var stringBuilder = new StringBuilder();

            for (var i = 0; i < fields.Length; ++i)
            {
                if (WrapFields)
                    stringBuilder.AppendFormat("{0}{1}{0}", QuoteChar, EscapeField(fields[i]));
                else
                    stringBuilder.AppendFormat("{0}", fields[i]);

                if (i != fields.Length - 1)
                    stringBuilder.Append(FieldDelimiter);
            }

            streamWriter.WriteLine(stringBuilder.ToString());
            streamWriter.Flush();
        }

        /// <summary>
        /// Escapes a field value by doubling any quote characters.
        /// </summary>
        /// <param name="field">The string to be escaped.</param>
        /// <returns>The escaped string with quote characters doubled.</returns>
        private string EscapeField(string field)
        {
            var quoteCharString = QuoteChar.ToString();
            return field.Replace(quoteCharString, quoteCharString + quoteCharString);
        }

        private StreamWriter streamWriter;

        #region IDisposable Support
        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
                streamWriter.Dispose();

            disposed = true;
        }

        /// <summary>
        /// Releases all resources used by the CSVFileWriter.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
    #endregion

    private RemoteVariableSynchronizer variableSynchronizer;
    private DelayedTask task;
    private object lockObject = new object();
    private IUAVariable ApplyFromDBTrigger;
    private IUAVariable ApplyFromEditModelTrigger;
    private IUAVariable DeleteTrigger;
    private IUAVariable ExportTrigger;
    private IUAVariable ImportTrigger;
    private IUAVariable LoadFromPLCTrigger;
    private IUAVariable SaveToDBTrigger;
    private IUAVariable SelectFromDBTrigger;
}
