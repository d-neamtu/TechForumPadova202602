#region Using directives
using System;
using FTOptix.HMIProject;
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.Core;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using FilesystemBrowserHelper;
#endregion

public class FilesystemBrowserLogic : BaseNetLogic
{
	/// <summary>
	/// This method initializes various variables and performs validation based on configuration settings,
	/// then proceeds to browse the file system according to the provided path and filter settings.
	/// </summary>
	/// <remarks>
	/// The method checks if the path variable is valid and falls back to a default path if necessary
	/// if the system does not support full filesystem access.
	/// </remarks>
	public override void Start()
	{
		pathVariable = LogicObject.GetVariable("Path");
		if (pathVariable == null)
			throw new CoreConfigurationException("Path variable not found in FilesystemBrowserLogic");

		filterVariable = Owner.GetVariable("ExtensionFilter");
		if (filterVariable == null)
			throw new CoreConfigurationException("ExtensionFilter variable not found in FilesystemBrowserLogic");

		accessFullFilesystemVariable = Owner.GetVariable("AccessFullFilesystem");
		if (accessFullFilesystemVariable == null)
			throw new CoreConfigurationException("AccessFullFilesystem variable not found");

		if (accessFullFilesystemVariable.Value && !PlatformConfigurationHelper.IsFreeNavigationSupported())
			throw new CoreConfigurationException($"Current system does not support full filesystem access");

		showHiddenFilesVariable = Owner.GetVariable("ShowHiddenFiles");
		if (showHiddenFilesVariable == null)
			throw new CoreConfigurationException("ShowHiddenFiles variable not found");

		accessNetworkDrivesVariable = Owner.GetVariable("AccessNetworkDrives");
		if (accessNetworkDrivesVariable == null)
			throw new CoreConfigurationException("AccessNetworkDrives variable not found");

		// In case of invalid path the fall back is %PROJECTDIR%
		var startFolderPathResourceUri = new ResourceUri(pathVariable.Value);

		resourceUriHelper = new ResourceUriHelper(LogicObject.NodeId.NamespaceIndex);
		if (!resourceUriHelper.IsFolderPathAllowed(startFolderPathResourceUri, accessFullFilesystemVariable.Value, accessNetworkDrivesVariable.Value))
		{
			Log.Error("FilesystemBrowserLogic", $"Path variable '{pathVariable.Value.Value}' is invalid. Falling back to '%PROJECTDIR%\\'");
			startFolderPathResourceUri = resourceUriHelper.GetDefaultResourceUri();
			pathVariable.Value = startFolderPathResourceUri;
		}

		Browse(startFolderPathResourceUri);

		pathVariable.VariableChange += PathVariable_VariableChange;
	}

	public override void Stop()
	{
		pathVariable.VariableChange -= PathVariable_VariableChange;
	}

	/// <summary>
	/// This method handles the variable change event for the path variable.
	/// It checks if the new value is different from the old value,
	/// validates the new path, and then browses to the new path if allowed.
	/// </summary>
	/// <param name="sender">The sender of the event.</param>
	/// <param name="e">The event arguments containing the old and new values.</param>
	private void PathVariable_VariableChange(object sender, VariableChangeEventArgs e)
	{
		var updatedPathResourceUri = new ResourceUri(e.NewValue);
		var oldPathResourceUri = new ResourceUri(e.OldValue);

		if (oldPathResourceUri.Uri == updatedPathResourceUri.Uri)
			return;

		if (!resourceUriHelper.IsFolderPathAllowed(updatedPathResourceUri, accessFullFilesystemVariable.Value, accessNetworkDrivesVariable.Value))
		{
			Log.Error("FilesystemBrowserLogic", $"Cannot browse to {updatedPathResourceUri} since this path is not allowed in current configuration");
			return;
		}

		Browse(updatedPathResourceUri);
	}

