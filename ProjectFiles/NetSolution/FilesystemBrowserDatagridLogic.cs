#region Using directives
using FTOptix.Core;
using UAManagedCore;
using FTOptix.NetLogic;
using System.IO;
using System;
using FTOptix.UI;
using FTOptix.HMIProject;
using FilesystemBrowserHelper;
using FTOptix.OPCUAServer;
#endregion

public class FilesystemBrowserDatagridLogic : BaseNetLogic
{
	/// <summary>
	/// This method initializes various variables and configurations related to file system access and data grid interaction.
	/// It retrieves the "FolderPath", "FullPath", and "AccessFullFilesystem" variables from the owner object.
	/// If any of these variables are not found, a CoreConfigurationException is thrown.
	/// If "AccessFullFilesystem" is set to true but Free Navigation is not supported, the method returns early.
	/// It then initializes the "Locations" object and creates a ResourceUriHelper instance with the locations object.
	/// Finally, it attaches an event handler to the DataGrid for user selection changes.
	/// </summary>
	public override void Start()
	{
		pathVariable = Owner.Owner.GetVariable("FolderPath");
		if (pathVariable == null)
			throw new CoreConfigurationException("FolderPath variable not found in FilesystemBrowser");

		fullPathVariable = Owner.Owner.GetVariable("FullPath");
		if (fullPathVariable == null)
			throw new CoreConfigurationException("FullPath variable not found in FilesystemBrowser");

		accessFullFilesystemVariable = Owner.Owner.GetVariable("AccessFullFilesystem");
		if (accessFullFilesystemVariable == null)
			throw new CoreConfigurationException("AccessFullFilesystem variable not found");

		if (accessFullFilesystemVariable.Value && !PlatformConfigurationHelper.IsFreeNavigationSupported())
			return;

		locationsObject = Owner.Owner.GetObject("Locations");
		if (locationsObject == null)
			throw new CoreConfigurationException("Locations object not found");

		resourceUriHelper = new ResourceUriHelper(LogicObject.NodeId.NamespaceIndex)
		{
			LocationsObject = locationsObject
		};

		filesDatagrid = Owner as DataGrid;
		filesDatagrid.OnUserSelectionChanged += DataGrid_OnUserSelectionChanged;
	}

	public override void Stop()
	{
		filesDatagrid.OnUserSelectionChanged -= DataGrid_OnUserSelectionChanged;
	}

	/// <summary>
	/// Handles the UserSelectionChanged event for a DataGrid, updating the current path based on the selected item.
	/// </summary>
	/// <param name="sender">The source of the event.</param>
	/// <param name="e">The UserSelectionChangedEvent instance containing event data.</param>
	/// <remarks>
	/// The method checks if a selected item exists and is not null or empty. If so, it retrieves the corresponding FileEntry object and updates the current path with its file name.
	/// </remarks>
	private void DataGrid_OnUserSelectionChanged(object sender, UserSelectionChangedEvent e)
	{
		var selectedItemNodeId = e.SelectedItem;
		if (selectedItemNodeId == null || selectedItemNodeId.IsEmpty)
			return;

		var entry = (FileEntry)InformationModel.GetObject(selectedItemNodeId);
		if (entry == null)
			return;

		UpdateCurrentPath(entry.FileName);
	}

	/// <summary>
	/// This method updates the current path by adding a namespace prefix to the FT Optix Runtime folder
	/// and setting the path based on the last path token.
	/// </summary>
	/// <param name="lastPathToken">The last path token to use for determining the selected file.</param>
	private void UpdateCurrentPath(string lastPathToken)
	{
		// Necessary when FTOptixStudio placeholder path is configured with only %APPLICATIONDIR%\, %PROJECTDIR%\
		// i.e. at the start of the project
		var currentPath = resourceUriHelper.AddNamespacePrefixToFTOptixRuntimeFolder(pathVariable.Value);
		var currentPathResourceUri = new ResourceUri(currentPath);

		if (lastPathToken == "..")
			SetPathsToParentFolder(currentPathResourceUri);
		else
			SetPathsToSelectedFile(currentPathResourceUri, lastPathToken);
	}

	/// <summary>
	/// This method retrieves the parent directory of a given resource URI and sets the paths accordingly.
	/// </summary>
	/// <param name="startingDirectoryResourceUri">
	/// A ResourceUri object representing the starting directory.
	/// </param>
	private void SetPathsToParentFolder(ResourceUri startingDirectoryResourceUri)
	{
		DirectoryInfo parentDirectory;
		try
		{
			parentDirectory = Directory.GetParent(startingDirectoryResourceUri.Uri);
		}
		catch (Exception exception)
		{
			Log.Error("FilesystemBrowserDatagridLogic", $"Unable to get parent folder: {exception.Message}");
			return;
		}

		if (parentDirectory == null)
			return;

		var parentDirectoryPath = parentDirectory.FullName;

		// E.g. %PROJECTDIR%/PKI
		pathVariable.Value = resourceUriHelper.GetFTOptixStudioFormattedPath(startingDirectoryResourceUri,
																	   parentDirectoryPath);

		fullPathVariable.Value = ResourceUri.FromAbsoluteFilePath(parentDirectoryPath);
	}

	/// <summary>
	/// This method sets the paths to the selected file based on the provided directory and target file.
	/// It constructs a path by combining the directory URI with the target file name and handles potential errors.
	/// If the resulting path is not a directory, it returns early.
	/// </summary>
	/// <param name="currentDirectoryResourceUri">The URI of the current directory.</param>
	/// <param name="targetFile">The name of the target file to be added to the directory.</param>
	private void SetPathsToSelectedFile(ResourceUri currentDirectoryResourceUri, string targetFile)
	{
		string updatedPath;
		try
		{
			updatedPath = Path.Combine(currentDirectoryResourceUri.Uri, targetFile);
		}
		catch (Exception exception)
		{
			Log.Error("FilesystemBrowserDatagridLogic", $"Path not found {exception.Message}");
			return;
		}

		fullPathVariable.Value = ResourceUri.FromAbsoluteFilePath(updatedPath);

		if (!IsDirectory(updatedPath))
			return;

		// E.g. %PROJECTDIR%/PKI
		pathVariable.Value = resourceUriHelper.GetFTOptixStudioFormattedPath(currentDirectoryResourceUri,
																	   updatedPath);
	}

	/// <summary>
	/// This method checks if the specified path represents a directory.
	/// </summary>
	/// <param name="path">The path to check.</param>
	/// <returns>
	/// A boolean value indicating whether the path represents a directory.
	/// </returns>
	private bool IsDirectory(string path)
	{
		return Directory.Exists(path);
	}

	private IUAVariable pathVariable;
	private IUAVariable fullPathVariable;
	private DataGrid filesDatagrid;
	private IUAVariable accessFullFilesystemVariable;
	private IUAObject locationsObject;

	private ResourceUriHelper resourceUriHelper;
}
