#region Using directives
using System;
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.Core;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using FilesystemBrowserHelper;
using FTOptix.OPCUAServer;
#endregion

class Location
{
	public Location(
			string locationVariableBrowseName,
			string locationVariableValue,
			string locationVariableDisplayName,
			string locationVariableDisplayNameValue)
	{
		LocationVariableBrowseName = locationVariableBrowseName;
		LocationVariableValue = locationVariableValue;
		LocationVariableDisplayName = locationVariableDisplayName;
		LocationVariableDisplayNameValue = locationVariableDisplayNameValue;
	}

	public string LocationVariableBrowseName { get; }
	public string LocationVariableValue { get; }
	public string LocationVariableDisplayName { get; }
	public string LocationVariableDisplayNameValue { get; }
}

class DeviceLocation : Location
{
	public DeviceLocation(
			string locationVariableBrowseName,
			string locationVariableValue,
			string locationVariableDisplayName,
			string locationVariableDisplayNameValue)
			: base(
				locationVariableBrowseName,
				locationVariableValue,
				locationVariableDisplayName,
				locationVariableDisplayNameValue)
	{
	}
}

class SystemLocation : Location
{
	public SystemLocation(
			string locationVariableBrowseName,
			string locationVariableValue,
			string locationVariableDisplayName,
			string locationVariableDisplayNameValue)
			: base(
				locationVariableBrowseName,
				locationVariableValue,
				locationVariableDisplayName,
				locationVariableDisplayNameValue)
	{
	}
}

public class FolderBarLogic : BaseNetLogic
{
	/// <summary>
	/// This method initializes and configures the FilesystemBrowserLogic based on the provided path and platform settings.
	/// It checks for required variables, validates their presence, and sets up the UI components for navigation and file access.
	/// </summary>
	/// <remarks>
	/// The method performs the following steps:
	/// 1. Checks if free navigation is supported on the current platform.
	/// 2. Retrieves the path variable and ensures it is not null.
	/// 3. Initializes the locations combo box and relative path textbox.
	/// 4. Validates access to full filesystem and network drives.
	/// 5. Sets up the resource URI helper and handles default paths if necessary.
	/// 6. Initializes the locations object and sets it as the resource URI helper's location.
	/// 7. Sets up event handlers for variable changes and user input.
	/// 8. Starts a periodic task to update devices.
	/// </remarks>
	public override void Start()
	{
		isFreeNavigationSupportedForCurrentPlatform = PlatformConfigurationHelper.IsFreeNavigationSupported();

		// Path variables
		pathVariable = LogicObject.GetVariable("Path");
		if (pathVariable == null)
			throw new CoreConfigurationException("Path variable not found in FilesystemBrowserLogic");

		// FolderBar variables
		locationsComboBox = Owner.Get<ComboBox>("Locations");
		if (locationsComboBox == null)
			throw new CoreConfigurationException("Locations combo box not found");

		relativePathTextBox = Owner.Get<TextBox>("RelativePath");
		if (relativePathTextBox == null)
			throw new CoreConfigurationException("RelativePath textbox not found");

		accessFullFilesystemVariable = Owner.Owner.GetVariable("AccessFullFilesystem");
		if (accessFullFilesystemVariable == null)
			throw new CoreConfigurationException("AccessFullFilesystem variable not found");

		if (accessFullFilesystemVariable.Value && !isFreeNavigationSupportedForCurrentPlatform)
			return;

		accessNetworkDrivesVariable = Owner.Owner.GetVariable("AccessNetworkDrives");
		if (accessNetworkDrivesVariable == null)
			throw new CoreConfigurationException("AccessNetworkDrives variable not found");

		// In case of invalid path the fall back is %PROJECTDIR%
		var startFolderPathResourceUri = new ResourceUri(pathVariable.Value);

		resourceUriHelper = new ResourceUriHelper(LogicObject.NodeId.NamespaceIndex);
		if (!resourceUriHelper.IsFolderPathAllowed(startFolderPathResourceUri, accessFullFilesystemVariable.Value, accessNetworkDrivesVariable.Value))
		{
			startFolderPathResourceUri = resourceUriHelper.GetDefaultResourceUri();
			pathVariable.Value = startFolderPathResourceUri;
		}

		locationsObject = Owner.Owner.GetObject("Locations");
		if (locationsObject == null)
			throw new CoreConfigurationException("Locations object not found");

		InitializeLocationsObject();
		resourceUriHelper.LocationsObject = locationsObject;

		InitializeComboBoxAndTextBox(startFolderPathResourceUri);

		pathVariable.VariableChange += PathVariable_VariableChange;
		locationsComboBox.SelectedValueVariable.VariableChange += SelectedValueComboBox_VariableChange;
		relativePathTextBox.OnUserTextChanged += RelativePathTextBox_UserTextChanged;

		periodicTaskDeviceUpdater = new PeriodicTask(UpdateDevices, deviceUpdaterPeriod, LogicObject);
		periodicTaskDeviceUpdater.Start();
	}