	/// <summary>
	/// This method browses the specified file system path and returns a list of entries (files and directories).
	/// It checks if the path exists, and if so, processes the directory and its contents according to filtering rules.
	/// </summary>
	/// <param name="resourceUri">The resource URI specifying the path to browse.</param>
	/// <remarks>
	/// - Validates that the path is not empty and that the directory exists.
	/// - Clears the existing files list.
	/// - Adds a "back" entry for the parent directory.
	/// - Filters files and directories based on specified extensions.
	/// </remarks>
	private void Browse(ResourceUri resourceUri)
	{
		string path = resourceUri.Uri;

		if (path == string.Empty)
		{
			Log.Warning("FilesystemBrowserLogic", "Path variable is empty");
			return;
		}

		if (!Directory.Exists(path))
		{
			Log.Warning("FilesystemBrowserLogic", $"Path '{path}' does not exist");
			return;
		}

		var currentDirectory = new DirectoryInfo(@path);
		var filesList = LogicObject.GetObject("FilesList");
		if (filesList == null)
			return;

		// Clean files list
		filesList.Children.ToList().ForEach((entry) => entry.Delete());

		// Create back entry
		if (BackEntryMustBeAdded(resourceUri))
		{
			var backEntry = InformationModel.MakeObject<FileEntry>("back");
			backEntry.FileName = "..";
			backEntry.IsDirectory = true;
			filesList.Add(backEntry);
		}

		string extensions = filterVariable.Value;
		var extensionsList = extensions.Split(';').ToList();

		var directories = currentDirectory.GetFileSystemInfos().Where(entry => entry is DirectoryInfo &&
																				FileHasToBeListed(entry));
		foreach (var dir in directories)
		{
			var fileSystemEntry = CreateFilesystemEntry(dir, true);
			filesList.Add(fileSystemEntry);
		}

		var files = currentDirectory.GetFileSystemInfos().Where(entry => entry is FileInfo &&
																		 FileHasToBeListed(entry));
		foreach (var file in files)
		{
			if (!AllFilesFilterSelected(extensionsList) && FileHasToBeFiltered(extensionsList, file))
				continue;

			var fileSystemEntry = CreateFilesystemEntry(file, false);
			filesList.Add(fileSystemEntry);
		}
	}

	/// <summary>
	/// Determines whether a file should be listed based on its attributes and a flag indicating whether to show hidden files.
	/// See https://docs.microsoft.com/it-it/dotnet/api/system.io.fileattributes?view=netcore-2.1 for additional information on FileAttributes values
	/// Junction points must be filtered out on Windows and system files must be filtered out
	/// </summary>
	/// <param name="fileSystemInfo">The file system information of the file.</param>
	/// <param name="showHiddenFiles">A boolean indicating whether to show hidden files.</param>
	/// <returns>
	/// A boolean value indicating whether the file should be listed.
	/// </returns>
	private bool FileHasToBeListed(FileSystemInfo fileSystemInfo)
	{
		bool showHiddenFiles = showHiddenFilesVariable.Value;
		return !fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
			   !fileSystemInfo.Attributes.HasFlag(FileAttributes.System) &&
			   (showHiddenFiles || !fileSystemInfo.Attributes.HasFlag(FileAttributes.Hidden));
	}

	/// <summary>
	/// Determines whether a back entry must be added based on the URI type.
	/// </summary>
	/// <param name="resourceUri">The resource URI to check.</param>
	/// <returns>true if a back entry must be added; otherwise, false.</returns>
	private bool BackEntryMustBeAdded(ResourceUri resourceUri)
	{
		switch (resourceUri.UriType)
		{
			case UriType.ApplicationRelative:
				return !string.IsNullOrEmpty(resourceUri.ApplicationRelativePath);
			case UriType.ProjectRelative:
				return !string.IsNullOrEmpty(resourceUri.ProjectRelativePath);
            case UriType.USBRelative:
            case UriType.SDRelative:
                return !string.IsNullOrEmpty(resourceUri.DeviceRelativePath);
            case UriType.AbsoluteFilePath:
				string path = resourceUri.Uri;
				return !PlatformConfigurationHelper.IsWindowsDriveBaseFolder(path) &&
					!PlatformConfigurationHelper.IsGenericLinuxRoot(path);
		}

		return false;
	}

