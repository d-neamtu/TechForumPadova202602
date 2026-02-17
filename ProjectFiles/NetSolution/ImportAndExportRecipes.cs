#region Using directives
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FTOptix.Core;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using UAManagedCore;
using FTOptix.Recipe;
#endregion

/// <summary>
/// Provides functionality to import and export RecipeX recipes to/from CSV and JSON files.
/// Supports recipe data, metadata, and structure preservation for complete roundtrip operations.
/// </summary>
public class ImportAndExportRecipes : BaseNetLogic
{
    #region Constants
    private const string CsvExtension = ".csv";
    private const string JsonExtension = ".json";
    private const string MetadataSuffix = "_metadata";
    private const char WrapCharacter = '`';
    #endregion

    /// <summary>
    /// Exports all recipes from the recipe schema to CSV, JSON structure, and metadata files.
    /// </summary>
    [ExportMethod]
    public void ExportRecipes()
    {
        if (exportTask == null)
        {
            exportTask = new LongRunningTask(ExportRecipesMethod, LogicObject);
            exportTask.Start();
        }
        else
        {
            Log.Warning("ImportAndExportRecipesX.ExportRecipes", "Export task is already running");
        }
    }

    /// <summary>
    /// Imports recipes from CSV, JSON structure, and optional metadata files into the recipe schema.
    /// </summary>
    [ExportMethod]
    public void ImportRecipes()
    {
        if (importTask == null)
        {
            importTask = new LongRunningTask(ImportRecipesMethod, LogicObject);
            importTask.Start();
        }
        else
        {
            Log.Warning("ImportAndExportRecipesX.ImportRecipes", "Import task is already running");
        }
    }

    /// <summary>
    /// Stops the import and export tasks.
    /// </summary>
    public override void Stop()
    {
        // Clean up any resources if necessary
        StopImport();
        StopExport();
    }

    private void StopImport()
    {
        importTask?.Dispose();
        importTask = null;
    }

    private void StopExport()
    {
        exportTask?.Dispose();
        exportTask = null;
    }

    #region Private Methods - Validation & Configuration

    /// <summary>
    /// Imports recipes from CSV, JSON structure, and optional metadata files into the recipe schema.
    /// </summary>
    private void ImportRecipesMethod()
    {
        if (!TryValidateRecipeSchema(out var recipeSchema))
        {
            Log.Error("ImportAndExportRecipesX.ExportRecipes", "Invalid RecipeSchema configuration. Make sure that a valid Recipe Schema was selected. This script cannot be used with the legacy Recipe Schemas.");
            StopImport();
            return;
        }

        var recipeTarget = InformationModel.Get(recipeSchema.TargetNode);

        var importConfig = LoadImportConfiguration();
        if (importConfig == null)
        {
            StopImport();
            return;
        }

        if (!ValidateImportFiles(importConfig))
        {
            StopImport();
            return;
        }

        try
        {
            Log.Info("ImportAndExportRecipesX.ImportRecipes", $"Importing recipes from '{importConfig.CsvPath}'");

            var recipeStructures = ParseJsonStructureFile(importConfig.JsonPath, recipeTarget);
            if (recipeStructures == null || recipeStructures.Count == 0)
            {
                Log.Error("ImportAndExportRecipesX.ImportRecipes", "Failed to parse JSON structure file or no structures found");
                StopImport();
                return;
            }

            var csvData = ParseCsvFileForImport(importConfig.CsvPath, importConfig.CsvSeparator);
            if (csvData == null || csvData.Count == 0)
            {
                Log.Warning("ImportAndExportRecipesX.ImportRecipes", "No recipe data found in CSV file");
                StopImport();
                return;
            }

            // Parse metadata if file exists
            IReadOnlyList<CsvRecipeMetadata> metadataList = null;
            if (File.Exists(importConfig.MetadataPath))
            {
                metadataList = ParseMetadataFileForImport(importConfig.MetadataPath, importConfig.CsvSeparator);
                if (metadataList != null)
                {
                    Log.Verbose1("ImportAndExportRecipesX.ImportRecipes", $"Parsed {metadataList.Count} recipe metadata entries");
                }
            }

            ImportRecipesFromData(recipeSchema, csvData, recipeStructures, metadataList);

            Log.Info("ImportAndExportRecipesX.ImportRecipes", $"Successfully imported recipes");
        }
        catch (Exception ex)
        {
            Log.Error("ImportAndExportRecipesX.ImportRecipes", $"Failed to import recipes: {ex.Message}");
        }

        // Clean up import task
        StopImport();
    }

    /// <summary>
    /// Exports all recipes from the recipe schema to CSV, JSON structure, and metadata files.
    /// </summary>
    private void ExportRecipesMethod()
    {
        if (!TryValidateRecipeSchema(out var recipeSchema))
        {
            Log.Error("ImportAndExportRecipesX.ExportRecipes", "Invalid RecipeSchema configuration. Make sure that a valid Recipe Schema was selected. This script cannot be used with the legacy Recipe Schemas.");
            StopExport();
            return;
        }

        var exportConfig = LoadExportConfiguration();
        if (exportConfig == null)
        {
            StopExport();
            return;
        }

        var recipesList = GetRecipesList(recipeSchema);
        if (recipesList.Length == 0)
        {
            Log.Info("ImportAndExportRecipesX.ExportRecipes", $"No recipes found in '{Owner.BrowseName}' to export");
            StopExport();
            return;
        }

        try
        {
            using var exportContext = CreateExportContext(exportConfig);
            ExportRecipesInternal(recipeSchema, recipesList, exportContext);

            Log.Info("ImportAndExportRecipesX.ExportRecipes",
                $"Successfully exported recipes to '{exportConfig.CsvPath}'");
        }
        catch (Exception ex)
        {
            Log.Error("ImportAndExportRecipesX.ExportRecipes",
                $"Failed to write CSV file to '{exportConfig.CsvPath}': {ex.Message}");
        }

        // Clean up export task
        StopExport();
    }

    /// <summary>
    /// Validates that the Owner is a RecipeSchema.
    /// </summary>
    /// <param name="recipeSchema">The validated RecipeSchema if successful; otherwise, null.</param>
    /// <returns>True if Owner is a valid RecipeSchema; otherwise, false.</returns>
    private bool TryValidateRecipeSchema(out FTOptix.RecipeX.RecipeSchema recipeSchema)
    {
        var recipeSchemaPointer = LoadConfigurationVariable("RecipeSchema").Value;

        recipeSchema = InformationModel.Get<FTOptix.RecipeX.RecipeSchema>(recipeSchemaPointer);

        return recipeSchema != null;
    }