	/// <summary>
	/// This method unsubscribes from event handlers and cancels a periodic task.
	/// </summary>
	/// <remarks>
	/// This method is part of the override implementation for the <see cref="Stop"/> method.
	/// </remarks>
	public override void Stop()
	{
		pathVariable.VariableChange -= PathVariable_VariableChange;
		locationsComboBox.SelectedValueVariable.VariableChange -= SelectedValueComboBox_VariableChange;
		relativePathTextBox.OnUserTextChanged -= RelativePathTextBox_UserTextChanged;
		periodicTaskDeviceUpdater.Cancel();
	}

	/// <summary>
	/// This method handles the variable change event for a ComboBox, updating the text box value and setting the value of a resource URI.
	/// </summary>
	/// <param name="sender">The source of the event.</param>
	/// <param name="e">The VariableChangeEventArgs instance containing the event data.</param>
	/// <remarks>
	/// The method clears the text box value, then sets the <see cref="pathVariable"/> to a new <see cref="ResourceUri"/> based on the new value from the event.
	/// </remarks>
	private void SelectedValueComboBox_VariableChange(object sender, VariableChangeEventArgs e)
	{
		// Clear RelativePath Textbox and update the current path (a new browse is made)
		SetTextBoxValue(string.Empty);
		pathVariable.Value = new ResourceUri(e.NewValue);
	}

	/// <summary>
	/// This method handles the text change event for a relative path textbox, validating and processing the input.
	/// It checks for invalid characters like '..', ensures the input is not a full path, and validates the resulting path.
	/// </summary>
	/// <param name="sender">The source of the event.</param>
	/// <param name="e">The UserTextChangedEvent instance containing the new text value.</param>
	/// <remarks>
	/// The method ensures that the input does not contain the '..' character, which would indicate a folder traversal.
	/// It also checks if the input is a full path (starting with a slash), and if not, appends a slash.
	/// Finally, it validates the resulting path using the ResourceUri helper and logs an error if the path is invalid.
	/// </remarks>
	private void RelativePathTextBox_UserTextChanged(object sender, UserTextChangedEvent e)
	{
		var updatedRelativePathString = e.NewText.Text;

		if (updatedRelativePathString.Contains(".."))
		{
			Log.Error("FolderBarLogic", $"Input is incorrect: '..' is not supported");
			SetTextBoxValue(lastTexboxValidText);
			return;
		}

		if (Path.IsPathRooted(updatedRelativePathString))
		{
			Log.Error("FolderBarLogic", "Input is incorrect: cannot insert a full path");
			SetTextBoxValue(lastTexboxValidText);
			return;
		}

		// Update pathVariable value with the text inserted into the textbox
		string ftoptixstudioBasePath = (string)locationsComboBox.SelectedValue;

		bool endsWithSlash = ftoptixstudioBasePath.EndsWith("/") || ftoptixstudioBasePath.EndsWith("\\");
		if (!endsWithSlash && !string.IsNullOrEmpty(updatedRelativePathString))
			ftoptixstudioBasePath += "/";

		string updatedPathResourceUriString = ftoptixstudioBasePath + updatedRelativePathString;
		ResourceUri updatedResourceUri = new ResourceUri(updatedPathResourceUriString);
		if (!resourceUriHelper.IsResourceUriValid(updatedResourceUri))
		{
			Log.Error("FolderBarLogic", $"Input is incorrect: folder path {updatedPathResourceUriString} does not exist");
			SetTextBoxValue(lastTexboxValidText);
			return;
		}

		pathVariable.Value = ftoptixstudioBasePath + updatedRelativePathString;
	}