	/// <summary>
	/// Creates a FileEntry object based on a FileSystemInfo entry and a boolean indicating if it's a directory.
	/// </summary>
	/// <param name="entry">The FileSystemInfo object representing the file system entry.</param>
	/// <param name="isDirectory">A boolean indicating whether the entry is a directory.</param>
	/// <returns>
	/// A FileEntry object containing the name, directory status, and size (in kilobytes) of the entry.
	/// </returns>
	private FileEntry CreateFilesystemEntry(FileSystemInfo entry, bool isDirectory)
	{
		var fileSystemEntry = InformationModel.MakeObject<FileEntry>(entry.Name);
		fileSystemEntry.FileName = entry.Name;
		fileSystemEntry.IsDirectory = isDirectory;
		if (!isDirectory)
		{
			var file = entry as FileInfo;
			fileSystemEntry.Size = (ulong)Math.Round(file.Length / 1000.0);
		}

		return fileSystemEntry;
	}

	/// <summary>
	/// This method checks if a file should be filtered based on its extension.
	/// It determines if the file's extension is in the list of excluded extensions.
	/// </summary>
	/// <param name="extensionsList">A list of string extensions to exclude.</param>
	/// <param name="file">The file system information object containing the file's extension.</param>
	/// <returns>
	/// A boolean value indicating whether the file should be filtered.
	/// If the file's extension is in the excluded list, it returns <c>true</c>.
	/// Otherwise, it returns <c>false</c>.
	/// </returns>
	private bool FileHasToBeFiltered(List<string> extensionsList, FileSystemInfo file)
	{
		return !extensionsList.Contains($"*{file.Extension}");
	}

	/// <summary>
	/// This method checks if all files in the list are filtered with the wildcard '*.*' or if the list contains only one entry which is an empty string.
	/// </summary>
	/// <param name="extensionsList">A list of string extensions to check.</param>
	/// <returns>
	/// A boolean value indicating whether the files are filtered correctly.
	/// </returns>
	private bool AllFilesFilterSelected(List<string> extensionsList)
	{
		return extensionsList.Contains("*.*") || (extensionsList.Count == 1 && extensionsList.Contains(string.Empty));
	}

	private IUAVariable pathVariable;
	private IUAVariable filterVariable;
	private IUAVariable accessFullFilesystemVariable;
	private IUAVariable accessNetworkDrivesVariable;
	private IUAVariable showHiddenFilesVariable;
	private ResourceUriHelper resourceUriHelper;
}

namespace FilesystemBrowserHelper
{
	public class ResourceUriHelper
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ResourceUriHelper"/> class with the specified namespace index.
		/// </summary>
		/// <param name="namespaceIndex">The index of the namespace to use.</param>
		/// <remarks>
		/// This constructor sets the <see cref="namespaceIndex"/> property to the provided value.
		/// </remarks>
		public ResourceUriHelper(int namespaceIndex)
		{
			this.namespaceIndex = namespaceIndex;
		}

		private IUAObject locationsObject;
		public IUAObject LocationsObject
		{
			set
			{
				if (locationsObject != value)
					locationsObject = value;
			}
		}

		/// <summary>
		/// This method adds a namespace prefix to the given resource URI string.
		/// If the URI starts with "%APPLICATIONDIR" or "%PROJECTDIR", it replaces
		/// the URI with a new string that includes the namespace index and the original URI.
		/// </summary>
		/// <param name="resourceUriString">The resource URI string to modify.</param>
		/// <returns>
		/// The modified URI string with the namespace prefix added.
		/// </returns>
		public string AddNamespacePrefixToFTOptixRuntimeFolder(string resourceUriString)
		{
			if (resourceUriString.StartsWith("%APPLICATIONDIR") || resourceUriString.StartsWith("%PROJECTDIR"))
				resourceUriString = $"ns={namespaceIndex};{resourceUriString}";

			return resourceUriString;
		}

