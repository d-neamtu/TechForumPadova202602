#region Using directives

using FTOptix.Alarm;
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;

#endregion

public class ImportAndExportAlarms : BaseNetLogic
{
    private static readonly List<string> commonProperties = new List<string>() { "Enabled", "AutoAcknowledge", "AutoConfirm", "Severity", "Message", "LocalizedMessage", "MessageHighHighState", "MessageHighState", "MessageLowState", "MessageLowLowState", "LocalizedMessageHighHighState", "LocalizedMessageHighState", "LocalizedMessageLowState", "LocalizedMessageLowLowState", "HighHighLimit", "HighLimit", "LowLowLimit", "LowLimit", "LastEvent", "InputValue", "NormalStateValue", "Setpoint", "PollingTime", "MaxTimeShelved", "PresetTimeShelved", "LatchingEnabled" };

    /// <summary>
    /// Imports alarms from CSV files into the current project.
    /// This method reads CSV files from a specified directory, processes each file, and creates or updates alarms in the project.
    /// </summary>
    [ExportMethod]
    public void ImportAlarms()
    {
        var folderPath = GetCSVFilePath();
        if (string.IsNullOrEmpty(folderPath))
        {
            Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "No CSV file chosen, please fill the CSVPath variable");
            return;
        }

        char fieldDelimiter = (char)GetFieldDelimiter();
        if (fieldDelimiter == '\0')
            return;

        bool wrapFields = GetWrapFields();