	/// <summary>
	/// This method updates the text box value based on a new resource URI.
	/// It retrieves the relative path from the provided URI and sets the text box value.
	/// </summary>
	/// <param name="sender">The source of the event.</param>
	/// <param name="e">The VariableChangeEventArgs instance containing the event data.</param>
	/// <remarks>
	/// The method attempts to set the text box value using the <see cref="resourceUriHelper.GetRelativePathToLocationFromResourceUri"/> method.
	/// If an exception occurs (e.g., the URI is not found), an error is logged and the method returns immediately.
	/// </remarks>
	private void PathVariable_VariableChange(object sender, VariableChangeEventArgs e)
	{
		var updatedPathResourceUri = new ResourceUri(e.NewValue);

		// Update the textbox with the relative path to the current selected location
		try
		{
			SetTextBoxValue(resourceUriHelper.GetRelativePathToLocationFromResourceUri(updatedPathResourceUri));
		}
		catch (Exception exception)
		{
			Log.Error("FolderBarLogic", $"Unable to set value on textbox. Path '{e.NewValue.Value}' not found: {exception.Message}");
			return;
		}
	}

	/// <summary>
	/// This method initializes the locations object by adding namespace prefixes to standard locations,
	/// initializing USB/SD devices, and optionally initializing system drives if the accessFullFilesystemVariable is true.
	/// It also adds the locations.
	/// </summary>
	/// <remarks>
	/// The method assumes the existence of the <see cref="AddNamespacePrefixToStandardLocations"/> method,
	/// <see cref="InitializeRemovableMediaDevices"/> method, <see cref="InitializeSystemDrives"/> method, and
	/// <see cref="AddLocations"/> method.
	/// </remarks>
	private void InitializeLocationsObject()
	{
		// Set the namespace prefix to %APPLICATIONDIR% and %PROJECTDIR%
		AddNamespacePrefixToStandardLocations();

		// Detect connected removable media devices
		InitializeRemovableMediaDevices();

		// Detect fixed drives
		if (accessFullFilesystemVariable.Value)
		{
			if (Environment.OSVersion.Platform != PlatformID.Unix)
				InitializeSystemDrives();
		}

		AddLocations();
	}

	/// <summary>
	/// This method iterates through each location in the provided collection and updates their value by adding a namespace prefix using the specified helper method.
	/// </summary>
	/// <param name="locationsObject">A collection of IUAVariable objects to update.</param>
	private void AddNamespacePrefixToStandardLocations()
	{
		foreach (IUAVariable location in locationsObject.Children)
			location.Value = resourceUriHelper.AddNamespacePrefixToFTOptixRuntimeFolder((string)location.Value.Value);
	}

	/// <summary>
	/// This method initializes system drives by retrieving the list of drives using DriveInfo,
	/// checking their types, and adding them to the locations collection.
	/// </summary>
	private void InitializeSystemDrives()
	{
		if (!accessFullFilesystemVariable.Value)
			return;

		DriveInfo[] drives;
		try
		{
			drives = DriveInfo.GetDrives();
		}
		catch (Exception exception)
		{
			Log.Error("FolderBarLogic", $"Unable to get the list of system drives: {exception.Message}");
			return;
		}

		currentlyConnectedSystemDrives = new List<string>();
		foreach (var drive in drives)
		{
			// Skip USB/SD removable devices
			if (detectedRemovableMediaDrives.Contains(drive.Name))
				continue;

			string driveName = drive.RootDirectory.FullName;
			var systemDriveNode = locationsObject.GetVariable(driveName);
			bool systemdriveNodeAlreadyExists = systemDriveNode != null;

			bool isFixedDrive = drive.DriveType == DriveType.Fixed;
			bool isNetworkDrive = drive.DriveType == DriveType.Network;

			// A network drive is valid only if AccessFullFilesystem and AccessNetworkDrives are true
			if (isNetworkDrive && !accessNetworkDrivesVariable.Value)
				continue;

			// Check if network drives are reachable
			if (isNetworkDrive && !PlatformConfigurationHelper.IsWindowsNetworkDriveAccessible(driveName))
				continue;

			// Drive is reachable
			if (isFixedDrive || isNetworkDrive)
			{
				// Assumption: in the small periodic task update period (e.g. 5secs) there is collision of system drive names.
				// For example in that period we assume that this case is not possible:
				// D:/ is removed and then a different disk is inserted with the same drive name D:/
				currentlyConnectedSystemDrives.Add(driveName);
				if (systemdriveNodeAlreadyExists)
					continue;

				locations.Add(new SystemLocation(driveName,
					ResourceUri.FromAbsoluteFilePath(driveName),
					$"WindowsDrive_{driveName}",
					driveName));
			}
		}
	}