		/// <summary>
		/// This method checks if the provided ResourceUri is valid by resolving its path and verifying that the directory exists.
		/// </summary>
		/// <param name="resourceUri">The ResourceUri object to validate.</param>
		/// <returns>
		/// A boolean value indicating whether the resource URI is valid.
		/// - Returns <c>true</c> if the directory exists and the URI is valid.
		/// - Returns <c>false</c> if the URI cannot be resolved or the directory does not exist.
		/// </returns>
		public bool IsResourceUriValid(ResourceUri resourceUri)
		{
			// Check that the start folder resource uri can be resolved (i.e. non existing USB device or mispelled path)
			string resolvedPath;
			try
			{
				resolvedPath = resourceUri.Uri;
			}
			catch (Exception)
			{
				return false;
			}

			if (!Directory.Exists(resolvedPath))
				return false;

			return true;
		}

		/// <summary>
		/// This method checks if the given resource URI is relative.
		/// </summary>
		/// <param name="resourceUri">The resource URI to check.</param>
		/// <returns>
		/// A boolean value indicating whether the URI is relative.
		/// </returns>
		public bool IsResourceUriRelative(ResourceUri resourceUri)
		{
			return resourceUri.UriType == UriType.ApplicationRelative ||
				resourceUri.UriType == UriType.ProjectRelative ||
                resourceUri.UriType == UriType.USBRelative ||
                resourceUri.UriType == UriType.SDRelative;
        }

        /// <summary>
        /// Checks if a folder path is allowed based on access permissions and operating system.
        /// </summary>
        /// <param name="startFolderPathResourceUri">The resource URI of the folder path to check.</param>
        /// <param name="accessFullFilesystem">Indicates whether the user has access to the full filesystem.</param>
        /// <param name="accessNetworkDrives">Indicates whether the user has access to network drives.</param>
        /// <returns>
        /// A boolean indicating whether the folder path is allowed.
        /// </returns>
        public bool IsFolderPathAllowed(ResourceUri startFolderPathResourceUri,
												bool accessFullFilesystem,
												bool accessNetworkDrives)
		{
			if (!IsResourceUriValid(startFolderPathResourceUri))
				return false;

			if (IsResourceUriRelative(startFolderPathResourceUri))
				return IsResourceUriValid(startFolderPathResourceUri);

			if (Environment.OSVersion.Platform == PlatformID.Unix)
				return false;

			// Here we have a Windows full path
			FileInfo drivePathRoot = new FileInfo(Path.GetPathRoot(startFolderPathResourceUri.Uri));
			DriveInfo drive = new DriveInfo(drivePathRoot.FullName);

			bool isNetworkDrive = drive.DriveType == DriveType.Network;
			if (accessFullFilesystem && !isNetworkDrive)
				return IsResourceUriValid(startFolderPathResourceUri);

			bool isAccessibleNetworkDrive = isNetworkDrive && PlatformConfigurationHelper.IsWindowsNetworkDriveAccessible(startFolderPathResourceUri.Uri);
			if (accessFullFilesystem && isAccessibleNetworkDrive)
				return accessNetworkDrives && IsResourceUriValid(startFolderPathResourceUri);

			return false;
		}

		/// <summary>
		/// This method returns a ResourceUri object with a string representation that includes the namespace index and the project directory.
		/// The string format is "ns={namespaceIndex};%PROJECTDIR%\\".
		/// </summary>
		/// <returns>
		/// A ResourceUri object with the specified string format.
		/// </returns>
		public ResourceUri GetDefaultResourceUri()
		{
			return new ResourceUri($"ns={namespaceIndex};%PROJECTDIR%\\");
		}

		/// <summary>
		/// This method returns a string that contains the default resource URI in the format
		/// "ns={namespaceIndex};%PROJECTDIR%\\".
		/// </summary>
		/// <returns>
		/// A string in the format "ns={namespaceIndex};%PROJECTDIR%\\".
		/// </returns>
		public string GetDefaultResourceUriAsString()
		{
			return $"ns={namespaceIndex};%PROJECTDIR%\\";
		}