        foreach (string file in Directory.EnumerateFiles(folderPath, "*.csv"))
        {
            try
            {
                using (CsvFileReader reader = new(file) { FieldDelimiter = fieldDelimiter, WrapFields = wrapFields })
                {
                    List<CsvUaObject> csvUaObjects = new();
                    List<string> headerColumns = reader.ReadLine();
                    while (!reader.EndOfFile())
                    {
                        CsvUaObject obj = GetDataFromCsvRow(reader.ReadLine(), headerColumns);
                        // if no data is read from csv, exit from while
                        if (obj == null)
                            continue;
                        csvUaObjects.Add(obj);
                    }
                    if (csvUaObjects.Count == 0)
                    {
                        Log.Warning("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"CSV file '{file}' does not contain valid objects to be imported");
                        continue;
                    }
                    List<string> objectTypesIntoFile = csvUaObjects.Select(o => o.TypeBrowsePath).Distinct().ToList();
                    if (objectTypesIntoFile.Count == 0)
                    {
                        Log.Warning("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"CSV file '{file}' contains no data for object type: {string.Join(",", objectTypesIntoFile)}. CSV file '{file}' will be skipped!");
                        continue;
                    }
                    else if (objectTypesIntoFile.Count > 1)
                    {
                        Log.Warning("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"CSV file '{file}' contains data of more than one object type, found: '{string.Join(", ", objectTypesIntoFile)}'. CSV file '{file}' will be skipped!");
                        continue;
                    }
                    // Get the name of Alarm type by removing all path
                    string csvObjectsCommonType = objectTypesIntoFile.FirstOrDefault();

                    NodeId nativeAlarmTypeNodeID = null;
                    IUANode myNewAlarmType = Project.Current.Find(csvObjectsCommonType);
                    // in case of the type not exist in the project, try to check if is a common FTOptix Alarm type
                    if (myNewAlarmType == null)
                    {
                        // Check if the current alarm type is native
                        nativeAlarmTypeNodeID = (NodeId)typeof(FTOptix.Alarm.ObjectTypes).GetFields().FirstOrDefault(x => x.Name.Equals(csvObjectsCommonType, StringComparison.InvariantCultureIgnoreCase) && x.FieldType == typeof(NodeId), null)?.GetValue(null);
                        if (nativeAlarmTypeNodeID != null)
                        {
                            // Get the node of the native Alarm type
                            myNewAlarmType = InformationModel.Get(nativeAlarmTypeNodeID);
                        }
                        else
                        {
                            Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"Object Type {csvObjectsCommonType} does not exist in the current FTOptix project. CSV file '{file}' will be skipped!");
                            continue;
                        }
                    }

                    // make sure the custom type described by the CSV is an Alarm
                    if (!myNewAlarmType.GetType().IsAssignableTo(typeof(AlarmControllerType)))
                    {
                        Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"Object Type '{csvObjectsCommonType}' is not an an alarm subtype, CSV file '{file}' will be skipped!");
                        continue;
                    }

                    // Loop per each alarm in the CSV file
                    foreach (CsvUaObject csvUaObject in csvUaObjects)
                    {
                        _ = CreateFoldersTreeFromPath(csvUaObject.BrowsePath);
                        Project.Current.Get(csvUaObject.BrowsePath).Children.Remove(csvUaObject.Name);
                        IUAObject myNewAlarm = InformationModel.MakeObject(csvUaObject.Name, myNewAlarmType.NodeId);
                        //Check all common properties and set their values from CSV
                        foreach (string commonProperty in commonProperties)
                        {
                            string commonPropertyValue = csvUaObject.Variables.SingleOrDefault(v => v.Key == commonProperty).Value;
                            if (!string.IsNullOrEmpty(commonPropertyValue))
                            {
                                if (commonProperty.Contains("Message", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    IUAVariable propertyVariable = null;
                                    if (!commonProperty.Contains("Localized"))
                                    {
                                        if (myNewAlarm is LimitAlarmController myNewlimitAlarmController)
                                        {
                                            if (commonProperty.Contains("HighHigh"))
                                            {
                                                propertyVariable = myNewlimitAlarmController.MessageHighHighStateVariable;
                                            }
                                            else if (commonProperty.Contains("LowLow"))
                                            {
                                                propertyVariable = myNewlimitAlarmController.MessageLowLowStateVariable;
                                            }
                                            else if (commonProperty.Contains("High"))
                                            {
                                                propertyVariable = myNewlimitAlarmController.MessageHighStateVariable;
                                            }
                                            else if (commonProperty.Contains("Low"))
                                            {
                                                propertyVariable = myNewlimitAlarmController.MessageLowStateVariable;
                                            }
                                        }
                                        if (commonProperty.Equals("Message", StringComparison.InvariantCultureIgnoreCase))
                                        {
                                            propertyVariable = myNewAlarm.GetOrCreateVariable(commonProperty);
                                        }
                                    }
                                    if (!SetValueProperty(propertyVariable, commonProperty, commonPropertyValue, myNewAlarm.BrowseName))
                                    {
                                        // Set the message or translation key for this alarm
                                        SetAlarmMessage((AlarmController)myNewAlarm, commonProperty, commonPropertyValue);
                                    }
                                }
                                else if (!SetValueProperty(myNewAlarm.GetOrCreateVariable(commonProperty), commonProperty, commonPropertyValue, myNewAlarm.BrowseName))
                                {
                                    if (!IsNotOnlyNumberOrBooleanOrDurationValue().IsMatch(commonPropertyValue))
                                    {
                                        Log.Warning("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"Unable to find variable {commonPropertyValue} for the alarm property {commonProperty} in the alarm {myNewAlarm.BrowseName}, value set as normal and not Dynamic Link");
                                    }
                                    // Try setting the value read in the csv file
                                    TrySetOptimalProperty((AlarmController)myNewAlarm, commonProperty, commonPropertyValue);
                                }
                            }
                        }
                        // Get all uncommon properties of the Alarm to set its the value
                        foreach (var (property, customPropertyValue) in
                                 from property in myNewAlarm.Children.Where(x => !commonProperties.Contains(x.BrowseName))
                                 let valueProperty = csvUaObject.Variables.SingleOrDefault(v => v.Key == property.BrowseName).Value
                                 select (property, valueProperty))
                        {
                            if (!SetValueProperty(myNewAlarm.GetVariable(property.BrowseName), property.BrowseName, customPropertyValue, myNewAlarm.BrowseName))
                            {
                                TrySetOptimalProperty((AlarmController)myNewAlarm, property.BrowseName, customPropertyValue);
                            }
                        }
                        Project.Current.Get(csvUaObject.BrowsePath).Children.Add(myNewAlarm);
                    }
                }
                Log.Info("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "Alarms successfully imported from " + file);
            }
            catch (Exception ex)
            {
                Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"Unable to import alarms from {file}, error message: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Sets the message or translation key for the alarm.
    /// If the property is related to messages (both key and content), it sets the value accordingly.
    /// If the message key is not found, it logs a warning.
    /// </summary>
    /// <param name="myNewAlarm">The alarm controller to set the message for.</param>
    /// <param name="commonProperty">The property name to set.</param>
    /// <param name="propertyValue">The value to set for the property.</param>
    private static void SetAlarmMessage(AlarmController myNewAlarm, string commonProperty, string propertyValue)
    {
        // If the property is related to messages (both key and content)
        foreach (var alarmProperty in myNewAlarm.GetType().GetProperties())
        {
            if (alarmProperty.Name == commonProperty)
            {
                object valueToSet = null;
                if (alarmProperty.Name.Contains("Localized"))
                {
                    string messageKey = propertyValue;
                    if (!string.IsNullOrEmpty(messageKey))
                    {
                        LocalizedText alarmKey = new LocalizedText(myNewAlarm.NodeId.NamespaceIndex, messageKey);
                        if (string.IsNullOrEmpty(InformationModel.LookupTranslation(alarmKey).Text))
                        {
                            Log.Warning("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"Unable to find translation for the key {messageKey} in the Alarm {myNewAlarm.BrowseName}");
                            return;
                        }
                        myNewAlarm.Message = "";
                        valueToSet = new LocalizedText(myNewAlarm.NodeId.NamespaceIndex, messageKey);
                    }
                }
                else
                {
                    string message = propertyValue;
                    // If this alarm already has a translation, we cannot set the plain message
                    LocalizedText alarmMessageKey = (LocalizedText)myNewAlarm.GetType().GetProperty("Localized" + commonProperty).GetValue(myNewAlarm, null);
                    if (alarmMessageKey?.HasTextId == false && !string.IsNullOrEmpty(message))
                        valueToSet = message;
                }
                if (valueToSet != null)
                    alarmProperty.SetValue(myNewAlarm, valueToSet);
            }
        }
    }

    /// <summary>
    /// Sets the value of a property for an alarm.
    /// If the property is not found, it tries to set the value using a dynamic link.
    /// If the property is null or empty, it exits without making any changes.
    /// </summary>
    private static void TrySetOptimalProperty(AlarmController alarm, string propertyName, string propertyValue)
    {
        // If property is null or empty, exit from void
        if (propertyValue?.Length == 0 || propertyValue == null)
            return;
        UAValue defaultPropertyValue;
        try
        {
            defaultPropertyValue = alarm.GetOptionalVariableValue(propertyName);
        }
        catch
        {
            defaultPropertyValue = null;
        }
        if (defaultPropertyValue != null)
        {
            // Switch between the type of the property value
            switch (defaultPropertyValue.Value)
            {
                case bool:
                    alarm.SetOptionalVariableValue(propertyName, ConvertStringToBool(propertyValue));
                    break;

                case int:
                    if (!int.TryParse(propertyValue, out int intValue))
                        _ = SetValueProperty(alarm.GetOrCreateVariable(propertyName), propertyName, propertyValue, alarm.BrowseName);
                    else
                        alarm.SetOptionalVariableValue(propertyName, intValue);
                    break;

                case double:
                    var defaultProperty = alarm.GetOrCreateVariable(propertyName);
                    if (defaultProperty.DataType == OpcUa.DataTypes.Duration && TimeSpan.TryParse(propertyValue, CultureInfo.InvariantCulture.DateTimeFormat, out TimeSpan durationValue))
                    {
                        alarm.SetOptionalVariableValue(propertyName, durationValue.TotalMilliseconds);
                    }
                    else
                    {
                        if (!double.TryParse(propertyValue, out double doubleValue))
                        {
                            _ = SetValueProperty(alarm.GetOrCreateVariable(propertyName), propertyName, propertyValue, alarm.BrowseName);
                        }
                        else
                        {
                            alarm.SetOptionalVariableValue(propertyName, doubleValue);
                        }
                    }
                    break;

                case float:
                    if (!float.TryParse(propertyValue, out float floatValue))
                        _ = SetValueProperty(alarm.GetOrCreateVariable(propertyName), propertyName, propertyValue, alarm.BrowseName);
                    else
                        alarm.SetOptionalVariableValue(propertyName, floatValue);
                    break;

                case ushort:
                    if (!ushort.TryParse(propertyValue, out ushort ushortValue))
                        _ = SetValueProperty(alarm.GetOrCreateVariable(propertyName), propertyName, propertyValue, alarm.BrowseName);
                    else
                        alarm.SetOptionalVariableValue(propertyName, ushortValue);
                    break;

                case uint:
                    if (!uint.TryParse(propertyValue, out uint uintValue))
                        _ = SetValueProperty(alarm.GetOrCreateVariable(propertyName), propertyName, propertyValue, alarm.BrowseName);
                    else
                        alarm.SetOptionalVariableValue(propertyName, uintValue);
                    break;

                case ulong:
                    if (!ulong.TryParse(propertyValue, out ulong ulongValue))
                        _ = SetValueProperty(alarm.GetOrCreateVariable(propertyName), propertyName, propertyValue, alarm.BrowseName);
                    else
                        alarm.SetOptionalVariableValue(propertyName, ulongValue);
                    break;

                case LocalizedText:
                    var alarmKey = new LocalizedText(alarm.NodeId.NamespaceIndex, propertyValue);
                    UAValue valueToSet;
                    if (string.IsNullOrEmpty(InformationModel.LookupTranslation(alarmKey).Text))
                    {
                        Log.Warning("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"Unable to find translation for the key {propertyValue} in the Alarm property {propertyName} in {alarm.BrowseName}");
                        valueToSet = new(propertyValue);
                    }
                    else
                    {
                        valueToSet = new(new LocalizedText(alarm.NodeId.NamespaceIndex, propertyValue));
                    }
                    alarm.SetOptionalVariableValue(propertyName, valueToSet);
                    break;

                default:
                    // in case of is an unknown type, try to manage as dynamic link, otherwise set the value
                    if (!SetValueProperty(alarm.GetOrCreateVariable(propertyName), propertyName, propertyValue, alarm.BrowseName))
                    {
                        alarm.SetOptionalVariableValue(propertyName, propertyValue);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Converts a given string to a boolean value.
    /// Returns true if the string is equal to "1" or "true".
    /// </summary>
    /// <param name="value">The string value to convert.</param>
    /// <returns>
    /// A boolean value indicating whether the string was converted successfully.
    /// </returns>
    private static bool ConvertStringToBool(string value)
    {
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets the value of a property for an alarm field.
    /// If the property is an alias, it creates a DynamicLink variable.
    /// If the property is a variable, it sets the dynamic link to the variable.
    /// If the property is an array or bit notation, it extracts the value accordingly.
    /// If the property value is not found, it logs a warning.
    /// </summary>
    /// <param name="alarmField">The alarm field to set the value for.</param>
    /// <param name="propertyBrowseName">The browse name of the property.</param>
    /// <param name="propertyValue">The value to set for the property.</param>
    /// <param name="alarmName">The name of the alarm.</param>
    private static bool SetValueProperty(IUAVariable alarmField, string propertyBrowseName, string propertyValue, string alarmName)
    {
        if (alarmField == null || string.IsNullOrEmpty(propertyValue))
            return false;
        IUAVariable dynamicLinkVariable;
        // First check if it is an Alias or a Variable. For Alias check if the string start with a value in angle brackets
        if (Regex.IsMatch(propertyValue, @"^\{.*?\}"))
        {
            // Create a Variable of type DynamicLink with datatype NodePath
            DynamicLink aliasDynamicLinkVar = InformationModel.MakeVariable<DynamicLink>("AppCrAl_" + propertyBrowseName, FTOptix.Core.DataTypes.NodePath);
            // Set value with the full Alias path {AliasName}/Path1/Path2/../Var
            aliasDynamicLinkVar.Value = propertyValue;
            // Set reference to Alarm field with the DynamicLink
            alarmField.Refs.AddReference(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink, aliasDynamicLinkVar);
        }
        else
        {
            // If the string in the csv is an array, I extract the value. Regular expression check square brackets with digit values
            string findBracketsRegex = @"\[.*?\d\]";
            // If the string in the csv is a variable with bit notation (var.<bit position>), I extract the value. Regular check the presence of numbers after the dot.
            string findIndexedWords = @"\.\d{1,2}\z";
            if (Regex.IsMatch(propertyValue, findBracketsRegex) || Regex.IsMatch(propertyValue, findIndexedWords))
            {
                Regex valueToExtract;
                // get the variable name without brackets or bit notation
                string targetVar = Regex.Replace(propertyValue, findBracketsRegex, "");
                targetVar = Regex.Replace(targetVar, findIndexedWords, "");
                // Variable name is the full key-value without the match of regex. try to get from the project
                dynamicLinkVariable = Project.Current.GetVariable(targetVar);
                // If the result of GetVariable is null, write a warning and return
                if (dynamicLinkVariable == null)
                {
                    Log.Warning("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"Unable to find the variable {targetVar} for the alarm property {propertyBrowseName} in the alarm {alarmName}");
                    return true;
                }
                // First set the variable (without brackets or bit notation)
                alarmField.SetDynamicLink(dynamicLinkVariable);
                // in case of Array set the variable name with the brackets and the index
                if (Regex.IsMatch(propertyValue, findBracketsRegex))
                {
                    // Replace the value of dynamic link with variable name and the brackets (the regex match value)
                    valueToExtract = new Regex(findBracketsRegex);
                    alarmField.GetVariable("DynamicLink").Value += valueToExtract.Match(propertyValue).Value;
                }
                // In case of bit notation (also applicable to arrays, that is why the check occurs after the first one), set the variable name in dynamic link with bit notation
                if (Regex.IsMatch(propertyValue, findIndexedWords))
                {
                    // Replace the value of dynamic link with variable name and the bit notation
                    valueToExtract = new Regex(findIndexedWords);
                    alarmField.GetVariable("DynamicLink").Value += valueToExtract.Match(propertyValue).Value;
                }
            }
            else
            {
                // Try to get the variable from the project
                dynamicLinkVariable = Project.Current.GetVariable(propertyValue);
                // If the result of GetVariable is null, write a warning and return
                if (dynamicLinkVariable == null)
                {
                    return false;
                }
                // Create the dynamic link
                alarmField.SetDynamicLink(dynamicLinkVariable);
            }
        }
        return true;
    }

    /// <summary>
    /// This method retrieves a list of IUAObjectType representing Alarm Type objects based on specific criteria.
    /// <example>
    /// For example:
    /// <code>
    /// var alarmTypes = GetAlarmTypeList();
    /// </code>
    /// will return a list containing specific Alarm Type objects related to Limit, Exclusive Limit, and Non-Exclusive Limit Alarm Controllers.
    /// </example>
    /// </summary>
    /// <returns>
    /// A list of IUAObjectType instances representing the selected Alarm Types.
    /// </returns>
    /// <remarks>
    /// The method filters out abstract types from the list before adding them to the alarms collection.
    /// It also includes user-defined types with their corresponding namespace index matching the project's namespace index.
    /// </remarks>
    private List<IUAObjectType> GetAlarmTypeList()
    {
        List<NodeId> filteredTypes = [FTOptix.Alarm.ObjectTypes.LimitAlarmController, FTOptix.Alarm.ObjectTypes.ExclusiveLimitAlarmController, FTOptix.Alarm.ObjectTypes.NonExclusiveLimitAlarmController];
        var alarms = new List<IUAObjectType>();
        var projectNamespaceIndex = LogicObject.NodeId.NamespaceIndex;
        // Insert code to be executed by the method
        var alarmControllerType = InformationModel.Get(FTOptix.Alarm.ObjectTypes.AlarmController);
        var allControllerTypes = new List<IUAObjectType>();
        CollectRecursive((IUAObjectType)alarmControllerType, allControllerTypes);
        var concreteTypes = allControllerTypes.Where(x => !filteredTypes.Contains(x.NodeId)).ToList().FindAll(type => !type.IsAbstract);
        alarms.AddRange(concreteTypes);
        var userDefinedTypes = concreteTypes.FindAll(type => type.NodeId.NamespaceIndex == projectNamespaceIndex);
        alarms.AddRange(userDefinedTypes);
        return alarms;
    }

    /// <summary>
    /// Exports alarms to CSV files.
    /// This method retrieves alarms from the project, processes them, and writes their properties to CSV files.
    /// </summary>
    [ExportMethod]
    public void ExportAlarms()
    {
        var csvDir = GetCSVFilePath();
        // Make sure path is not empty
        if (string.IsNullOrEmpty(csvDir))
        {
            Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "No CSV file chosen, please fill the CSVPath variable");
            return;
        }

        Log.Info("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "Exporting alarms to: " + csvDir);

        char? fieldDelimiter = GetFieldDelimiter();
        if (fieldDelimiter == null || fieldDelimiter == '\0')
            return;

        bool wrapFields = GetWrapFields();

        List<IUAObjectType> allAlarmsInProject = GetAlarmTypeList();

        foreach (var singleAlarmToExport in allAlarmsInProject)
        {
            List<IUAObject> alarmTypesToExport = GetAlarmList(singleAlarmToExport.NodeId);

            // Export CSV only if we have at least one alarm to process
            if (alarmTypesToExport.Count > 0)
            {
                string pathAlarmType = GetBrowsePathFromIuaNode(InformationModel.Get(singleAlarmToExport.NodeId));
                List<string> propertiesFields = new();
                List<string> valuesFields = new();
                // Add standard fields to the alarm
                propertiesFields.Add("Name");
                propertiesFields.Add("Type");
                propertiesFields.Add("Path");
                propertiesFields.Add("Message");
                propertiesFields.Add("LocalizedMessage");
                if (singleAlarmToExport.GetType().IsAssignableTo(typeof(LimitAlarmControllerType)))
                {
                    propertiesFields.Add("MessageHighHighState");
                    propertiesFields.Add("MessageHighState");
                    propertiesFields.Add("MessageLowState");
                    propertiesFields.Add("MessageLowLowState");
                    propertiesFields.Add("LocalizedMessageHighHighState");
                    propertiesFields.Add("LocalizedMessageHighState");
                    propertiesFields.Add("LocalizedMessageLowState");
                    propertiesFields.Add("LocalizedMessageLowLowState");
                }

                // Add any additional custom field to the list
                CheckAlarmProperties(singleAlarmToExport.NodeId, propertiesFields);

                try
                {
                    // Write CSV header
                    using var csvWriter = new CsvFileWriter(Path.Combine(csvDir, singleAlarmToExport.BrowseName + ".csv"))
                    {
                        FieldDelimiter = fieldDelimiter.Value,
                        WrapFields = wrapFields
                    };
                    csvWriter.WriteLine(propertiesFields.ToArray());
                    foreach (var alarm in alarmTypesToExport.Cast<AlarmController>())
                    {
                        valuesFields = new List<string>
                        {
                            alarm.BrowseName,
                            pathAlarmType.Split("/").LastOrDefault(),
                            GetBrowsePathWithoutNodeName(alarm)
                        };
                        foreach (var item in propertiesFields)
                        {
                            switch (item)
                            {
                                case "Name":
                                case "Type":
                                case "Path":
                                case "InputValueArrayIndex":
                                case "InputValueArraySubIndex":
                                    break;

                                case "InputValue":
                                    valuesFields.Add(ExportAlarmVariableOrValue(alarm.InputValueVariable));
                                    break;

                                case "Message":
                                    valuesFields.Add(ExportAlarmVariableOrValue(alarm.MessageVariable));
                                    break;

                                case "LocalizedMessage":
                                    valuesFields.Add(ExtractAlarmMessageKey(alarm.LocalizedMessage));
                                    break;

                                case "MessageHighHighState":
                                    valuesFields.Add(ExportAlarmVariableOrValue(((LimitAlarmController)alarm).MessageHighHighStateVariable));
                                    break;

                                case "MessageHighState":
                                    valuesFields.Add(ExportAlarmVariableOrValue(((LimitAlarmController)alarm).MessageHighStateVariable));
                                    break;

                                case "MessageLowState":
                                    valuesFields.Add(ExportAlarmVariableOrValue(((LimitAlarmController)alarm).MessageLowStateVariable));
                                    break;

                                case "MessageLowLowState":
                                    valuesFields.Add(ExportAlarmVariableOrValue(((LimitAlarmController)alarm).MessageLowLowStateVariable));
                                    break;

                                case "LocalizedMessageHighHighState":
                                    valuesFields.Add(ExtractAlarmMessageKey(((LimitAlarmController)alarm).LocalizedMessageHighHighState));
                                    break;

                                case "LocalizedMessageHighState":
                                    valuesFields.Add(ExtractAlarmMessageKey(((LimitAlarmController)alarm).LocalizedMessageHighState));
                                    break;

                                case "LocalizedMessageLowState":
                                    valuesFields.Add(ExtractAlarmMessageKey(((LimitAlarmController)alarm).LocalizedMessageLowState));
                                    break;

                                case "LocalizedMessageLowLowState":
                                    valuesFields.Add(ExtractAlarmMessageKey((alarm as LimitAlarmController).LocalizedMessageLowLowState));
                                    break;

                                case "MaxTimeShelved":
                                    valuesFields.Add(ExportAlarmVariableOrValue(alarm.MaxTimeShelvedVariable));
                                    break;
                                case "PresetTimeShelved":
                                    valuesFields.Add(ExportAlarmVariableOrValue(alarm.PresetTimeShelvedVariable));
                                    break;

                                case "LatchingEnabled":
                                    valuesFields.Add(ExportAlarmVariableOrValue(alarm.LatchingEnabledVariable));
                                    break;

                                default:
                                    valuesFields.Add(ExportAlarmVariableOrValue(alarm.GetVariable(item)));
                                    break;
                            }
                        }
                        // Write CSV content per each alarm
                        csvWriter.WriteLine(valuesFields.ToArray());
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "Unable to export alarms: " + ex);
                }
            }
            else
            {
                Log.Info("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "No alarms to export for " + singleAlarmToExport.BrowseName);
            }
        }
        Log.Info("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "Alarms successfully exported to " + csvDir);
    }

    /// <summary>
    /// This method extracts the text ID from a localized text message if it exists; otherwise, returns an empty string.
    /// <example>
    /// For example:
    /// <code>
    /// string key = ExtractAlarmMessageKey(new LocalizedText("alarm_message", "text_id"));
    /// </code>
    /// results in <c>key</c>'s having the value "text_id".
    /// </example>
    /// </summary>
    /// <param name="message">The localized text message containing the text ID.</param>
    /// <returns>
    /// A string representing the extracted text ID or an empty string if no text ID was found.
    /// </returns>
    private string ExtractAlarmMessageKey(LocalizedText message)
    {
        if (message?.HasTextId == true)
        {
            return message.TextId;
        }
        else
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Converts a double duration value to a human-readable string representation.
    /// Example usage:
    /// <code>
    /// string timeString = DurationToString(3600.5);
    /// </code>
    /// The output will be something like "0'.'1h':48:'0.5''".
    /// </summary>
    /// <param name="durationValue">The duration value to convert.</param>
    /// <returns>A string representing the duration in a human-readable format.</returns>
    private string DurationToString(double durationValue)
    {
        return TimeSpan.FromMilliseconds(durationValue).ToString("d'.'hh':'mm':'ss'.'fff");
    }

    /// <summary>
    /// This method creates a regular expression that matches strings consisting of integers, time durations, or boolean values.
    /// <example>
    /// For example:
    /// <code>
    /// Regex pattern = IsNotOnlyNumberOrBooleanOrDurationValue();
    /// string testString = "123";
    /// bool isValid = pattern.IsMatch(testString); // Returns true if the string matches the pattern, false otherwise.
    /// </code>
    /// </example>
    /// </summary>
    /// <returns>
    /// A compiled regular expression object matching strings with specified criteria.
    /// </returns>
    private static Regex IsNotOnlyNumberOrBooleanOrDurationValue()
    {
        return new Regex(@"^-?\d+$|^\d\.\d{2}:\d{2}:\d{2}\.\d{3}$|^(True|False)$");
    }

    /// <summary>
    /// Retrieves the browse path from an IUA node without including the node's name.
    /// </summary>
    /// <param name="uaObj">The UANode object containing the browse path information.</param>
    /// <returns>The browse path excluding the node's name if it contains a slash, otherwise the entire browse path.</returns>
    /// <example>
    /// For example:
    /// <code>
    /// string path = GetBrowsePathWithoutNodeName(new UANode());
    /// </code>
    /// results in <c>path</c> being set to the full browse path.
    /// </example>
    private static string GetBrowsePathWithoutNodeName(IUANode uaObj)
    {
        var browsePath = GetBrowsePathFromIuaNode(uaObj);
        return browsePath.Contains('/') ? browsePath.Substring(0, browsePath.LastIndexOf("/", StringComparison.Ordinal)) : browsePath;
    }

    private static string GetBrowsePathFromIuaNode(IUANode uaNode) => ClearPathFromProjectInfo(Log.Node(uaNode));

    /// <summary>
    /// This method clears the project information from a given path.
    /// If the project name is found within the path, it removes the project's directory structure.
    /// Otherwise, it returns the original path unchanged.
    /// </summary>
    /// <param name="path">The path from which to clear the project information.</param>
    /// <returns>A new path with the project information cleared.</returns>
    /// <remarks>
    /// Example usage:
    /// <code>
    /// string newPath = ClearPathFromProjectInfo(@"C:\Users\John Doe\Documents\MyProject\MyFile.txt");
    /// </code>
    /// would result in <c>"C:\Users\John Doe\Documents\MyFile.txt"</c>.
    /// </remarks>
    private static string ClearPathFromProjectInfo(string path)
    {
        var projectName = Project.Current.BrowseName + "/";
        var occurrence = path.IndexOf(projectName);
        if (occurrence == -1)
        {
            return path;
        }
        return path.Substring(occurrence + projectName.Length);
    }

    /// <summary>
    /// This method exports the value of a given IUAVariable or its linked variable.
    /// If the IUAVariable is null, it returns an empty string.
    /// If the IUAVariable has a DynamicLink, it resolves the path and returns it.
    /// If the IUAVariable is not linked, it returns its value.
    /// </summary>
    /// <param name="varToAnalyze">The IUAVariable to analyze and export.</param>
    private string ExportAlarmVariableOrValue(IUAVariable varToAnalyze)
    {
        if (varToAnalyze == null)
            return string.Empty;
        // Get the DynamicLink (variable linked) of the Dynamic Link
        DynamicLink inputPath = (DynamicLink)varToAnalyze.GetVariable("DynamicLink");
        // If inputPath is null, return the value of the variable
        if (inputPath == null)
        {
            if (varToAnalyze.DataType == OpcUa.DataTypes.Duration)
            {
                return DurationToString(varToAnalyze.Value);
            }
            else if (varToAnalyze.Value.Value is LocalizedText localizedTextValue)
            {
                if (localizedTextValue.HasTextId)
                {
                    return localizedTextValue.TextId;
                }
                else
                {
                    return localizedTextValue.Text ?? string.Empty;
                }
            }
            else
            {
                return varToAnalyze.Value ?? string.Empty;
            }
        }

        // Resolve the path of variable linked to the field
        PathResolverResult resolvePathResult = LogicObject.Context.ResolvePath(varToAnalyze, inputPath.Value);
        // If resolvePathResult is null, return empty string
        if (resolvePathResult == null)
            return string.Empty;
        string pathToInputValueNode;
        // Check if is an Alias or Variable
        if (resolvePathResult.AliasSpecification != null && resolvePathResult.AliasSpecification.AliasTokenPath != "")
        {
            // If is alias return the full value of inputPath like {aliasName}\<struct>
            pathToInputValueNode = inputPath.Value;
        }
        else
        {
            // Get the path in string format of the variable for write to CSV file
            pathToInputValueNode = MakeBrowsePath(resolvePathResult.ResolvedNode);
            // if the Indexes is plus then 0, mean is an array (more indexes, more dimension of array)
            if (resolvePathResult.Indexes?.Length > 0)
            {
                StringBuilder stringBracketBuilder = new StringBuilder();
                // Open square brackets for string notation
                _ = stringBracketBuilder.Append("[");
                // for each index append the value on the string with a , as separator
                for (int i = 0; i < resolvePathResult.Indexes.Length; i++)
                {
                    _ = stringBracketBuilder.Append(resolvePathResult.Indexes[i]);
                    // if not the last element add a ,
                    if (i != resolvePathResult.Indexes.Length - 1)
                        _ = stringBracketBuilder.Append(",");
                }
                // Close the square brackets for string notation
                _ = stringBracketBuilder.Append("]");
                pathToInputValueNode += stringBracketBuilder.ToString();
            }
            // Try if the variable is an indexed word
            string dynamicLinkPath = varToAnalyze.GetVariable("DynamicLink").Value.Value.ToString();
            if (Regex.IsMatch(dynamicLinkPath, "\\.\\d*?\\z"))
            {
                var splitPath = dynamicLinkPath.Split(".");
                pathToInputValueNode += "." + splitPath[^1];
            }
        }
        return pathToInputValueNode;
    }

    /// <summary>
    /// This method constructs a browse path for a given UANode starting from its owner up to the root project.
    /// <example>
    /// For instance:
    /// <code>
    /// string result = MakeBrowsePath(new Node());
    /// </code>
    /// would yield <c>result</c> with the path as "Node".
    /// </example>
    /// </summary>
    /// <param name="node">The UANode whose browse path needs to be constructed.</param>
    /// <returns>A string representing the full browse path of the node.</returns>
    private static string MakeBrowsePath(IUANode node)
    {
        string path = node.BrowseName;
        IUANode current = node.Owner;

        while (current != Project.Current)
        {
            path = $"{current.BrowseName}/{path}";
            current = current.Owner;
        }
        return path;
    }

    /// <summary>
    /// Retrieves a list of IUAObjects representing alarms for a specified type.
    /// </summary>
    /// <param name="alarmTypeNodeId">A NodeId representing the type of alarms to retrieve.</param>
    /// <returns>A List containing IUAObjects that represent alarms of the specified type.</returns>
    private List<IUAObject> GetAlarmList(NodeId alarmTypeNodeId)
    {
        var alarms = new List<IUAObject>();
        var typedAlarms = GetAlarmsByType(alarmTypeNodeId);
        alarms.AddRange(typedAlarms);
        return alarms;
    }

    /// <summary>
    /// Retrieves a list of alarms associated with a specified object type.
    /// <example>
    /// For example:
    /// <code>
    /// var alarms = GetAlarmsByType(new NodeId("ObjectType"));
    /// </code>
    /// results in <c>alarms</c> containing objects representing alarms for the given type.
    /// </example>
    /// </summary>
    /// <param name="type">The object type whose alarms are to be retrieved.</param>
    /// <returns>An IReadOnlyList&lt;IUAObject&gt; containing alarms related to the specified object type.</returns>
    /// <remarks>
    /// The method retrieves all alarms that have the specified object type as their definition type.
    /// </remarks>
    /// <returns>
    /// An IReadOnlyList&lt;IUAObject&gt; where each element represents an alarm object.
    /// </returns>
    private IReadOnlyList<IUAObject> GetAlarmsByType(NodeId type)
    {
        var alarmType = LogicObject.Context.GetObjectType(type);
        var alarms = alarmType.InverseRefs.GetObjects(OpcUa.ReferenceTypes.HasTypeDefinition, false);
        return alarms;
    }

    /// <summary>
    /// This method retrieves the CSV file path from an IUAVariable resource URI and returns its directory.
    /// <example>
    /// For example:
    /// <code>
    /// string filePath = GetCSVFilePath();
    /// </code>
    /// results in <c>filePath</c> containing the directory path of the CSV file.
    /// </example>
    /// </summary>
    /// <returns>The directory path of the CSV file as a string.</returns>
    private string GetCSVFilePath()
    {
        string csvPath;
        FileInfo fi;
        try
        {
            var csvPathVariable = LogicObject.Get<IUAVariable>("CSVPath");
            csvPath = new ResourceUri(csvPathVariable.Value).Uri;
            fi = new FileInfo(csvPath);
        }
        catch (Exception ex)
        {
            Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "Cannot read CSV path, exception: " + ex.Message);
            return string.Empty;
        }
        return fi.DirectoryName;
    }

    /// <summary>
    /// This method retrieves the field delimiter character from the logic object.
    /// </summary>
    /// <returns>
    /// A nullable char representing the field delimiter character.
    /// </returns>
    private char? GetFieldDelimiter()
    {
        var separatorVariable = LogicObject.GetVariable("CharacterSeparator");
        if (separatorVariable == null)
        {
            Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "CharacterSeparator variable not found");
            return null;
        }

        string separator = separatorVariable.Value;

        if (separator.Length != 1)
        {
            Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "Wrong CharacterSeparator configuration. Please insert a char");
            return null;
        }

        if (char.TryParse(separator, out char result))
            return result;

        return null;
    }

    /// <summary>
    /// This method retrieves the 'WrapFields' variable from the logic object.
    /// If the variable is not found, it logs an error message and returns false.
    /// Otherwise, it returns the value of the 'WrapFields' variable.
    /// </summary>
    /// <returns>
    /// A boolean indicating whether the variable was successfully retrieved or not.
    /// </returns>
    private bool GetWrapFields()
    {
        var wrapFieldsVariable = LogicObject.GetVariable("WrapFields");
        if (wrapFieldsVariable == null)
        {
            Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, "WrapFields variable not found");
            return false;
        }

        return wrapFieldsVariable.Value;
    }

    /// <summary>
    /// This method creates a folder tree in the project based on the provided path.
    /// </summary>
    /// <param name="path"> The path to create the folder tree.</param>
    /// <returns>
    /// A boolean indicating whether the folder tree was successfully created or not.
    /// </returns>
    private static bool CreateFoldersTreeFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        var segments = path.Split('/').ToList();
        string updatedSegment = "";
        string segmentsAccumulator = "";
        try
        {
            foreach (var s in segments)
            {
                if (segmentsAccumulator?.Length == 0)
                    updatedSegment = s;
                else
                    updatedSegment = $"{updatedSegment}/{s}";
                var folder = InformationModel.MakeObject<Folder>(s);
                var folderAlreadyExists = Project.Current.GetObject(updatedSegment) != null;
                if (!folderAlreadyExists)
                {
                    if (segmentsAccumulator?.Length == 0)
                        Project.Current.Add(folder);
                    else
                        Project.Current.GetObject(segmentsAccumulator).Children.Add(folder);
                }
                segmentsAccumulator = updatedSegment;
            }
        }
        catch (Exception e)
        {
            Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"Cannot create folder, error {e.Message}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// This method extracts data from a CSV row into a CsvUaObject.
    /// If the object is invalid, it logs an error message and returns null.
    /// Otherwise, it populates the variables property with the corresponding values from the header and remaining lines.
    /// </summary>
    /// <param name="line">The list containing the row data.</param>
    /// <param name="header">The list containing column headers.</param>
    /// <returns>A CsvUaObject populated with the extracted data or null on failure.</returns>
    /// <remarks>
    /// The method assumes that each element in `line` corresponds to a variable's name, type browse path, and actual value.
    /// It iterates through the header and adds each non-header item as a variable within the CsvUaObject.
    /// </remarks>
    private static CsvUaObject GetDataFromCsvRow(List<string> line, List<string> header)
    {
        var csvUaObject = new CsvUaObject
        {
            Name = line[0],
            TypeBrowsePath = line[1],
            BrowsePath = line[2]
        };

        if (!csvUaObject.IsValid())
        {
            Log.Error("ImportAndExportAlarms." + MethodBase.GetCurrentMethod().Name, $"Invalid object with name {csvUaObject.Name}. Please check its properties.");
            return null;
        }

        for (var i = 3; i < header.Count; i++)
        {
            csvUaObject.Variables.Add(header[i], line[i]);
        }

        return csvUaObject;
    }

    private sealed class CsvUaObject
    {
        public string Name { get; set; }
        public string TypeBrowsePath { get; set; }
        public string BrowsePath { get; set; }
        public Dictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Validates if all required fields (TypeBrowsePath, Name, BrowsePath) are not null or whitespace-only.
        /// <example>
        /// For example:
        /// <code>
        /// bool isValid = IsValid();
        /// </code>
        /// results in <c>isValid</c> being true if all fields are valid, otherwise false.
        /// </example>
        /// </summary>
        /// <returns>
        /// A boolean indicating whether all required fields are valid.
        /// </returns>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(TypeBrowsePath) &&
                   !string.IsNullOrWhiteSpace(Name) &&
                   !string.IsNullOrWhiteSpace(BrowsePath);
        }
    }

    private class CsvFileReader : IDisposable
    {
        public char FieldDelimiter { get; set; } = ',';

        public char QuoteChar { get; set; } = '"';

        public bool WrapFields { get; set; } = false;

        public bool IgnoreMalformedLines { get; set; } = false;

        public CsvFileReader(string filePath)
        {
            streamReader = new StreamReader(filePath, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Checks if the end of the file has been reached.
        /// <example>
        /// For example:
        /// <code>
        /// bool isEOF = EndOfFile(streamReader);
        /// </code>
        /// will return true if the end of the file was reached, otherwise false.
        /// </example>
        /// </summary>
        /// <returns>
        /// A boolean indicating whether the end of the file has been reached or not.
        /// </returns>
        public bool EndOfFile()
        {
            return streamReader.EndOfStream;
        }

        /// <summary>
        /// Reads a line from the stream reader and processes it based on wrapping fields or without.
        /// If end-of-file is reached, an empty list is returned.
        /// Otherwise, the processed line is added to the result list.
        /// </summary>
        /// <returns>A list containing the parsed line.</returns>
        /// <remarks>
        /// Wraps lines around fields for parsing, or parses the entire line as a single string.
        /// Increments the current line number after processing each line.
        /// </remarks>
        public List<string> ReadLine()
        {
            if (EndOfFile())
                return new List<string>();

            var line = streamReader.ReadLine();

            var result = WrapFields ? ParseLineWrappingFields(line) : ParseLineWithoutWrappingFields(line);

            currentLineNumber++;
            return result;
        }

        /// <summary>
        /// This method parses a line without wrapping fields.
        /// It splits the line into fields based on the specified field delimiter.
        /// If the line is empty and IgnoreMalformedLines is true, it returns an empty list.
        /// </summary>
        /// <param name="line"></param>
        /// <returns>
        /// A list of strings representing the parsed fields.
        /// </returns>
        private List<string> ParseLineWithoutWrappingFields(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                if (IgnoreMalformedLines)
                {
                    return new List<string>();
                }
                else
                {
                    throw new FormatException($"Error processing line {currentLineNumber}. Line cannot be empty");
                }
            }
            return line.Split(FieldDelimiter).ToList();
        }

        /// <summary>
        /// This method parses a line with wrapping fields.
        /// </summary>
        /// <param name="line"> The line to parse.</param>
        /// <returns>
        /// A list of strings representing the parsed fields.
        /// </returns>
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
                                return new List<string>();
                            else
                                throw new FormatException($"{lineErrorMessage}. Expected quotation marks at column {i + 1}");
                        }

                        fieldParsing = true;
                    }
                    else
                    {
                        if (IsQuoteChar(line, i))
                        {
                            fieldParsing = true;
                        }
                        else if (!IsFieldDelimiter(line, i))
                        {
                            if (IgnoreMalformedLines)
                                return new List<string>();
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

        /// <summary>
        /// This method checks if an escaped quote character is present at the specified index in the given string.
        /// <example>
        /// For example:
        /// <code>
        /// bool result = IsEscapedQuoteChar("Hello\"World", 7);
        /// </code>
        /// results in <c>result</c> being true.
        /// </example>
        /// </summary>
        /// <param name="line">The input string to check.</param>
        /// <param name="i">The index to look for the escaped quote character.</param>
        /// <returns>
        /// A boolean indicating whether the escaped quote character was found.
        /// </returns>
        private bool IsEscapedQuoteChar(string line, int i)
        {
            return line[i] == QuoteChar && i != line.Length - 1 && line[i + 1] == QuoteChar;
        }

        /// <summary>
        /// This method checks if the character at a given index in a string is a quote character.
        /// <example>
        /// For example:
        /// <code>
        /// bool result = IsQuoteChar("Hello \"World\"", 6);
        /// </code>
        /// would result in <c>result</c> being true because the 7th character ('\"') is a quote character.
        /// </example>
        /// </summary>
        /// <param name="line">The string to check within.</param>
        /// <param name="i">The index of the character to check.</param>
        /// <returns>
        /// A boolean value indicating whether the character at the specified index is a quote character.
        /// </returns>
        private bool IsQuoteChar(string line, int i)
        {
            return line[i] == QuoteChar;
        }

        /// <summary>
        /// This method checks if the character at the specified index within a string represents a field delimiter.
        /// <example>
        /// For example:
        /// <code>
        /// bool result = IsFieldDelimiter("Hello World", 5);
        /// </code>
        /// would return true because ' ' (space) is considered a field delimiter.
        /// </example>
        /// </summary>
        /// <param name="line">The input string containing fields separated by delimiters.</param>
        /// <param name="i">The index position to check for the field delimiter.</param>
        /// <returns>
        /// A boolean value indicating whether the character at the given index is a field delimiter.
        /// </returns>
        private bool IsFieldDelimiter(string line, int i)
        {
            return line[i] == FieldDelimiter;
        }

        /// <summary>
        /// This method checks if the character at the specified index in a string is whitespace.
        /// <example>
        /// For example:
        /// <code>
        /// bool result = IsWhiteSpace("Hello World", 4);
        /// </code>
        /// results in <c>result</c>'s having the value true because 'W' is a whitespace character.
        /// </example>
        /// </summary>
        /// <param name="line">The input string.</param>
        /// <param name="i">The index of the character to check.</param>
        /// <returns>
        /// A boolean indicating whether the character at the given index is whitespace.
        /// </returns>
        private static bool IsWhiteSpace(string line, int i)
        {
            return Char.IsWhiteSpace(line[i]);
        }

        private readonly StreamReader streamReader;
        private int currentLineNumber = 1;

        #region IDisposable support

        private bool disposed = false;

        /// <summary>
        /// This protected virtual method handles the disposal process for resources associated with this object.
        /// It checks whether the object's state has been finalized by checking the 'disposed' flag.
        /// If 'disposing' is true, it disposes of the 'streamReader'.
        /// The method ensures that all resources are properly released before returning.
        /// </summary>
        /// <param name="disposing">
        /// A boolean indicating whether the object's finalizers or cleanup code is being called during disposal.
        /// </param>
        /// <remarks>
        /// If 'disposing' is false, this method does nothing as no further resource management is required.
        /// </remarks>
        /// <returns>
        /// This method does not return anything.
        /// </returns>
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
                streamReader.Dispose();

            disposed = true;
        }

        /// <summary>
        /// The Dispose method is called by the garbage collector or explicitly when an object is no longer needed.
        /// It releases unmanaged resources associated with the object and calls the base class's Dispose method to release managed resources.
        /// </summary>
        /// <param name="disposing">A boolean indicating whether the call comes from the Dispose method itself (true) or from a finalizer (false).</param>
        /// <remarks>
        /// Implement this method properly to ensure proper cleanup of resources.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }

    private class CsvFileWriter : IDisposable
    {
        public char FieldDelimiter { get; set; } = ',';

        public char QuoteChar { get; set; } = '"';

        public bool WrapFields { get; set; } = false;

        public CsvFileWriter(string filePath)
        {
            streamWriter = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// This method writes an array of strings to a stream with specified field wrapping and delimiter settings.
        /// <example>
        /// For example:
        /// <code>
        /// string[] data = { "Name", "Age", "City" };
        /// WriteLine(data);
        /// </code>
        /// would output formatted strings on the stream.
        /// </example>
        /// </summary>
        /// <param name="fields">An array containing the fields to be written.</param>
        /// <param name="wrapFields">A boolean indicating whether fields should be wrapped.</param>
        /// <param name="fieldDelimiter">A character used as the delimiter between fields.</param>
        /// <remarks>
        /// The method iterates over each field in the provided array, applying field wrapping based on the `wrapFields` parameter,
        /// escaping special characters within fields using the `EscapeField` function, and appending them to a `StringBuilder`.
        /// If `wrapFields` is true, it adds quotes around each field before escaping; otherwise, it simply escapes the field without quotes.
        /// After processing all fields, it formats the final string and writes it to the stream followed by flushing the stream.
        /// </remarks>
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
        /// This method escapes a given field by replacing all occurrences of the quote character with the escaped version containing both quotes.
        /// </summary>
        /// <param name="field">The field to be escaped.</param>
        /// <returns>The escaped field as a string.</returns>
        private string EscapeField(string field)
        {
            var quoteCharString = QuoteChar.ToString();
            return field.Replace(quoteCharString, quoteCharString + quoteCharString);
        }

        private readonly StreamWriter streamWriter;

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
        /// The Dispose method is called by the garbage collector or explicitly when an object is no longer needed.
        /// It releases unmanaged resources associated with the object and calls the base class's Dispose method to release managed resources.
        /// </summary>
        /// <param name="disposing">A boolean indicating whether the call comes from the Dispose method itself (true) or from a finalizer (false).</param>
        /// <remarks>
        /// Implement this method properly to ensure proper cleanup of resources.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }

    /// <summary>
    /// This method checks the properties of an alarm type node by traversing its super types.
    /// It filters out specific browse names from the list of child nodes based on certain conditions.
    /// </summary>
    /// <param name="alarmType">The node ID representing the alarm type.</param>
    /// <param name="propertyList">A list to store the filtered property names.</param>
    /// <remarks>
    /// The method retrieves the root node of the specified alarm type using InformationModel.Get().
    /// Then it iterates through each super type until it reaches an AlarmControllerType or LimitAlarmControllerType.
    /// During this traversal, it adds selected property names to the propertyList if they meet the criteria (not already present and are not 'LastEvent').
    /// Finally, it processes the children of the current node to add additional property names to the list unless they are already included or 'LastEvent'.
    /// </remarks>
    private static void CheckAlarmProperties(NodeId alarmType, List<string> propertyList)
    {
        IUANode myAlarm = InformationModel.Get(alarmType);
        IUAObjectType myAlarmSuperType = ((UAObjectType)myAlarm).SuperType;
        while (myAlarmSuperType != null)
        {
            if (myAlarmSuperType is AlarmControllerType || myAlarmSuperType is LimitAlarmControllerType)
            {
                propertyList.AddRange(myAlarmSuperType.Children.Where(
                    item => commonProperties.Contains(item.BrowseName) &&
                    item.BrowseName != "LastEvent" &&
                    !propertyList.Contains(item.BrowseName)).Select(item => item.BrowseName));
            }
            myAlarmSuperType = myAlarmSuperType.SuperType;
        }
        foreach (string itemBrowseName in myAlarm.Children.Select(x => x.BrowseName))
        {
            if (propertyList.Contains(itemBrowseName) || itemBrowseName == "LastEvent")
                continue;
            propertyList.Add(itemBrowseName);
        }
    }

    /// <summary>
    /// This method recursively collects all types under the specified parent type into the provided list.
    /// </summary>
    /// <param name="parentType">The parent type to start collecting from.</param>
    /// <param name="allControllerTypes">The list to store the collected types.</param>
    private void CollectRecursive(IUAObjectType parentType, List<IUAObjectType> allControllerTypes)
    {
        allControllerTypes.Add(parentType);
        foreach (var childType in parentType.Refs.GetObjectTypes(OpcUa.ReferenceTypes.HasSubtype, false))
            CollectRecursive(childType, allControllerTypes);
    }
}