	/// <summary>
	/// This method updates the list of system drives by checking for any changes in the connected drives.
	/// It compares the current list of drives with the previous list and logs any changes.
	/// If a drive is disconnected, it removes the corresponding node from the locations object.
	/// If a drive is connected, it logs the connection.
	/// </summary>
	private void UpdateSystemDrives()
	{
		if (!accessFullFilesystemVariable.Value)
			return;

		// If a disk (external hard drive or network drive) is removed, it is not retrieved by DriveInfo.GetDrives()
		var initialConnectedSystemDrives = currentlyConnectedSystemDrives;
		InitializeSystemDrives();

		if (initialConnectedSystemDrives.Count == currentlyConnectedSystemDrives.Count)
			return;

		if (initialConnectedSystemDrives.Count < currentlyConnectedSystemDrives.Count)
		{
			var connectedDrives = currentlyConnectedSystemDrives.Except(initialConnectedSystemDrives).ToList();
			foreach (var drive in connectedDrives)
				Log.Info("FolderBarLogic", $"Drive {drive} has been connected");
		}

		// Check if a system drive in locations is no longer attached
		if (initialConnectedSystemDrives.Count > currentlyConnectedSystemDrives.Count)
		{
			var disconnectedDrives = initialConnectedSystemDrives.Except(currentlyConnectedSystemDrives).ToList();
			foreach (var drive in disconnectedDrives)
			{
				var driveNode = locationsObject.Find(drive);
				if (driveNode != null)
				{
					Log.Info("FolderBarLogic", $"Drive {drive} has been disconnected");

					// Fall back to %PROJECTDIR% if the currently selected drive is disconnected
					ResourceUri selectedComboBoxValue = new ResourceUri((string)locationsComboBox.SelectedValue);
					var currentlySelectedDriveRoot = PlatformConfigurationHelper.GetWindowsDrivePathRoot(selectedComboBoxValue);
					if (currentlySelectedDriveRoot == drive)
						locationsComboBox.SelectedValue = resourceUriHelper.GetDefaultResourceUriAsString();

					locationsObject.Remove(driveNode);
				}
			}
		}
	}

	/// <summary>
	/// This method initializes the Linux root folder by retrieving the base browse name and path from configuration,
	/// then creating a SystemLocation object with the retrieved values.
	/// </summary>
	private void InitializeLinuxRootFolder()
	{
		// Non supported platforms was already filtered out
		// Only Debian is supported
		string linuxRootBrowseName = PlatformConfigurationHelper.GetGenericLinuxBaseBrowseNameFolder();
		string linuxRootResourceUri = ResourceUri.FromAbsoluteFilePath(PlatformConfigurationHelper.GetGenericLinuxBaseFolderPath());

		locations.Add(new SystemLocation(linuxRootBrowseName,
			linuxRootResourceUri,
			"LinuxRoot",
			linuxRootBrowseName));
	}

    /// <summary>
    /// This method initializes a device by scanning for connected flash memory device (USB or SD card) up to a maximum limit.
    /// It iterates through a range of device numbers, checks if a device is present, and adds it to the list of detected storage drives.
    /// If the platform is not Unix-based, it also adds the root path of the device to the list of detected storage drives.
    /// The method then adds the device location information to the locations collection.
    /// </summary>
    /// <param name="deviceType">USB or SD</param>
    /// <remarks>
    /// - The method uses a loop to iterate from 1 to maxConnectedDevices.
    /// - It checks if a device is present using the locationsObject and ResourceUri.
    /// - If the platform is not Unix-based, it adds the root path of the device to the detectedRemovableMediaDrives list.
    /// - If a device node is found, it continues to the next iteration.
    /// - The method updates the currentlyConnectedRemovableMediaDevices variable with the count of connected devices.
    /// </remarks>
    private uint InitializeRemovableMediaDevice(string deviceType)
	{
        uint connectedDevices = 0;
        for (uint i = 1; i <= maxConnectedDevices; ++i)
        {
            var deviceLocationBrowseName = $"{deviceType}{i}";
            var deviceResourceUri = new ResourceUri($"%{deviceLocationBrowseName}%");

            var deviceNode = locationsObject.GetVariable(deviceLocationBrowseName);

            if (!resourceUriHelper.IsResourceUriValid(deviceResourceUri))
                break;

            connectedDevices++;

            if (Environment.OSVersion.Platform != PlatformID.Unix)
                detectedRemovableMediaDrives.Add(Path.GetPathRoot(deviceResourceUri.Uri));

            // location is valid and it already exists
            if (deviceNode != null)
                continue;

            string deviceName;
            if (Environment.OSVersion.Platform != PlatformID.Unix)
                deviceName = $"{deviceType} {i} ({Path.GetPathRoot(deviceResourceUri.Uri)})";
            else
                deviceName = $"{deviceType} {i}";

            locations.Add(new DeviceLocation($"{deviceType}{i}",
                deviceResourceUri,
                "ComboBoxFileSelectorUSBDisplayName",
                deviceName));
        }

		return connectedDevices;
    }