		/// <summary>
		/// This method calculates the relative path from a resource URI to the location.
		/// Depending on the URI type, it returns the appropriate relative path.
		/// </summary>
		/// <param name="resourceUri">The URI representing the resource location.</param>
		/// <returns>
		/// A string representing the relative path from the resource URI to the location.
		/// </returns>
		public string GetRelativePathToLocationFromResourceUri(ResourceUri resourceUri)
		{
			string relativeFolderPath;
			switch (resourceUri.UriType)
			{
				case UriType.ApplicationRelative:
					relativeFolderPath = resourceUri.ApplicationRelativePath;
					break;
				case UriType.ProjectRelative:
					relativeFolderPath = resourceUri.ProjectRelativePath;
					break;
				case UriType.USBRelative:
                case UriType.SDRelative:
                    relativeFolderPath = resourceUri.DeviceRelativePath;
                    break;
				case UriType.AbsoluteFilePath:
					string baseLocation = GetBaseLocationPathFromLocationsObject(resourceUri);

					// If a location exists, its value must be correct because it was created by this widget at startup
					var resolvedBaseLocationPath = new ResourceUri(baseLocation).Uri;
					relativeFolderPath = GetRelativePathToLocationFromAbsoluteSystemPath(resolvedBaseLocationPath, resourceUri.Uri);
					break;
				default:
					throw new CoreConfigurationException($"UriType '{resourceUri.UriType}' not expected");
			}

			return relativeFolderPath;
		}

		/// <summary>
		/// Convert the updated full path (e.g. D:\\MyFolder\\SubFolder) to a string following FTOptixStudio conventions:
		/// location base path + relative path that location (e.g. %USB1%/MyFolder\\SubFolder)
		/// A string with this format can then be parsed back into a ResourceUri with the corresponding UriType
		/// </summary>
		/// <param name="oldResourceUri">The original resource URI from which the base location path is derived.</param>
		/// <param name="updatedPath">The path to be added relative to the base location path.</param>
		/// <returns>
		/// A string representing the formatted path, combining the base location path and the relative path.
		/// </returns>
		public string GetFTOptixStudioFormattedPath(ResourceUri oldResourceUri, string updatedPath)
		{
			var baseLocationPath = GetBaseLocationPathFromLocationsObject(oldResourceUri);
			string relativePathToLocation;

			if (oldResourceUri.UriType == UriType.AbsoluteFilePath)
				relativePathToLocation = GetRelativePathToLocationFromAbsoluteSystemPath(baseLocationPath, updatedPath);
			else
				relativePathToLocation = GetRelativePathToLocationFromRelativeSystemPath(baseLocationPath, oldResourceUri.UriType, updatedPath);

			bool endsWithSlash = baseLocationPath.EndsWith("/") || baseLocationPath.EndsWith("\\");
			if (!endsWithSlash && !string.IsNullOrEmpty(relativePathToLocation))
				baseLocationPath += "/";

			return baseLocationPath + relativePathToLocation;
		}

		/// <summary>
		/// Retrieves the value of the base location path from the locations object based on the given resource URI.
		/// </summary>
		/// <param name="resourceUri">The URI of the resource for which the base location path is being retrieved.</param>
		/// <returns>
		/// A string value representing the base location path from the locations object.
		/// </returns>
		public string GetBaseLocationPathFromLocationsObject(ResourceUri resourceUri)
		{
			if (locationsObject == null)
				throw new CoreConfigurationException("Object Locations is not initialized");

			var baseLocation = locationsObject.GetVariable(GetBaseLocationBrowseName(resourceUri));
			if (baseLocation == null)
				throw new CoreConfigurationException($"Locations object is malformed");

			return baseLocation.Value;
		}

		/// <summary>
		/// This method returns the relative path from the base location to the system path.
		/// If the system path is the same as the base location, it returns an empty string.
		/// </summary>
		/// <param name="baseLocationPath">The absolute path of the base location.</param>
		/// <param name="systemPath">The system path from which the relative path is calculated.</param>
		/// <returns>
		/// A string representing the relative path from the base location to the system path.
		/// If the paths are the same, returns an empty string.
		/// </returns>
		private string GetRelativePathToLocationFromAbsoluteSystemPath(string baseLocationPath, string systemPath)
		{
			// Get the base location from the current resource uri value
			var resolvedBaseLocationPath = new ResourceUri(baseLocationPath).Uri;
			if (systemPath == resolvedBaseLocationPath)
				return string.Empty;

			return systemPath.Substring(resolvedBaseLocationPath.Length);
		}

