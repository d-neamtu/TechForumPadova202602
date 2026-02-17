#region Using directives
using System;
using System.IO;
using System.Text;
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.Store;
using FTOptix.Core;
using FTOptix.HMIProject;
#endregion

public class DataLoggerExporter : BaseNetLogic
{
    /// <summary>
    /// This method exports data from a data logger to a CSV file.
    /// It validates time slice, retrieves CSV path, delimiter, wraps fields, creates a table object, stores it, constructs a query, executes the query, writes the results to a CSV file, logs success or failure.
    /// </summary>
    /// <remarks>
    /// Example usage:
    /// <code>
    /// Export();
    /// </code>
    /// Results in logging information about successful data export to a specified CSV file.
    /// </remarks>
    [ExportMethod]
    public void Export()
    {
        try
        {
            ValidateTimeSlice();

            var csvPath = GetCSVFilePath();
            if (string.IsNullOrEmpty(csvPath))
                throw new Exception("No CSV file chosen, please fill the CSVPath variable");

            char? fieldDelimiter = GetFieldDelimiter();
            bool wrapFields = GetWrapFields();
            var tableObject = GetTable();
            var storeObject = GetStoreObject(tableObject);
            var selectQuery = CreateQuery(tableObject);

            storeObject.Query(selectQuery, out string[] header, out object[,] resultSet);

            if (header == null || resultSet == null)
                throw new Exception("Unable to execute SQL query, malformed result");

            var rowCount = resultSet.GetLength(0);
            var columnCount = resultSet.GetLength(1);

            using (var csvWriter = new CSVFileWriter(csvPath) { FieldDelimiter = fieldDelimiter.Value, WrapFields = wrapFields })
            {
                csvWriter.WriteLine(header);
                WriteTableContent(resultSet, rowCount, columnCount, csvWriter);
            }

            Log.Info("DataLoggerExporter", "The Data logger " + tableObject.BrowseName + " has been succesfully exported to " + csvPath);
        }
        catch (Exception ex)
        {
            Log.Error("DataLoggerExporter", "Unable to export data logger: " + ex.Message);
        }

    }

    /// <summary>
    /// This method writes the content of a two-dimensional object array to a CSV file writer.
    /// Each row in the array corresponds to a line in the CSV file, with each element in the row represented by its corresponding cell index.
    /// If an element in the array is null, it is replaced with "NULL" before writing.
    /// </summary>
    /// <param name="resultSet">The two-dimensional object array containing table data.</param>
    /// <param name="rowCount">The number of rows in the table.</param>
    /// <param name="columnCount">The number of columns in the table.</param>
    /// <param name="csvWriter">The CSV file writer instance used to write the data.</param>
    private void WriteTableContent(object[,] resultSet, int rowCount, int columnCount, CSVFileWriter csvWriter)
    {
        for (var r = 0; r < rowCount; ++r)
        {
            var currentRow = new string[columnCount];

            for (var c = 0; c < columnCount; ++c)
                currentRow[c] = resultSet[r, c]?.ToString() ?? "NULL";

            csvWriter.WriteLine(currentRow);
        }
    }

    /// <summary>
    /// This method retrieves a table from the information model based on its nodeId.
    /// It checks for the existence and validity of the table before returning it.
    /// If the table does not exist or is invalid, it throws an exception.
    /// </summary>
    /// <returns>A table object representing the retrieved table.</returns>
    private Table GetTable()
    {
        var tableVariable = LogicObject.GetVariable("Table");

        if (tableVariable == null)
            throw new Exception("Table variable not found");

        NodeId tableNodeId = tableVariable.Value;
        if (tableNodeId == null || tableNodeId == NodeId.Empty)
            throw new Exception("Table variable is empty");

        var tableNode = InformationModel.Get<Table>(tableNodeId);
        if (tableNode == null)
            throw new Exception("The specified table node is not an instance of Store Table type");

        return tableNode;
    }

    /// <summary>
    /// This method retrieves the Store object associated with the given Table node's owner.
    /// <example>
    /// For example:
    /// <code>
    /// Store store = GetStoreObject(tableNode);
    /// </code>
    /// results in <c>store</c> containing the Store object for the specified Table node.
    /// </example>
    /// </summary>
    /// <param name="tableNode">The Table node whose owner's Store object is needed.</param>
    /// <returns>
    /// The Store object corresponding to the provided Table node's owner.
    /// </returns>
    /// <remarks>
    /// Assumes that 'tableNode' has an 'Owner' property which itself is an instance of 'Table'.
    /// </remarks>
    private Store GetStoreObject(Table tableNode)
    {
        return tableNode.Owner.Owner as Store;
    }

    /// <summary>
    /// This method retrieves the CSV file path from the logic object's variable named "CSVPath".
    /// If the variable is not found, it throws an exception.
    /// The returned URI is constructed using the resource uri factory with the provided CSVPath value.
    /// </summary>
    /// <returns>The Uri representing the CSV file path as a string.</returns>
    private string GetCSVFilePath()
    {
        var csvPathVariable = LogicObject.GetVariable("CSVPath");
        if (csvPathVariable == null)
            throw new Exception("CSVPath variable not found");

        return new ResourceUri(csvPathVariable.Value).Uri;
    }