    /// <summary>
    /// This method initializes all USB and SD devices and saves the total number of devices
    /// </summary>
    /// <remarks>
    /// - The method updates the currentlyConnectedRemovableMediaDevices variable with the count of connected USB/SD devices.
    /// </remarks>
    private void InitializeRemovableMediaDevices()
	{
		uint connectedRemovableMediaDevices = 0;
		detectedRemovableMediaDrives.Clear();

		connectedRemovableMediaDevices = InitializeRemovableMediaDevice("USB");
        connectedRemovableMediaDevices += InitializeRemovableMediaDevice("SD");

		currentlyConnectedRemovableMediaDevices = connectedRemovableMediaDevices;
    }

    /// <summary>
    /// This method updates the list of connected devices by checking for changes in the number of connected devices.
    /// </summary>
    /// <param name="deviceType">USB or SD</param>
    /// <remarks>
    /// It compares the current count with the previous count and logs any changes.
    /// If a device is disconnected, it removes the corresponding node from the locations object.
    /// If a device is connected, it logs the connection.
    /// </remarks>
	private void RemoveDisconnectedDevices(string deviceType)
	{
		// Remove invalid (e.g. disconnected) USB/SD devices
		for (uint i = 1; i <= maxConnectedDevices; ++i)
		{
			var deviceLocationBrowseName = $"{deviceType}{i}";
			var deviceNode = locationsObject.GetVariable(deviceLocationBrowseName);
			if (deviceNode != null)
			{
				var deviceResourceUri = new ResourceUri($"%{deviceLocationBrowseName}%");
				// USB/SD<i> location still exists but it is invalid
				if (!resourceUriHelper.IsResourceUriValid(deviceResourceUri))
					locationsObject.Remove(deviceNode);
			}
		}
	}

    /// <summary>
    /// This method updates the list of connected USB devices by checking for changes in the number of connected devices.
    /// It compares the current count with the previous count and logs any changes.
    /// If a device is disconnected, it removes the corresponding node from the locations object.
    /// If a device is connected, it logs the connection.
    /// </summary>
    private void UpdateDevices()
	{
		uint initialConnectedRemovableMediaDevices = currentlyConnectedRemovableMediaDevices;
		InitializeRemovableMediaDevices();

		if (Environment.OSVersion.Platform == PlatformID.Win32NT)
			UpdateSystemDrives();

		AddLocations();

		if (currentlyConnectedRemovableMediaDevices == initialConnectedRemovableMediaDevices)
			return;

		bool deviceConnected = currentlyConnectedRemovableMediaDevices > initialConnectedRemovableMediaDevices;
		string deviceStatus = (deviceConnected) ? "connected" : "disconnected";
		Log.Info("FolderBarLogic", $"Mass storage device has been {deviceStatus}");

		// If a USB stick or SD card device or SD card is currently selected and device changes it is now invalid.
		// 1. Disconnection of a USB/SD
		// E.g. USB1 and USB2 are detected at start. Then USB1 is disconnected. USB2 is promoted by FTOptixStudio as USB1:
		// if we are browsing USB1 the current view and path values are outdated (because USB1 is a different device now);
		// if we are browsing USB2 the current view and path values are outdated (because USB2 does not exist anymore)
		// 2. Connection of a USB/SD
		// E.g. USB1 is connected at start. USB2 is connected later. It is not guaranteed that the enumeration preserves the order.
		// It depends on the physical port: if USB2 physical port is enumerated before USB1 physical port, then values are invalid.
		// We must fall back to %PROJECTDIR%
		ResourceUri selectedComboBoxValue = new ResourceUri((string)locationsComboBox.SelectedValue);
		if (selectedComboBoxValue.UriType == UriType.USBRelative || selectedComboBoxValue.UriType == UriType.SDRelative)
			locationsComboBox.SelectedValue = resourceUriHelper.GetDefaultResourceUriAsString();

		// Remove invalid (e.g. disconnected) USB/SD devices
		RemoveDisconnectedDevices("USB");
		RemoveDisconnectedDevices("SD");
	}