		/// <summary>
		/// This method returns the relative path from the base location path to the new full path,
		/// considering the URI type and operating system platform.
		/// </summary>
		/// <param name="baseLocationPath">The base location path as a string.</param>
		/// <param name="uriType">The type of URI (e.g., USBRelative).</param>
		/// <param name="newFullPath">The full path from which to extract the relative path.</param>
		/// <returns>
		/// A string representing the relative path, adjusted based on the URI type and operating system platform.
		/// </returns>
		private string GetRelativePathToLocationFromRelativeSystemPath(string baseLocationPath, UriType uriType, string newFullPath)
		{
			// Extract the relative path from %APPLICATIONDIR%, %PROJECTDIR%, %USB<n>% by removing the computed baseLocationPath.
			// E.g. On Windows with 'D:\\MyFolder\\SubFolder' removing '%USB1%/'=='D:\\' results in 'MyFolder\\SubFolder'.
			// E.g. On Unix with '/storage/usb1/MyFolder/SubFolder' removing '%USB1%/'=='/storage/usb1' results in '/MyFolder/SubFolder'.
			var resolvedBaseLocationPathUri = new ResourceUri(baseLocationPath);

			if (newFullPath.Length < resolvedBaseLocationPathUri.Uri.Length)
				return string.Empty;

			var resultRelativePath = newFullPath.Substring(resolvedBaseLocationPathUri.Uri.Length);

			if (string.IsNullOrEmpty(resultRelativePath))
				return string.Empty;

			// On Unix the initial "/" must be removed always.
			// A Unix USB path has to be managed as a normal filesyestem path:
			// i.e. /storage/usb1/MyFolder has "/myFolder" as resultedRelativePath, so "/" has to be removed
			if (Environment.OSVersion.Platform == PlatformID.Unix)
				return resultRelativePath.Substring(1);

			// On Windows in case of %APPLICATIONDIR%, %PROJECTDIR% the initial "/" of resultRelativePath must be removed.
			// Windows USB path starts with <Drive>:/ so in that case the initial character has not to be removed:
			// i.e. D:/MyFolder has "MyFolder" has resultedRelativePath, so nothing has to be removed
			if (uriType != UriType.USBRelative && uriType != UriType.SDRelative)
				return resultRelativePath.Substring(1);

			return resultRelativePath;
		}

		/// <summary>
		/// This method returns the base location browse name based on the URI type.
		/// </summary>
		/// <param name="resourceUri">The resource URI to determine the base location.</param>
		/// <returns>
		/// A string representing the base location browse name.
		/// </returns>
		private string GetBaseLocationBrowseName(ResourceUri resourceUri)
		{
			switch (resourceUri.UriType)
			{
				case UriType.ApplicationRelative:
					return "APPLICATION_DIR";
				case UriType.ProjectRelative:
					return "PROJECT_DIR";
                case UriType.USBRelative:
                    return $"USB{resourceUri.DeviceNumber}";
                case UriType.SDRelative:
                    return $"SD{resourceUri.DeviceNumber}";
                case UriType.AbsoluteFilePath:
					if (Environment.OSVersion.Platform == PlatformID.Unix) // Only Debian is supported
						return PlatformConfigurationHelper.GetGenericLinuxBaseBrowseNameFolder();
					else // Windows
						return PlatformConfigurationHelper.GetWindowsDrivePathRoot(resourceUri).ToUpper();
				default:
					return null;
			}
		}