    /// <summary>
    /// This method retrieves the field delimiter from the logic object's variable named "FieldDelimiter".
    /// If the variable is not found or its value is invalid, it throws an exception.
    /// It then checks for a single character delimiter with length one, ensuring proper configuration.
    /// The method converts the delimiter into a valid char type before returning it.
    /// </summary>
    /// <returns>A char representing the correct field delimiter configuration.</returns>
    private char GetFieldDelimiter()
    {
        var separatorVariable = LogicObject.GetVariable("FieldDelimiter");
        if (separatorVariable == null)
            throw new Exception("FieldDelimiter variable not found");

        string separator = separatorVariable.Value;

        if (separator == string.Empty)
            throw new Exception("FieldDelimiter variable is empty");

        if (separator.Length != 1)
            throw new Exception("Wrong FieldDelimiter configuration. Please insert a single character");

        if (!char.TryParse(separator, out char result))
            throw new Exception("Wrong FieldDelimiter configuration. Please insert a char");

        return result;
    }

    /// <summary>
    /// Retrieves the value of the "WrapFields" variable from the logic object.
    /// If the variable is not found, an exception is thrown.
    /// </summary>
    /// <returns>A boolean indicating whether the "WrapFields" variable was successfully retrieved.</returns>
    /// <remarks>
    /// The method retrieves the value of the "WrapFields" variable from the logic object and checks if it exists before returning its value as a boolean.
    /// </remarks>
    private bool GetWrapFields()
    {
        var wrapFieldsVariable = LogicObject.GetVariable("WrapFields");
        if (wrapFieldsVariable == null)
            throw new Exception("WrapFields variable not found");

        return wrapFieldsVariable.Value;
    }

    /// <summary>
    /// This method retrieves the query filter from the logic object's variable named 'Query'.
    /// If the 'Query' variable is not found or its value is either empty or invalid, it throws an exception.
    /// The method returns the query as a string.
    /// </summary>
    /// <returns>A string containing the query filter.</returns>
    private string GetQueryFilter()
    {
        var queryVariable = LogicObject.GetVariable("Query");
        if (queryVariable == null)
            throw new Exception("Query variable not found");

        string query = queryVariable.Value;

        if (string.IsNullOrEmpty(query))
            throw new Exception("Query variable is empty or not valid");

        return query;
    }

    /// <summary>
    /// Generates a SQL SELECT query based on the provided table information.
    /// </summary>
    /// <param name="table">The table object containing browse name for the query.</param>
    /// <returns>A string representing the SQL query with columns and filter condition.</returns>
    private string CreateQuery(Table table)
    {
        var queryColumns = GetQueryColumns(table);
        var queryFilter = GetQueryFilter();

        return $"SELECT {queryColumns} FROM \"{table.BrowseName}\" WHERE {queryFilter}";
    }

    /// <summary>
    /// This method retrieves the column names from a given table.
    /// The method iterates through each column's browse name and constructs a comma-separated list of column names,
    /// excluding the "Id" column. If there are additional columns after the current one that are not named "Id",
    /// they are also included in the final column list.
    /// </summary>
    /// <param name="table">The table object containing the columns.</param>
    /// <returns>A string representing the comma-separated list of non-"Id" column names.</returns>
    private string GetQueryColumns(Table table)
    {
        var tableColumns = table.Columns;
        string columns = "";

        for (int i = 0; i < tableColumns.Count; i++)
        {
            var columnName = tableColumns[i].BrowseName;
            if (columnName == "Id")
                continue;

            columns += $"\"{columnName}\"";

            if (i != tableColumns.Count - 1 && tableColumns[i + 1].BrowseName != "Id")
                columns += ", ";
        }

        return columns;
    }

    /// <summary>
    /// Validates the time slice by checking for null or empty values of the From and To variables.
    /// If either variable is null or its value is null, an exception is thrown with an appropriate error message.
    /// Additionally, it ensures that the From value is greater than the minimum supported date.
    /// It also checks if the To property has been set and throws an exception if it hasn't.
    /// Furthermore, it verifies whether the To value is earlier than the From value, throwing an exception in this case.
    /// </summary>
    /// <remarks>
    /// Throws exceptions when required conditions are not met during validation.
    /// </remarks>
    private void ValidateTimeSlice()
    {
        var fromVariable = LogicObject.GetVariable("From");
        if (fromVariable == null || fromVariable.Value == null)
            throw new Exception("From variable is empty or missing");
        var toVariable = LogicObject.GetVariable("To");
        if (toVariable == null || toVariable.Value == null)
            throw new Exception("To variable is empty or missing");

        DateTime fromValue = fromVariable.Value;
        DateTime toValue = toVariable.Value;

        if (fromValue <= minumumSupportedDate)
            Log.Warning("DataLoggerExporter", "The From value is lower than the minumum supported date");

        if (toValue == minimumOpcUaDate)
            throw new Exception("The To property is not set");

        if (toValue < fromValue)
            throw new Exception("Not a valid time slice. The date entered in the From property is later than the date entered in the To");
    }