	/// <summary>
	/// Adds all location objects from the <see cref="locations"/> collection to the system and USB/SD drives.
	/// </summary>
	/// <remarks>
	/// This method iterates through two types of location objects: <see cref="SystemLocation"/> and <see cref="DeviceLocation"/>.
	/// It calls the <see cref="AddLocation"/> method for each of these objects.
	/// After processing, the <see cref="locations"/> collection is cleared.
	/// </remarks>
	private void AddLocations()
	{
		var systemDrives = locations.OfType<SystemLocation>();
		foreach (var location in systemDrives)
			AddLocation(location);

		var removableMediaDrives = locations.OfType<DeviceLocation>();
		foreach (var location in removableMediaDrives)
			AddLocation(location);

		locations.Clear();
	}

	/// <summary>
	/// This method adds a location to the locationsObject using the provided location data.
	/// It sets the location variable's value, applies a locale-specific display name, and adds the variable to the collection.
	/// </summary>
	/// <param name="location">The location object containing the data to be added.</param>
	/// <remarks>
	/// The method uses the current user's locale ID to dynamically set the display name of the location variable.
	/// If no locale ID is found, an error is logged.
	/// </remarks>
	private void AddLocation(Location location)
	{
		var locationVariable = InformationModel.MakeVariable(location.LocationVariableBrowseName, FTOptix.Core.DataTypes.ResourceUri);
		locationVariable.Value = location.LocationVariableValue;

		var localeId = Session.User.LocaleId;
		if (String.IsNullOrEmpty(localeId))
			Log.Error("FolderBarLogic", "No locale found for the current user");

		locationVariable.DisplayName = new LocalizedText(location.LocationVariableDisplayName, location.LocationVariableDisplayNameValue, localeId);

		locationsObject.Add(locationVariable);
	}

	/// <summary>
	/// This method initializes a ComboBox and a TextBox by setting their values based on a resource URI.
	/// </summary>
	/// <param name="startFolderPathResourceUri">The resource URI from which to derive the base location path and relative path.</param>
	private void InitializeComboBoxAndTextBox(ResourceUri startFolderPathResourceUri)
	{
		locationsComboBox.SelectedValue = resourceUriHelper.GetBaseLocationPathFromLocationsObject(startFolderPathResourceUri);
		SetTextBoxValue(resourceUriHelper.GetRelativePathToLocationFromResourceUri(startFolderPathResourceUri));
	}

	/// <summary>
	/// This method sets the text value of the relativePathTextBox to the provided text.
	/// </summary>
	private void SetTextBoxValue(string text)
	{
		lastTexboxValidText = text;
		relativePathTextBox.Text = lastTexboxValidText;
	}

	private IUAVariable pathVariable;

	// FolderBar variables
	private TextBox relativePathTextBox;
	private ComboBox locationsComboBox;
	private IUAObject locationsObject;

	private IUAVariable accessFullFilesystemVariable;
	private IUAVariable accessNetworkDrivesVariable;
	private bool isFreeNavigationSupportedForCurrentPlatform;

	private PeriodicTask periodicTaskDeviceUpdater;
	private readonly int deviceUpdaterPeriod = 5000;

	private readonly uint maxConnectedDevices = 5;
	private uint currentlyConnectedRemovableMediaDevices = 0;
	private List<string> currentlyConnectedSystemDrives;

	private string lastTexboxValidText = "";
	private ResourceUriHelper resourceUriHelper;

	private readonly HashSet<string> detectedRemovableMediaDrives = new HashSet<string>();
	private readonly List<Location> locations = new List<Location>();
}