		private readonly int namespaceIndex;
	}

	static class PlatformConfigurationHelper
	{
		/// <summary>
		/// This method returns the string "root" as the base browse name folder.
		/// </summary>
		/// <returns>
		/// A string value containing "root".
		/// </returns>
		public static string GetGenericLinuxBaseBrowseNameFolder()
		{
			return "root";
		}

		/// <summary>
		/// This method returns the base folder path for Linux systems.
		/// </summary>
		/// <returns>
		/// A string representing the base folder path, which is "/".
		/// </returns>
		public static string GetGenericLinuxBaseFolderPath()
		{
			return "/";
		}

		/// <summary>
		/// This method checks if the given path is a base folder of a Windows drive.
		/// A base folder is a folder that is directly under a drive's root directory,
		/// such as "C:\Users" or "D:\".
		/// </summary>
		/// <param name="path">The path to check.</param>
		/// <returns>
		/// A boolean value indicating whether the path is a base folder of a Windows drive.
		/// </returns>
		public static bool IsWindowsDriveBaseFolder(string path)
		{
			return Path.GetPathRoot(path) == path;
		}

		/// <summary>
		/// This method checks if the given path is the root directory of a generic Linux system.
		/// </summary>
		/// <param name="path">The path to check.</param>
		/// <returns>
		/// A boolean value indicating whether the path is the root directory.
		/// </returns>
		public static bool IsGenericLinuxRoot(string path)
		{
			return path == "/";
		}

		/// <summary>
		/// This method retrieves the root path of a given resource URI.
		/// It uses the <see cref="Path.GetPathRoot"/> method to extract the root path
		/// from the full path of the resource.
		/// </summary>
		/// <param name="resourceUri">The resource URI from which to retrieve the root path.</param>
		/// <returns>
		/// A string representing the root path of the resource.
		/// If an exception occurs, an <see cref="Exception"/> is thrown with the
		/// message indicating the failure to retrieve the root path.
		/// </returns>
		public static string GetWindowsDrivePathRoot(ResourceUri resourceUri)
		{
			string rootPath;
			try
			{
				FileInfo pathInfo = new FileInfo(resourceUri.Uri);
				rootPath = Path.GetPathRoot(pathInfo.FullName);
			}
			catch (Exception exception)
			{
				throw new Exception($"Unable to get root path of '{resourceUri.Uri}': {exception.Message}");
			}

			return rootPath;
		}

		/// <summary>
		/// This method checks if free navigation is supported based on the operating system.
		/// On Unix-based systems, it returns false, indicating that free navigation is not supported.
		/// </summary>
		/// <returns>
		/// A boolean value indicating whether free navigation is supported.
		/// </returns>
		public static bool IsFreeNavigationSupported()
		{
			if (Environment.OSVersion.Platform == PlatformID.Unix)
				return false;

			return true;
		}

		/// <summary>
		/// Checks if a given Windows network drive is accessible.
		/// </summary>
		/// <param name="filePath">The file path to check.</param>
		/// <returns>
		/// A boolean value indicating whether the network drive is accessible.
		/// </returns>
		public static bool IsWindowsNetworkDriveAccessible(string filePath)
		{
			var pathRoot = Path.GetPathRoot(filePath);
			var pathRootLetter = pathRoot.TrimEnd(new char[] { '\\' });

			string output;
			try
			{
				output = LaunchProcess("net", "use");
			}
			catch (Exception exception)
			{
				Log.Error("PlatformConfigurationHelper", $"Unable to determine connected network drives: {exception.Message}");
				return false;
			}

			foreach (string line in output.Split('\n'))
			{
				if (line.Contains(pathRootLetter) && line.Contains("OK"))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// This method launches a process with the specified process name and parameter.
		/// The method executes the process and returns the standard output as a string.
		/// </summary>
		/// <param name="processName">The name of the process to launch.</param>
		/// <param name="parameter">The parameter to pass to the process.</param>
		/// <returns>
		/// A string containing the standard output of the executed process.
		/// </returns>
		private static string LaunchProcess(string processName, string parameter)
		{
			string output;
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = processName,
				UseShellExecute = false,
				Arguments = parameter,
				RedirectStandardOutput = true,
				CreateNoWindow = true
			};

			Process process = Process.Start(processStartInfo);
			output = process.StandardOutput.ReadToEnd().Trim();
			process.WaitForExit();
			process.Close();

			return output;
		}
	}
}