    /// <summary>
    /// Loads and validates the export configuration from project variables.
    /// </summary>
    /// <returns>An ExportConfiguration object if successful; otherwise, null.</returns>
    private ExportConfiguration LoadExportConfiguration()
    {
        string csvPath = new ResourceUri(LoadConfigurationVariable("CsvPath").Value).Uri;
        if (!csvPath.EndsWith(CsvExtension))
        {
            Log.Error("ImportAndExportRecipesX.ExportRecipes", "Invalid CSV output path");
            return null;
        }

        return new ExportConfiguration
        {
            CsvPath = csvPath,
            JsonPath = DeriveFilePath(csvPath, JsonExtension),
            MetadataPath = DeriveFilePath(csvPath, CsvExtension, MetadataSuffix),
            CsvSeparator = ExtractCsvSeparator(LoadConfigurationVariable("CsvSeparator").Value),
            WrapFields = LoadConfigurationVariable("WrapFields").Value
        };
    }

    /// <summary>
    /// Loads and validates the import configuration from project variables.
    /// </summary>
    /// <returns>An ImportConfiguration object if successful; otherwise, null.</returns>
    private ImportConfiguration LoadImportConfiguration()
    {
        string csvPath = new ResourceUri(LoadConfigurationVariable("CsvPath").Value).Uri;
        if (!csvPath.EndsWith(CsvExtension))
        {
            Log.Error("ImportAndExportRecipesX.ImportRecipes", "Invalid CSV input path");
            return null;
        }

        return new ImportConfiguration
        {
            CsvPath = csvPath,
            JsonPath = DeriveFilePath(csvPath, JsonExtension),
            MetadataPath = DeriveFilePath(csvPath, CsvExtension, MetadataSuffix),
            CsvSeparator = ExtractCsvSeparator(LoadConfigurationVariable("CsvSeparator").Value)
        };
    }

    /// <summary>
    /// Validates that all required import files exist. Metadata file is optional for backward compatibility.
    /// </summary>
    /// <param name="config">The import configuration containing file paths.</param>
    /// <returns>True if required files exist; otherwise, false.</returns>
    private static bool ValidateImportFiles(ImportConfiguration config)
    {
        if (!File.Exists(config.CsvPath))
        {
            Log.Error("ImportAndExportRecipesX.ImportRecipes", $"CSV file not found at '{config.CsvPath}'");
            return false;
        }

        if (!File.Exists(config.JsonPath))
        {
            Log.Error("ImportAndExportRecipesX.ImportRecipes", $"JSON structure file not found at '{config.JsonPath}'");
            return false;
        }

        // Metadata file is optional for backward compatibility
        if (!File.Exists(config.MetadataPath))
        {
            Log.Warning("ImportAndExportRecipesX.ImportRecipes",
                $"Metadata file not found at '{config.MetadataPath}'. Metadata will not be imported.");
        }

        return true;
    }

    /// <summary>
    /// Derives a new file path by replacing the extension and optionally adding a suffix.
    /// </summary>
    /// <param name="basePath">The base file path with extension.</param>
    /// <param name="extension">The new extension to use (e.g., ".json").</param>
    /// <param name="suffix">Optional suffix to add before the extension (e.g., "_metadata").</param>
    /// <returns>The derived file path.</returns>
    private static string DeriveFilePath(string basePath, string extension, string suffix = "")
    {
        string pathWithoutExtension = basePath.Substring(0, basePath.Length - 4);
        return pathWithoutExtension + suffix + extension;
    }

    #endregion

    #region Private Methods - Export

    /// <summary>
    /// Creates an export context with all necessary file writers for recipe data, metadata, and structure.
    /// </summary>
    /// <param name="config">The export configuration.</param>
    /// <returns>An ExportContext with initialized writers.</returns>
    private ExportContext CreateExportContext(ExportConfiguration config)
    {
        return new ExportContext(
            new CsvWriter(
                new StreamWriter(config.CsvPath),
                config.CsvSeparator,
                config.WrapFields),
            new CsvWriter(
                new StreamWriter(config.MetadataPath),
                config.CsvSeparator,
                config.WrapFields),
            new JsonStructureWriter(config.JsonPath));
    }

    /// <summary>
    /// Internal method that performs the actual recipe export operation.
    /// </summary>
    /// <param name="recipeSchema">The recipe schema to export from.</param>
    /// <param name="recipesList">The list of recipes to export.</param>
    /// <param name="context">The export context containing file writers.</param>
    private void ExportRecipesInternal(FTOptix.RecipeX.RecipeSchema recipeSchema, FTOptix.RecipeX.Recipe[] recipesList, ExportContext context)
    {
        var recipeTarget = InformationModel.Get(recipeSchema.TargetNode);
        var firstRecipeId = recipesList[0].RecipeId;

        // Build headers and structures from first recipe
        var dataItemsList = GetDataItemsOrThrow(recipeSchema, firstRecipeId);
        var (dataHeaders, structures) = BuildRecipeDataHeaders(dataItemsList.DataItems, recipeTarget);
        context.RecipeDataWriter.WriteRow(dataHeaders.ToArray());

        var metadataItems = GetMetadataOrThrow(recipeSchema, firstRecipeId);
        var metaHeaders = BuildMetadataHeaders(metadataItems.MetadataValues);
        context.RecipeMetaWriter.WriteRow(metaHeaders.ToArray());

        // Write all recipe data
        foreach (var recipe in recipesList)
        {
            WriteRecipeData(recipe, recipeSchema, context.RecipeDataWriter, structures);
            WriteRecipeMetaData(recipe, recipeSchema, context.RecipeMetaWriter);
        }

        // Write JSON structure file
        WriteJsonStructureFile(structures, context.JsonWriter.Writer);
    }

    /// <summary>
    /// Builds CSV headers and recipe structure definitions from recipe data items.
    /// For long format, headers are simply: RecipeName, RecipeVersion, Item, Value
    /// </summary>
    /// <param name="dataItems">The data items from the recipe schema.</param>
    /// <param name="recipeTarget">The target node in the information model.</param>
    /// <returns>A tuple containing the CSV headers and recipe structures.</returns>
    private (IReadOnlyList<string> headers, IReadOnlyList<RecipeStructure> structures) BuildRecipeDataHeaders(
        FTOptix.RecipeX.RecipeDataItem[] dataItems,
        IUANode recipeTarget)
    {
        IReadOnlyList<string> headers = ["RecipeName", "RecipeVersion", "Item", "Value"];
        var structures = new List<RecipeStructure>();

        foreach (var dataItem in dataItems)
        {
            var node = ResolveNodePath(dataItem, recipeTarget);
            var dataType = ((IUAVariable)node).DataType;
            var dataTypeNode = InformationModel.Get(dataType);

            string elementPath = BuildElementPath(dataItem);
            string variableFullPath = elementPath + SerializeStructureNames((UADataType)dataTypeNode, dataItem.ElementAccess);

            structures.Add(new RecipeStructure
            {
                ItemRelativeBrowsePath = dataItem.ItemRelativeBrowsePath,
                DataItemRelativeBrowsePath = dataItem.DataItemRelativeBrowsePath,
                DataType = dataType,
                ElementAccess = dataItem.ElementAccess,
                ItemName = variableFullPath
            });
        }

        return (headers, structures);
    }