    // OPC UA DateTime minimum value is "1601-01-01T00:00:00.000Z". It indicates that the value was not set.
    private readonly DateTime minimumOpcUaDate = new DateTime(1601, 01, 01, 0, 0, 0, 0, DateTimeKind.Utc);

    // An intersection of the minumum supported Timestamps by Sqlserver (1/1/1753), MYSQL (1/1/1970) and an Embedded database
    private readonly DateTime minumumSupportedDate = new DateTime(1970, 01, 01, 0, 0, 0, 0, DateTimeKind.Utc);

    #region CSVFileWriter
    private class CSVFileWriter : IDisposable
    {
        public char FieldDelimiter { get; set; } = ',';

        public char QuoteChar { get; set; } = '"';

        public bool WrapFields { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the CSVFileWriter class with the specified file path.
        /// <example>
        /// For example:
        /// <code>
        /// CSVFileWriter writer = new CSVFileWriter("output.csv");
        /// </code>
        /// creates an object that can be used to write data to "output.csv".
        /// </example>
        /// </summary>
        /// <param name="filePath">The path of the output file.</param>
        /// <remarks>
        /// The constructor initializes a new StreamWriter for writing UTF-8 encoded text to the specified file.
        /// </remarks>
        public CSVFileWriter(string filePath)
        {
            streamWriter = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Initializes a new instance of the CSVFileWriter class with specified file path and encoding.
        /// <example>
        /// For example:
        /// <code>
        /// CSVFileWriter writer = new CSVFileWriter("output.csv", Encoding.UTF8);
        /// </code>
        /// The constructor creates a new StreamWriter object for writing to the specified file path using the provided encoding.
        /// </example>
        /// </summary>
        /// <param name="filePath">The file path where the CSV data will be written.</param>
        /// <param name="encoding">The character encoding used for writing the CSV data.</param>
        /// <remarks></remarks>
        /// <returns>
        /// A StreamWriter object that represents the CSV file writer.
        /// </returns>
        public CSVFileWriter(string filePath, System.Text.Encoding encoding)
        {
            streamWriter = new StreamWriter(filePath, false, encoding);
        }

        /// <summary>
        /// Initializes a new instance of the CSVFileWriter class with the specified StreamWriter.
        /// <example>
        /// For example:
        /// <code>
        /// var csvWriter = new CSVFileWriter(new StreamWriter("output.csv"));
        /// </code>
        /// creates an instance of CSVFileWriter with the output file named "output.csv".
        /// </example>
        /// </summary>
        /// <param name="streamWriter">The StreamWriter object used for writing CSV data.</param>
        /// <remarks>
        /// The constructor initializes the underlying stream writer, ready for writing CSV formatted data.
        /// </remarks>
        public CSVFileWriter(StreamWriter streamWriter)
        {
            this.streamWriter = streamWriter;
        }

        /// <summary>
        /// This method writes an array of strings to a stream with specified formatting options.
        /// <example>
        /// For example:
        /// <code>
        /// string[] data = { "Name", "John Doe" };
        /// WriteToStream(data);
        /// </code>
        /// will write "Name' John Doe'" to the stream.
        /// </example>
        /// </summary>
        /// <param name="fields">Array of strings to be written.</param>
        /// <param name="wrapFields">If true, wraps field values with quotes and escape characters.</param>
        /// <param name="quoteChar">Character used to wrap quoted fields.</param>
        /// <param name="fieldDelimiter">Character used to separate field values within the same line.</param>
        /// <param name="streamWriter">The stream writer object to which the output will be appended.</param>
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
        /// This method escapes a given field by replacing the enclosing quotation marks with their double form.
        /// <example>
        /// For example:
        /// <code>
        /// string escapedField = EscapeField("This is a \"test\" field.");
        /// </code>
        /// results in <c>escapedField</c>'s having the value "This is a \\"test\\" field.".
        /// </example>
        /// </summary>
        /// <param name="field">The field to be escaped.</param>
        /// <returns>
        /// A string containing the original field with its surrounding quotation marks replaced by their double forms.
        /// </returns>
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
        /// This method calls the finalizer (Dispose(bool disposing)) for proper cleanup.
        /// <example>
        /// For example:
        /// <code>
        /// object instance = new Object();
        /// instance.Dispose();
        /// </code>
        /// The finalize method will be called on the instance when it is no longer referenced.
        /// </example>
        /// </summary>
        /// <param name="disposing">
        /// A boolean indicating if the call comes from a finalizer or other context.
        /// </param>
        /// <remarks>
        /// The finalizer is automatically invoked by the garbage collector after an object has been deallocated.
        /// It's important to ensure that resources are properly cleaned up before calling this method.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
    #endregion
}