    /// <summary>
    /// Builds CSV headers for recipe metadata.
    /// </summary>
    /// <param name="metadataValues">The metadata values from the recipe schema.</param>
    /// <returns>A list of header column names.</returns>
    private IReadOnlyList<string> BuildMetadataHeaders(FTOptix.RecipeX.RecipeMetadata[] metadataValues)
    {
        var headers = new List<string> { "RecipeName", "RecipeVersion" };
        headers.AddRange(metadataValues.Select(m => m.Name));
        return headers;
    }

    /// <summary>
    /// Resolves the full node path for a recipe data item in the information model.
    /// </summary>
    /// <param name="dataItem">The recipe data item.</param>
    /// <param name="recipeTarget">The target node in the information model.</param>
    /// <returns>The resolved IUANode.</returns>
    private IUANode ResolveNodePath(FTOptix.RecipeX.RecipeDataItem dataItem, IUANode recipeTarget)
    {
        string itemPath = string.Join("/", dataItem.ItemRelativeBrowsePath);
        string dataItemPath = string.Join("/", dataItem.DataItemRelativeBrowsePath);

        if (dataItem.ItemRelativeBrowsePath.Length > 0)
        {
            var node = recipeTarget.Get(itemPath);
            return dataItem.DataItemRelativeBrowsePath.Length > 0
                ? node.Get(dataItemPath)
                : node;
        }

        return recipeTarget.Get(dataItemPath);
    }

    /// <summary>
    /// Builds the element path string for a recipe data item.
    /// </summary>
    /// <param name="dataItem">The recipe data item.</param>
    /// <returns>The formatted element path.</returns>
    private string BuildElementPath(FTOptix.RecipeX.RecipeDataItem dataItem)
    {
        string itemPath = string.Join("/", dataItem.ItemRelativeBrowsePath);
        string dataItemPath = string.Join("/", dataItem.DataItemRelativeBrowsePath);
        return $"{itemPath}/{dataItemPath}".Trim('/');
    }

    /// <summary>
    /// Writes the recipe structure definitions to a JSON file.
    /// </summary>
    /// <param name="structures">The list of recipe structures.</param>
    /// <param name="jsonWriter">The JSON writer to use.</param>
    private void WriteJsonStructureFile(IReadOnlyList<RecipeStructure> structures, Utf8JsonWriter jsonWriter)
    {
        jsonWriter.WriteStartArray();

        foreach (var structure in structures)
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("itemRelativeBrowsePath");
            JsonSerializer.Serialize(jsonWriter, structure.ItemRelativeBrowsePath);
            jsonWriter.WritePropertyName("dataItemRelativeBrowsePath");
            JsonSerializer.Serialize(jsonWriter, structure.DataItemRelativeBrowsePath);
            jsonWriter.WriteString("dataType", structure.DataType.ToString());
            jsonWriter.WritePropertyName("elementAccess");
            JsonSerializer.Serialize(jsonWriter, structure.ElementAccess);
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndArray();
        jsonWriter.Flush();
    }

    /// <summary>
    /// Gets data items for a recipe and throws an exception if the operation fails.
    /// </summary>
    /// <param name="recipeSchema">The recipe schema.</param>
    /// <param name="recipeId">The recipe ID.</param>
    /// <returns>The data items result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when data items cannot be retrieved.</exception>
    private FTOptix.RecipeX.GetDataItemsResult GetDataItemsOrThrow(FTOptix.RecipeX.RecipeSchema recipeSchema, FTOptix.RecipeX.RecipeId recipeId)
    {
        var result = recipeSchema.GetDataItems(recipeId);
        if (result.ResultCode != FTOptix.RecipeX.GetDataItemsResultCode.Success)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve data items from '{recipeSchema.BrowseName}'");
        }
        return result;
    }

    /// <summary>
    /// Gets metadata values for a recipe and throws an exception if the operation fails.
    /// </summary>
    /// <param name="recipeSchema">The recipe schema.</param>
    /// <param name="recipeId">The recipe ID.</param>
    /// <returns>The metadata values result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when metadata cannot be retrieved.</exception>
    private FTOptix.RecipeX.GetRecipeMetadataValuesResult GetMetadataOrThrow(FTOptix.RecipeX.RecipeSchema recipeSchema, FTOptix.RecipeX.RecipeId recipeId)
    {
        var result = recipeSchema.GetRecipeMetadataValues(recipeId);
        if (result.ResultCode != FTOptix.RecipeX.GetRecipeMetadataValuesResultCode.Success)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve metadata items from '{recipeSchema.BrowseName}'");
        }
        return result;
    }

    /// <summary>
    /// Writes recipe data items to a CSV file in long format (one row per item).
    /// </summary>
    /// <param name="recipe">The recipe to export.</param>
    /// <param name="recipeSchema">The recipe schema.</param>
    /// <param name="csvWriter">The CSV writer to use.</param>
    /// <param name="structures">The recipe structures containing item names.</param>
    private void WriteRecipeData(FTOptix.RecipeX.Recipe recipe, FTOptix.RecipeX.RecipeSchema recipeSchema, CsvWriter csvWriter, IReadOnlyList<RecipeStructure> structures)
    {
        var dataItems = recipeSchema.GetDataItems(recipe.RecipeId);
        if (dataItems.ResultCode != FTOptix.RecipeX.GetDataItemsResultCode.Success)
        {
            Log.Info("ImportAndExportRecipesX.WriteRecipeData",
                $"Failed to retrieve data items for recipe '{recipe.RecipeId.Name}' version '{recipe.RecipeId.Version}'");
            return;
        }

        // Write one row per data item
        for (int i = 0; i < dataItems.DataItems.Length; i++)
        {
            var item = dataItems.DataItems[i];
            string itemName = i < structures.Count ? structures[i].ItemName : $"Item_{i}";
            string value = JsonSerializer.Serialize(item.Value);

            csvWriter.WriteRow(recipe.RecipeId.Name, recipe.RecipeId.Version, itemName, value);
        }
    }

    /// <summary>
    /// Writes recipe metadata values to a CSV file.
    /// </summary>
    /// <param name="recipe">The recipe to export.</param>
    /// <param name="recipeSchema">The recipe schema.</param>
    /// <param name="csvWriter">The CSV writer to use.</param>
    private static void WriteRecipeMetaData(FTOptix.RecipeX.Recipe recipe, FTOptix.RecipeX.RecipeSchema recipeSchema, CsvWriter csvWriter)
    {
        var metaItems = recipeSchema.GetRecipeMetadataValues(recipe.RecipeId);
        if (metaItems.ResultCode != FTOptix.RecipeX.GetRecipeMetadataValuesResultCode.Success)
        {
            Log.Info("ImportAndExportRecipesX.WriteRecipeMetaData",
                $"Failed to retrieve metadata items for recipe '{recipe.RecipeId.Name}' version '{recipe.RecipeId.Version}'");
            return;
        }

        var valuesArray = new List<string> { recipe.RecipeId.Name, recipe.RecipeId.Version };
        valuesArray.AddRange(metaItems.MetadataValues.Select(item => JsonSerializer.Serialize(item.Value)));
        csvWriter.WriteRow(valuesArray.ToArray());
    }

    #endregion

    #region Private Methods - Import

    /// <summary>
    /// Imports recipes from parsed CSV data and optional metadata.
    /// </summary>
    /// <param name="recipeSchema">The target recipe schema.</param>
    /// <param name="csvData">The parsed CSV recipe data.</param>
    /// <param name="structures">The recipe structure definitions.</param>
    /// <param name="metadataList">Optional metadata for recipes.</param>
    private void ImportRecipesFromData(
        FTOptix.RecipeX.RecipeSchema recipeSchema,
        IReadOnlyList<CsvRecipeData> csvData,
        IReadOnlyList<RecipeStructure> structures,
        IReadOnlyList<CsvRecipeMetadata> metadataList)
    {
        var stats = new ImportStatistics();

        foreach (var recipeData in csvData)
        {
            // Find corresponding metadata for this recipe
            var metadata = metadataList?.FirstOrDefault(m =>
                m.RecipeName == recipeData.RecipeName && m.RecipeVersion == recipeData.RecipeVersion);

            ImportSingleRecipe(recipeSchema, recipeData, structures, metadata, stats);
        }

        Log.Info("ImportAndExportRecipesX.ImportRecipesFromData",
            $"Import complete. Created: {stats.CreatedCount}, Updated: {stats.UpdatedCount}, Errors: {stats.ErrorCount}");
    }

    /// <summary>
    /// Imports a single recipe with its data items and optional metadata.
    /// </summary>
    /// <param name="recipeSchema">The target recipe schema.</param>
    /// <param name="recipeData">The recipe data to import.</param>
    /// <param name="structures">The recipe structure definitions.</param>
    /// <param name="metadata">Optional metadata for the recipe.</param>
    /// <param name="stats">Import statistics tracker.</param>
    private void ImportSingleRecipe(
        FTOptix.RecipeX.RecipeSchema recipeSchema,
        CsvRecipeData recipeData,
        IReadOnlyList<RecipeStructure> structures,
        CsvRecipeMetadata metadata,
        ImportStatistics stats)
    {
        var recipeId = new FTOptix.RecipeX.RecipeId
        {
            Name = recipeData.RecipeName,
            Version = recipeData.RecipeVersion
        };

        try
        {
            if (EnsureRecipeExists(recipeSchema, recipeId, stats))
            {
                ImportRecipeDataItems(recipeSchema, recipeId, recipeData, structures);

                // Import metadata if available
                if (metadata != null)
                {
                    ImportRecipeMetadata(recipeSchema, recipeId, metadata);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("ImportAndExportRecipesX.ImportSingleRecipe",
                $"Failed to import recipe '{recipeData.RecipeName}' version '{recipeData.RecipeVersion}': {ex.Message}");
            stats.ErrorCount++;
        }
    }

    /// <summary>
    /// Ensures a recipe exists in the schema, creating it if necessary.
    /// </summary>
    /// <param name="recipeSchema">The recipe schema.</param>
    /// <param name="recipeId">The recipe ID to check/create.</param>
    /// <param name="stats">Import statistics tracker.</param>
    /// <returns>True if recipe exists or was created successfully; otherwise, false.</returns>
    private bool EnsureRecipeExists(FTOptix.RecipeX.RecipeSchema recipeSchema, FTOptix.RecipeX.RecipeId recipeId, ImportStatistics stats)
    {
        var existingRecipes = GetRecipesList(recipeSchema);
        bool recipeExists = existingRecipes.Any(r =>
            r.RecipeId.Name == recipeId.Name && r.RecipeId.Version == recipeId.Version);

        if (!recipeExists)
        {
            var createResult = recipeSchema.CreateRecipe(recipeId);
            if (createResult != FTOptix.RecipeX.CreateRecipeResultCode.Success)
            {
                Log.Error("ImportAndExportRecipesX.EnsureRecipeExists",
                    $"Failed to create recipe '{recipeId.Name}' version '{recipeId.Version}': {createResult}");
                stats.ErrorCount++;
                return false;
            }
            stats.CreatedCount++;
            Log.Info("ImportAndExportRecipesX.EnsureRecipeExists",
                $"Created recipe '{recipeId.Name}' version '{recipeId.Version}'");
        }
        else
        {
            stats.UpdatedCount++;
        }

        return true;
    }

    /// <summary>
    /// Imports all data items for a single recipe.
    /// </summary>
    /// <param name="recipeSchema">The recipe schema.</param>
    /// <param name="recipeId">The recipe ID.</param>
    /// <param name="recipeData">The recipe data containing item names and values.</param>
    /// <param name="structures">The recipe structure definitions.</param>
    private void ImportRecipeDataItems(
        FTOptix.RecipeX.RecipeSchema recipeSchema,
        FTOptix.RecipeX.RecipeId recipeId,
        CsvRecipeData recipeData,
        IReadOnlyList<RecipeStructure> structures)
    {
        int itemCount = 0;
        int itemErrorCount = 0;

        var structureLookup = BuildStructureLookup(structures);

        // Match items by name from CSV to structures
        for (int i = 0; i < recipeData.ItemNames.Count; i++)
        {
            string itemName = recipeData.ItemNames[i]?.Trim();
            string value = recipeData.Values[i];

            // Find matching structure by item name.
            // Supports:
            // - full path exported by this logic (e.g. "Folder/Variable>Field")
            // - simple variable name (e.g. "Variable5") for backwards compatible CSV files
            structureLookup.TryGetValue(itemName, out var structure);
            if (structure == null)
            {
                Log.Warning("ImportAndExportRecipesX.ImportRecipeDataItems",
                    $"No matching structure found for item '{itemName}' in recipe '{recipeId.Name}'");
                itemErrorCount++;
                continue;
            }

            if (TryImportDataItem(recipeSchema, recipeId, value, structure))
            {
                itemCount++;
            }
            else
            {
                itemErrorCount++;
            }
        }

        Log.Info("ImportAndExportRecipesX.ImportRecipeDataItems",
            $"Imported {itemCount} data items for recipe '{recipeId.Name}' version '{recipeId.Version}'. Errors: {itemErrorCount}");
    }

    private static Dictionary<string, RecipeStructure> BuildStructureLookup(IReadOnlyList<RecipeStructure> structures)
    {
        var dict = new Dictionary<string, RecipeStructure>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in structures)
        {
            if (s == null)
                continue;

            AddIfMissing(dict, s.ItemName, s);

            var simpleName = ExtractSimpleItemName(s.ItemName);
            AddIfMissing(dict, simpleName, s);
        }

        return dict;
    }

    private static void AddIfMissing(Dictionary<string, RecipeStructure> dict, string key, RecipeStructure value)
    {
        if (string.IsNullOrWhiteSpace(key) || value == null)
            return;

        dict.TryAdd(key.Trim(), value);
    }

    private static string ExtractSimpleItemName(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return itemName;

        itemName = itemName.Trim();

        // Remove any structure field suffix (e.g. ">Field1>Field2")
        var withoutStruct = itemName;
        var structSepIndex = withoutStruct.IndexOf('>');
        if (structSepIndex >= 0)
            withoutStruct = withoutStruct.Substring(0, structSepIndex);

        // Take last browse-name segment (e.g. "Folder/Sub/Variable" -> "Variable")
        var lastSlash = withoutStruct.LastIndexOf('/');
        var lastDot = withoutStruct.LastIndexOf('.');
        var lastSep = Math.Max(lastSlash, lastDot);
        return lastSep >= 0 ? withoutStruct.Substring(lastSep + 1) : withoutStruct;
    }

    /// <summary>
    /// Attempts to import a single data item for a recipe.
    /// </summary>
    /// <param name="recipeSchema">The recipe schema.</param>
    /// <param name="recipeId">The recipe ID.</param>
    /// <param name="valueJson">The JSON-serialized value.</param>
    /// <param name="structure">The structure definition for this data item.</param>
    /// <returns>True if import was successful; otherwise, false.</returns>
    private bool TryImportDataItem(
        FTOptix.RecipeX.RecipeSchema recipeSchema,
        FTOptix.RecipeX.RecipeId recipeId,
        string valueJson,
        RecipeStructure structure)
    {
        try
        {
            object value = DeserializeRecipeValue(valueJson);
            var result = recipeSchema.SetRecipeDataItemValue(
                recipeId,
                structure.ItemRelativeBrowsePath ?? Array.Empty<string>(),
                structure.DataItemRelativeBrowsePath ?? Array.Empty<string>(),
                structure.ElementAccess,
                value
            );

            if (result == FTOptix.RecipeX.SetRecipeDataItemValueResultCode.Success)
            {
                return true;
            }

            LogImportError(structure, recipeId, result, valueJson);
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning("ImportAndExportRecipesX.TryImportDataItem",
                $"Error importing data item for recipe '{recipeId.Name}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deserializes a JSON string to an object, preserving the original type.
    /// </summary>
    /// <param name="valueJson">The JSON-serialized value.</param>
    /// <returns>The deserialized object, or null if the input is empty.</returns>
    private object DeserializeRecipeValue(string valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
            return null;

        var jsonElement = JsonSerializer.Deserialize<JsonElement>(valueJson);
        return ConvertJsonElementToObject(jsonElement);
    }

    /// <summary>
    /// Logs an error that occurred during data item import.
    /// </summary>
    /// <param name="structure">The structure definition.</param>
    /// <param name="recipeId">The recipe ID.</param>
    /// <param name="result">The error result code.</param>
    /// <param name="valueJson">The value that failed to import.</param>
    private void LogImportError(RecipeStructure structure, FTOptix.RecipeX.RecipeId recipeId, FTOptix.RecipeX.SetRecipeDataItemValueResultCode result, string valueJson)
    {
        string itemPath = string.Join("/", structure.ItemRelativeBrowsePath ?? Array.Empty<string>());
        string dataItemPath = string.Join("/", structure.DataItemRelativeBrowsePath ?? Array.Empty<string>());
        Log.Warning("ImportAndExportRecipesX.TryImportDataItem",
            $"Failed to set value for item '{itemPath}/{dataItemPath}' in recipe '{recipeId.Name}': {result}. Value: '{valueJson}'");
    }

    /// <summary>
    /// Imports metadata values for a single recipe.
    /// </summary>
    /// <param name="recipeSchema">The recipe schema.</param>
    /// <param name="recipeId">The recipe ID.</param>
    /// <param name="metadata">The metadata to import.</param>
    private void ImportRecipeMetadata(FTOptix.RecipeX.RecipeSchema recipeSchema, FTOptix.RecipeX.RecipeId recipeId, CsvRecipeMetadata metadata)
    {
        if (metadata?.MetadataValues == null || metadata.MetadataValues.Count == 0)
            return;

        int successCount = 0;
        int errorCount = 0;

        foreach (var metadataEntry in metadata.MetadataValues)
        {
            try
            {
                // Deserialize the JSON value
                object value = string.IsNullOrWhiteSpace(metadataEntry.Value)
                    ? null
                    : DeserializeRecipeValue(metadataEntry.Value);

                var result = recipeSchema.SetRecipeMetadataValue(
                    recipeId,
                    metadataEntry.Key,
                    value
                );

                if (result == FTOptix.RecipeX.SetRecipeMetadataValueResultCode.Success)
                {
                    successCount++;
                }
                else
                {
                    Log.Warning("ImportAndExportRecipesX.ImportRecipeMetadata",
                        $"Failed to set metadata '{metadataEntry.Key}' for recipe '{recipeId.Name}': {result}");
                    errorCount++;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("ImportAndExportRecipesX.ImportRecipeMetadata",
                    $"Error importing metadata '{metadataEntry.Key}' for recipe '{recipeId.Name}': {ex.Message}");
                errorCount++;
            }
        }

        if (successCount > 0 || errorCount > 0)
        {
            Log.Info("ImportAndExportRecipesX.ImportRecipeMetadata",
                $"Imported {successCount} metadata values for recipe '{recipeId.Name}'. Errors: {errorCount}");
        }
    }

    #endregion

    #region Private Methods - Parsing

    /// <summary>
    /// Serializes structure field names into a path string (e.g., ">Field1>Field2").
    /// </summary>
    /// <param name="dataTypeNode">The data type node.</param>
    /// <param name="elementAccessStruct">The element access structure containing field indexes.</param>
    /// <returns>The serialized field path, or empty string if no fields.</returns>
    private string SerializeStructureNames(UADataType dataTypeNode, ElementAccessStruct elementAccessStruct)
    {
        if (elementAccessStruct.FieldIndexes == null || elementAccessStruct.FieldIndexes.Length == 0)
            return string.Empty;

        var stringBuilder = new StringBuilder();
        var currentDataType = dataTypeNode;

        foreach (var fieldIndex in elementAccessStruct.FieldIndexes)
        {
            if (!TryGetStructField(currentDataType, fieldIndex, out object field, out currentDataType))
                break;

            stringBuilder.Append(">").Append(((dynamic)field).Name);
        }

        return stringBuilder.ToString();
    }

    /// <summary>
    /// Attempts to get a structure field by its index.
    /// </summary>
    /// <param name="dataType">The current data type.</param>
    /// <param name="fieldIndex">The field index to retrieve.</param>
    /// <param name="field">The retrieved field, or null if failed.</param>
    /// <param name="nextDataType">The data type of the field, or null if failed.</param>
    /// <returns>True if field was retrieved successfully; otherwise, false.</returns>
    private bool TryGetStructField(
        UADataType dataType,
        FieldIndexStruct fieldIndex,
        out object field,
        out UADataType nextDataType)
    {
        field = null;
        nextDataType = null;

        if (dataType.StructDefinition == null || dataType.StructDefinition.Fields == null)
        {
            Log.Warning("ImportAndExportRecipesX.TryGetStructField",
                $"StructDefinition or Fields is null for data type '{dataType.BrowseName}'");
            return false;
        }

        int fieldPos = (int)fieldIndex.FieldPos;
        if (fieldPos >= dataType.StructDefinition.Fields.Count)
        {
            Log.Warning("ImportAndExportRecipesX.TryGetStructField",
                $"Field position {fieldPos} is out of range for data type '{dataType.BrowseName}' with {dataType.StructDefinition.Fields.Count} fields");
            return false;
        }

        var structField = dataType.StructDefinition.Fields[fieldPos];
        field = structField;

        // Use dynamic to access properties since we don't know the exact type
        nextDataType = InformationModel.Get<UADataType>(((dynamic)structField).DataTypeId);

        if (nextDataType == null)
        {
            Log.Warning("ImportAndExportRecipesX.TryGetStructField",
                $"Could not resolve data type for field '{((dynamic)structField).Name}'");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Parses the JSON structure file containing recipe structure definitions.
    /// </summary>
    /// <param name="jsonFilePath">The path to the JSON file.</param>
    /// <returns>A list of recipe structures, or null if parsing failed.</returns>
    private List<RecipeStructure> ParseJsonStructureFile(string jsonFilePath, IUANode recipeTarget)
    {
        try
        {
            string jsonContent = File.ReadAllText(jsonFilePath);
            var jsonDocument = JsonDocument.Parse(jsonContent);
            var structures = new List<RecipeStructure>();

            foreach (var element in jsonDocument.RootElement.EnumerateArray())
            {
                if (TryParseStructureElement(element, out var structure))
                {
                    TryPopulateStructureItemName(structure, recipeTarget);
                    structures.Add(structure);
                }
            }

            Log.Verbose1("ImportAndExportRecipesX.ParseJsonStructureFile",
                $"Parsed {structures.Count} structure definitions from JSON");
            return structures;
        }
        catch (Exception ex)
        {
            Log.Error("ImportAndExportRecipesX.ParseJsonStructureFile",
                $"Failed to parse JSON file '{jsonFilePath}': {ex.Message}");
            return null;
        }
    }

    private void TryPopulateStructureItemName(RecipeStructure structure, IUANode recipeTarget)
    {
        if (structure == null || recipeTarget == null)
            return;

        try
        {
            var node = ResolveNodePath(new FTOptix.RecipeX.RecipeDataItem
            {
                ItemRelativeBrowsePath = structure.ItemRelativeBrowsePath ?? Array.Empty<string>(),
                DataItemRelativeBrowsePath = structure.DataItemRelativeBrowsePath ?? Array.Empty<string>(),
                ElementAccess = structure.ElementAccess
            }, recipeTarget);

            var dataType = ((IUAVariable)node).DataType;
            var dataTypeNode = InformationModel.Get<UADataType>(dataType);

            var elementPath = BuildElementPath(new FTOptix.RecipeX.RecipeDataItem
            {
                ItemRelativeBrowsePath = structure.ItemRelativeBrowsePath ?? Array.Empty<string>(),
                DataItemRelativeBrowsePath = structure.DataItemRelativeBrowsePath ?? Array.Empty<string>()
            });

            structure.ItemName = elementPath + SerializeStructureNames(dataTypeNode, structure.ElementAccess);
        }
        catch
        {
            // If we can't compute the name (e.g. node missing), leave it null.
        }
    }

    /// <summary>
    /// Attempts to parse a single recipe structure from a JSON element.
    /// </summary>
    /// <param name="element">The JSON element to parse.</param>
    /// <param name="structure">The parsed structure, or null if failed.</param>
    /// <returns>True if parsing was successful; otherwise, false.</returns>
    private bool TryParseStructureElement(JsonElement element, out RecipeStructure structure)
    {
        structure = null;
        string dataTypeString = element.GetProperty("dataType").GetString();

        if (!TryParseNodeId(dataTypeString, out var dataTypeNodeId))
        {
            Log.Warning("ImportAndExportRecipesX.TryParseStructureElement",
                $"Failed to deserialize NodeId from '{dataTypeString}', skipping entry");
            return false;
        }

        structure = new RecipeStructure
        {
            ItemRelativeBrowsePath = element.GetProperty("itemRelativeBrowsePath").Deserialize<string[]>(),
            DataItemRelativeBrowsePath = element.GetProperty("dataItemRelativeBrowsePath").Deserialize<string[]>(),
            DataType = dataTypeNodeId,
            ElementAccess = element.GetProperty("elementAccess").Deserialize<ElementAccessStruct>()
        };

        return true;
    }

    /// <summary>
    /// Attempts to parse a NodeId from its string representation (format: "namespace/identifier").
    /// </summary>
    /// <param name="dataTypeString">The string representation of the NodeId.</param>
    /// <param name="nodeId">The parsed NodeId, or null if failed.</param>
    /// <returns>True if parsing was successful; otherwise, false.</returns>
    private bool TryParseNodeId(string dataTypeString, out NodeId nodeId)
    {
        nodeId = null;
        try
        {
            string[] nodeIdParts = dataTypeString.Split('/');
            string nameSpaceIndex = nodeIdParts[0];
            string identifierPart = nodeIdParts[1];
            nodeId = new NodeId(int.Parse(identifierPart), ushort.Parse(nameSpaceIndex));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a CSV file containing recipe data for import.
    /// </summary>
    /// <param name="csvFilePath">The path to the CSV file.</param>
    /// <param name="separator">The CSV field separator character.</param>
    /// <returns>A list of recipe data, or null if parsing failed.</returns>
    private IReadOnlyList<CsvRecipeData> ParseCsvFileForImport(string csvFilePath, char separator)
    {
        try
        {
            string[] lines = File.ReadAllLines(csvFilePath);
            if (lines.Length == 0)
            {
                Log.Warning("ImportAndExportRecipesX.ParseCsvFileForImport", "CSV file is empty");
                return null;
            }

            var recipes = ParseCsvLines(lines, separator);
            Log.Verbose1("ImportAndExportRecipesX.ParseCsvFileForImport", $"Parsed {recipes.Count} recipes from CSV");
            return recipes;
        }
        catch (Exception ex)
        {
            Log.Error("ImportAndExportRecipesX.ParseCsvFileForImport",
                $"Failed to parse CSV file '{csvFilePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses a CSV file containing recipe metadata for import.
    /// </summary>
    /// <param name="metadataFilePath">The path to the metadata CSV file.</param>
    /// <param name="separator">The CSV field separator character.</param>
    /// <returns>A list of recipe metadata, or null if parsing failed.</returns>
    private IReadOnlyList<CsvRecipeMetadata> ParseMetadataFileForImport(string metadataFilePath, char separator)
    {
        try
        {
            string[] lines = File.ReadAllLines(metadataFilePath);
            if (lines.Length == 0)
            {
                Log.Warning("ImportAndExportRecipesX.ParseMetadataFileForImport", "Metadata CSV file is empty");
                return null;
            }

            if (lines.Length < 2)
            {
                Log.Warning("ImportAndExportRecipesX.ParseMetadataFileForImport", "Metadata CSV file has no data rows");
                return null;
            }

            // Parse header to get metadata field names
            var headerFields = ParseCsvLine(lines[0], separator);
            if (headerFields.Count < 2)
            {
                Log.Error("ImportAndExportRecipesX.ParseMetadataFileForImport", "Invalid metadata CSV header");
                return null;
            }

            IReadOnlyList<string> metadataFieldNames = headerFields.Skip(2).ToList(); // Skip RecipeName and RecipeVersion
            var metadataList = new List<CsvRecipeMetadata>();

            // Parse data rows
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                var fields = ParseCsvLine(lines[i], separator);
                if (fields.Count < 2)
                {
                    Log.Warning("ImportAndExportRecipesX.ParseMetadataFileForImport",
                        $"Metadata line {i + 1} has insufficient fields");
                    continue;
                }

                var metadata = new CsvRecipeMetadata
                {
                    RecipeName = fields[0],
                    RecipeVersion = fields[1],
                    MetadataValues = new Dictionary<string, string>()
                };

                // Map metadata values
                for (int j = 0; j < metadataFieldNames.Count && j + 2 < fields.Count; j++)
                {
                    ((Dictionary<string, string>)metadata.MetadataValues)[metadataFieldNames[j]] = fields[j + 2];
                }

                metadataList.Add(metadata);
            }

            Log.Verbose1("ImportAndExportRecipesX.ParseMetadataFileForImport",
                $"Parsed {metadataList.Count} metadata entries from CSV");
            return metadataList;
        }
        catch (Exception ex)
        {
            Log.Error("ImportAndExportRecipesX.ParseMetadataFileForImport",
                $"Failed to parse metadata CSV file '{metadataFilePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses CSV lines into recipe data objects (long format: one row per item).
    /// </summary>
    /// <param name="lines">The lines to parse.</param>
    /// <param name="separator">The CSV field separator character.</param>
    /// <returns>A list of recipe data.</returns>
    private IReadOnlyList<CsvRecipeData> ParseCsvLines(string[] lines, char separator)
    {
        var recipesDict = new Dictionary<string, CsvRecipeData>();

        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var fields = ParseCsvLine(lines[i], separator);
            if (fields.Count < 4)
            {
                Log.Warning("ImportAndExportRecipesX.ParseCsvLines",
                    $"Line {i + 1} has insufficient fields. Expected 4 (RecipeName, RecipeVersion, Item, Value)");
                continue;
            }

            string recipeName = fields[0];
            string recipeVersion = fields[1];
            string itemName = fields[2];
            string value = fields[3];

            string recipeKey = $"{recipeName}|{recipeVersion}";

            if (!recipesDict.TryGetValue(recipeKey, out var recipeData))
            {
                recipeData = new CsvRecipeData
                {
                    RecipeName = recipeName,
                    RecipeVersion = recipeVersion,
                    ItemNames = new List<string>(),
                    Values = new List<string>()
                };
                recipesDict[recipeKey] = recipeData;
            }

            recipeData.ItemNames.Add(itemName);
            recipeData.Values.Add(value);
        }

        return recipesDict.Values.ToList();
    }

    /// <summary>
    /// Parses a single CSV line into fields, handling backtick-wrapped values correctly.
    /// </summary>
    /// <param name="line">The CSV line to parse.</param>
    /// <param name="separator">The field separator character.</param>
    /// <returns>A list of field values.</returns>
    private IReadOnlyList<string> ParseCsvLine(string line, char separator)
    {
        var fields = new List<string>();
        var currentField = new StringBuilder();
        bool inBackticks = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == WrapCharacter)
            {
                inBackticks = HandleBacktickCharacter(line, i, currentField, inBackticks, out int skip);
                i += skip;
            }
            else if (c == separator && !inBackticks)
            {
                fields.Add(currentField.ToString());
                _ = currentField.Clear();
            }
            else
            {
                _ = currentField.Append(c);
            }
        }

        fields.Add(currentField.ToString());
        return fields;
    }

    /// <summary>
    /// Handles wrap characters during CSV parsing, including escaped wrap characters.
    /// </summary>
    /// <param name="line">The line being parsed.</param>
    /// <param name="index">The current character index.</param>
    /// <param name="currentField">The current field being built.</param>
    /// <param name="inBackticks">Whether currently inside wrap characters.</param>
    /// <param name="skip">Number of characters to skip (for escaped wrap characters).</param>
    /// <returns>The new wrap character state.</returns>
    private bool HandleBacktickCharacter(string line, int index, StringBuilder currentField, bool inBackticks, out int skip)
    {
        skip = 0;
        if (inBackticks && index + 1 < line.Length && line[index + 1] == WrapCharacter)
        {
            _ = currentField.Append(WrapCharacter);
            skip = 1; // Skip next wrap character
            return true;
        }
        return !inBackticks;
    }

    /// <summary>
    /// Converts a JsonElement to its corresponding .NET object type.
    /// </summary>
    /// <param name="element">The JSON element to convert.</param>
    /// <returns>The converted object.</returns>
    private object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => ConvertJsonNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Array => ConvertJsonArray(element),
            JsonValueKind.Object => element.GetRawText(),
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// Converts a JSON number to the most appropriate .NET numeric type.
    /// </summary>
    /// <param name="element">The JSON element containing a number.</param>
    /// <returns>The converted numeric value (int, long, double, or decimal).</returns>
    private object ConvertJsonNumber(JsonElement element)
    {
        if (element.TryGetInt32(out int intVal))
            return intVal;
        if (element.TryGetInt64(out long longVal))
            return longVal;
        if (element.TryGetDouble(out double doubleVal))
            return doubleVal;
        return element.GetDecimal();
    }

    /// <summary>
    /// Converts a JSON array to an object array.
    /// </summary>
    /// <param name="element">The JSON element containing an array.</param>
    /// <returns>An object array containing the converted elements.</returns>
    private object ConvertJsonArray(JsonElement element)
    {
        var list = new List<object>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertJsonElementToObject(item));
        }
        return list.ToArray();
    }

    #endregion

    #region Private Methods - Utilities

    /// <summary>
    /// Loads a configuration variable from the LogicObject.
    /// </summary>
    /// <param name="variableName">The name of the variable to load.</param>
    /// <returns>The loaded variable.</returns>
    /// <exception cref="ArgumentException">Thrown when the variable is not found.</exception>
    private IUAVariable LoadConfigurationVariable(string variableName)
    {
        var variableNode = LogicObject.GetVariable(variableName);
        if (variableNode == null)
        {
            Log.Error("ImportAndExportRecipesX.LoadConfigurationVariable",
                $"Variable at path '{variableName}' not found.");
            throw new ArgumentException($"Variable at path '{variableName}' not found.");
        }
        return variableNode;
    }

    /// <summary>
    /// Retrieves the list of all recipes from a recipe schema.
    /// </summary>
    /// <param name="recipeSchema">The recipe schema.</param>
    /// <returns>An array of recipes, or an empty array if retrieval failed.</returns>
    private FTOptix.RecipeX.Recipe[] GetRecipesList(FTOptix.RecipeX.RecipeSchema recipeSchema)
    {
        var recipesListResult = recipeSchema.GetRecipes();
        if (recipesListResult.ResultCode != FTOptix.RecipeX.GetRecipesResultCode.Success)
        {
            Log.Info("ImportAndExportRecipesX.GetRecipesList",
                $"Failed to retrieve recipes from '{recipeSchema.BrowseName}'");
            return Array.Empty<FTOptix.RecipeX.Recipe>();
        }
        return recipesListResult.Recipes;
    }

    /// <summary>
    /// Extracts the CSV separator character from a string, defaulting to comma if empty.
    /// </summary>
    /// <param name="separatorString">The separator string.</param>
    /// <returns>The separator character.</returns>
    private static char ExtractCsvSeparator(string separatorString)
    {
        if (string.IsNullOrEmpty(separatorString))
            return ',';

        if (separatorString.Length > 1)
        {
            Log.Warning("ImportAndExportRecipesX.ExtractCsvSeparator",
                $"CSV separator '{separatorString}' is longer than one character. Using the first character '{separatorString[0]}'.");
        }

        return separatorString[0];
    }

    #endregion

    #region Custom Classes

    private sealed record ExportConfiguration
    {
        public required string CsvPath { get; init; }
        public required string JsonPath { get; init; }
        public required string MetadataPath { get; init; }
        public required char CsvSeparator { get; init; }
        public required bool WrapFields { get; init; }
    }

    private sealed record ImportConfiguration
    {
        public required string CsvPath { get; init; }
        public required string JsonPath { get; init; }
        public required string MetadataPath { get; init; }
        public required char CsvSeparator { get; init; }
    }

    private sealed record ExportContext(CsvWriter RecipeDataWriter, CsvWriter RecipeMetaWriter, JsonStructureWriter JsonWriter) : IDisposable
    {
        public void Dispose()
        {
            RecipeDataWriter?.Dispose();
            RecipeMetaWriter?.Dispose();
            JsonWriter?.Dispose();
        }
    }

    private sealed class ImportStatistics
    {
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int ErrorCount { get; set; }
    }

    private sealed record CsvRecipeData
    {
        public required string RecipeName { get; init; }
        public required string RecipeVersion { get; init; }
        public required List<string> ItemNames { get; init; }
        public required List<string> Values { get; init; }
    }

    private sealed record CsvRecipeMetadata
    {
        public required string RecipeName { get; init; }
        public required string RecipeVersion { get; init; }
        public required IReadOnlyDictionary<string, string> MetadataValues { get; init; }
    }

    private sealed class CsvWriter : IDisposable
    {
        private readonly StreamWriter writer;
        private readonly char separator;
        private readonly bool wrapFields;

        public CsvWriter(StreamWriter writer, char separator, bool wrapFields)
        {
            this.writer = writer;
            this.separator = separator;
            this.wrapFields = wrapFields;
        }

        public void WriteRow(params string[] fields)
        {
            string[] formattedFields = fields.Select((field, index) => FormatField(field, index)).ToArray();
            writer.WriteLine(string.Join(separator, formattedFields));
        }

        private string FormatField(string field, int fieldIndex)
        {
            field ??= string.Empty;

            bool containsSeparator = field.Contains(separator);
            bool containsWrapChar = field.Contains(WrapCharacter);
            bool containsNewline = field.Contains('\n') || field.Contains('\r');
            bool needsWrapping = wrapFields || containsSeparator || containsWrapChar || containsNewline;

            LogFieldWarningsIfNeeded(field, fieldIndex, containsSeparator, containsWrapChar, containsNewline);

            return needsWrapping ? $"{WrapCharacter}{field.Replace(WrapCharacter.ToString(), new string(WrapCharacter, 2))}{WrapCharacter}" : field;
        }

        private void LogFieldWarningsIfNeeded(string field, int fieldIndex, bool containsSeparator, bool containsWrapChar, bool containsNewline)
        {
            if (wrapFields || (!containsSeparator && !containsWrapChar && !containsNewline))
                return;

            var issues = new List<string>();
            if (containsSeparator)
                issues.Add($"separator '{separator}'");
            if (containsWrapChar)
                issues.Add($"wrap character '{WrapCharacter}'");
            if (containsNewline)
                issues.Add("newline character");

            string displayValue = field.Length > 50 ? field.Substring(0, 50) + "..." : field;
            Log.Warning("ImportAndExportRecipesX.CsvWriter",
                $"Field at index {fieldIndex} contains {string.Join(", ", issues)} but WrapFields is disabled. " +
                $"This may disrupt CSV parsing. Value: '{displayValue}'");
        }

        public void Dispose()
        {
            writer?.Dispose();
        }
    }

    private sealed class JsonStructureWriter : IDisposable
    {
        private readonly StreamWriter streamWriter;
        public Utf8JsonWriter Writer { get; }

        public JsonStructureWriter(string path)
        {
            streamWriter = new StreamWriter(path);
            Writer = new Utf8JsonWriter(
                streamWriter.BaseStream,
                new JsonWriterOptions { Indented = true });
        }

        public void Dispose()
        {
            Writer?.Dispose();
            streamWriter?.Dispose();
        }
    }

    private sealed class RecipeStructure
    {
        public string[] ItemRelativeBrowsePath { get; init; }
        public string[] DataItemRelativeBrowsePath { get; init; }
        public NodeId DataType { get; init; }
        public ElementAccessStruct ElementAccess { get; init; }
        public string ItemName { get; set; }
    }

    #endregion

    private LongRunningTask exportTask;
    private LongRunningTask importTask;
}
