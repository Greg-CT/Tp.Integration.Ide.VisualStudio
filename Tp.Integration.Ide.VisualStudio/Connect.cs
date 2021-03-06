using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Extensibility;
using Microsoft.VisualStudio.CommandBars;
using Tp.Integration.Ide.VisualStudio.Services;
using Tp.Integration.Ide.VisualStudio.UI;

namespace Tp.Integration.Ide.VisualStudio
{
	/// <summary>
	/// The object for implementing an Add-in.
	/// </summary>
	/// <seealso class='IDTExtensibility2' />
	public class Connect : IDTExtensibility2, IDTCommandTarget
	{
		internal const string CmdLogin = "Login";
		internal const string CmdLogout = "Logout";
		internal const string CmdToDoList = "ToDoList";
		internal const string CmdOptions = "Options";

		private AddIn _addIn;
		private DTE2 _application;
		private Window _toolWindow;
		private TraceListener _listener;
		private ControllerEnvironment _env;
		private CommandBarPopup _commandBarPopup;
		private Controller _controller;

		#region IDTExtensibility2 Members

		/// <summary>
		/// Implements the OnConnection method of the IDTExtensibility2 interface. Receives notification that the Add-in is being loaded.
		/// </summary>
		/// <param name='application'>
		/// Root object of the host application.
		/// </param>
		/// <param name='connectMode'>
		/// Describes how the Add-in is being loaded.
		/// </param>
		/// <param name='addIn'>
		/// Object representing this Add-in.
		/// </param>
		/// /// <param name='custom'>
		/// Array of parameters that are host application specific.
		/// </param>
		/// <seealso class='IDTExtensibility2' />
		public void OnConnection(object application, ext_ConnectMode connectMode, object addIn, ref Array custom)
		{
			_application = (DTE2)application;
			_addIn = (AddIn)addIn;
			try
			{
				if (connectMode == ext_ConnectMode.ext_cm_Startup || connectMode == ext_ConnectMode.ext_cm_AfterStartup)
				{
					_listener = CreateTraceListener();
					_env = new ControllerEnvironment(new WindowHandle(_application.MainWindow.HWnd), _listener);
					_controller = CreateController();
					_controller.OnConnectionStateChange += OnConnectionStateChange;
					CreateCommands();
					CreateToolWindow();
					_listener.WriteLine("Addin initialized");
					if (connectMode == ext_ConnectMode.ext_cm_AfterStartup)
					{
						OnStartupComplete(ref custom);
					}
				}
			}
			catch (Exception ex)
			{
				if (_listener != null)
				{
					_listener.WriteLine(ex);
				}
			}
		}

		/// <summary>
		/// Receives notification that the Add-in is being unloaded.
		/// </summary>
		/// <param name='disconnectMode'>
		/// Describes how the Add-in is being unloaded.
		/// </param>
		/// <param name='custom'>
		/// Array of parameters that are host application specific.
		/// </param>
		/// <seealso class='IDTExtensibility2' />
		public void OnDisconnection(ext_DisconnectMode disconnectMode, ref Array custom)
		{
			if (disconnectMode == ext_DisconnectMode.ext_dm_HostShutdown || disconnectMode == ext_DisconnectMode.ext_dm_UserClosed)
			{
				if (_commandBarPopup != null)
				{
					_commandBarPopup.Delete(null);
					_commandBarPopup = null;
				}
				if (_controller != null)
				{
					_controller.Dispose();
					_controller = null;
				}
				if (_listener != null)
				{
					_listener.WriteLine("Disposed");
					_listener.Dispose();
					_listener = null;
				}
			}
		}

		/// <summary>
		/// Receives notification when the collection of Add-ins has changed.
		/// </summary>
		/// <param name='custom'>
		/// Array of parameters that are host application specific.
		/// </param>
		/// <seealso class='IDTExtensibility2' />
		public void OnAddInsUpdate(ref Array custom) { }

		/// <summary>
		/// Implements the OnStartupComplete method of the IDTExtensibility2 interface. Receives notification that the host application has completed loading.
		/// </summary>
		/// <param name='custom'>
		/// Array of parameters that are host application specific.
		/// </param>
		/// <seealso class='IDTExtensibility2' />
		public void OnStartupComplete(ref Array custom)
		{
			try
			{
				var settings = new Settings();
				if (settings.AutoLogin)
				{
					_toolWindow.Visible = true;
					_controller.Connect(false);
				}
			}
			catch (Exception ex)
			{
				_listener.WriteLine(ex);
			}
		}

		/// <summary>
		/// Receives notification that the host application is being unloaded.
		/// </summary>
		/// <param name='custom'>
		/// Array of parameters that are host application specific.
		/// </param>
		/// <seealso class='IDTExtensibility2' />
		public void OnBeginShutdown(ref Array custom) { }

		#endregion

		/// <summary>
		/// Creates <see cref="TraceListener"/> which appends to Output Window.
		/// </summary>
		/// <returns>New trace listener instance.</returns>
		private TraceListener CreateTraceListener()
		{
			OutputWindow outputWindow = _application.ToolWindows.OutputWindow;
			OutputWindowPane outputWindowPane = null;
			foreach (OutputWindowPane pane in outputWindow.OutputWindowPanes)
			{
				if (string.Compare(pane.Name, "TargetProcess", StringComparison.InvariantCulture) != 0) continue;
				outputWindowPane = pane;
				break;
			}
			if (outputWindowPane == null)
			{
				outputWindowPane = outputWindow.OutputWindowPanes.Add("TargetProcess");
			}
			outputWindowPane.Activate();
			return new OutputPaneListener(outputWindowPane);
		}

		/// <summary>
		/// Creates controller behind the To Do list view.
		/// </summary>
		private Controller CreateController()
		{
			var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TargetProcess");
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			var path = Path.Combine(dir, "TimeTracking.xml");
			return new Controller(new FileSystemTimeTrackingRepository(path), WebServicesFactory.Instance, _env);
		}

		/// <summary>
		/// Creates TargetProcess popup menu with commands.
		/// </summary>
		private void CreateCommands()
		{
			// Find the MenuBar command bar, which is the top-level command bar holding all the main menu items.
			var menuBarCommandBar = ((CommandBars)_application.CommandBars)["MenuBar"];

			// Create the 'TargetProcess' popup menu before the 'Window' popup menu in the main menu bar.
			_commandBarPopup = (CommandBarPopup)menuBarCommandBar.Controls.Add(
													MsoControlType.msoControlPopup, Type.Missing, Type.Missing,
													FindMenuBarControl(menuBarCommandBar, "Window").Index, Type.Missing);
			_commandBarPopup.Caption = "TargetProcess";

			// Add commands.
			CreateCommand(_commandBarPopup.CommandBar, CmdLogin, "Login...", "Login to TargetProcess");
			CreateCommand(_commandBarPopup.CommandBar, CmdLogout, "Logout", "Logout from TargetProcess");
			CreateCommand(_commandBarPopup.CommandBar, CmdToDoList, "View To Do List", "Show TargetProcess To Do list window");
			CreateCommand(_commandBarPopup.CommandBar, CmdOptions, "Options...", "Show Options");
		}

		private static CommandBarPopup FindMenuBarControl(CommandBar menuBarCommandBar, string controlName)
		{
			foreach (CommandBarControl item in menuBarCommandBar.Controls)
			{
				if (item is CommandBarPopup && ((CommandBarPopup)item).CommandBar.Name == controlName)
					return (CommandBarPopup)item;
			}
			return null;
		}

		/// <summary>
		/// Adds named command to the TargetProcess popup menu.
		/// </summary>
		/// <param name="commandBar">Add command to this command bar.</param>
		/// <param name="name">Command name.</param>
		/// <param name="text">Command text.</param>
		/// <param name="tooltip">Command description.</param>
		private void CreateCommand(CommandBar commandBar, string name, string text, string tooltip)
		{
			var commands = (Commands2)_application.Commands;
			Command command;
			try
			{
				var contextGUIDS = new object[] { };
				command = commands.AddNamedCommand2(
					_addIn, name, text, tooltip, true, null, ref contextGUIDS,
					(int)vsCommandStatus.vsCommandStatusSupported + (int)vsCommandStatus.vsCommandStatusEnabled,
					(int)vsCommandStyle.vsCommandStylePictAndText, vsCommandControlType.vsCommandControlTypeButton);
			}
			catch (ArgumentException)
			{
				command = commands.Item(_addIn.ProgID + "." + name, 0);
			}
			if (command != null)
			{
				command.AddControl(commandBar, commandBar.Controls.Count + 1);
			}
		}

		/// <summary>
		/// Create tool window with the To Do list.
		/// </summary>
		private void CreateToolWindow()
		{
			if (_toolWindow != null)
			{
				_toolWindow.Activate();
			}
			else
			{
				object programmableObject = null;
				_toolWindow = ((Windows2)_application.Windows).CreateToolWindow2(
					_addIn, typeof(ToolWindowControl).Assembly.Location, typeof(ToolWindowControl).FullName, "TargetProcess Window",
					"{A523C4AD-EA9B-4944-94AB-41313992B2EE}", ref programmableObject);
				_toolWindow.SetTabPicture(ConvertImage.Convert(Resources.Icon));
				var toolWindowControl = (ToolWindowControl)programmableObject;
				toolWindowControl.Init(_application, _controller, _env);
				_toolWindow.Visible = true;
			}
		}

		/// <summary>
		/// Receives notification that the connection state has changed.
		/// </summary>
		private void OnConnectionStateChange(object sender, ConnectionStateEventArgs e)
		{
			_toolWindow.Caption = e.Connected ? String.Format("TargetProcess Window - Connected to {0}", e.Uri) : "TargetProcess Window - Disconnected";
		}

		#region IDTCommandTarget Members

		/// <summary>
		/// Implements the QueryStatus method of the IDTCommandTarget interface. This is called when the command's availability is updated.
		/// </summary>
		/// <param name='commandName'>
		/// The name of the command to determine state for.
		/// </param>
		/// <param name='neededText'>
		/// Text that is needed for the command.
		/// </param>
		/// <param name='status'>
		/// The state of the command in the user interface.
		/// </param>
		/// <param name='commandText'>
		/// Text requested by the neededText parameter.
		/// </param>
		/// <seealso class='Exec' />
		public void QueryStatus(string commandName, vsCommandStatusTextWanted neededText, ref vsCommandStatus status, ref object commandText)
		{
			try
			{
				status = DoQueryStatus(commandName, neededText);
			}
			catch (Exception ex)
			{
				_listener.WriteLine(ex);
			}
		}

		private vsCommandStatus DoQueryStatus(string commandName, vsCommandStatusTextWanted neededText)
		{
			if (neededText == vsCommandStatusTextWanted.vsCommandStatusTextWantedNone)
			{
				if (commandName == _addIn.ProgID + "." + CmdLogin)
				{
					return _controller.Connected
							? vsCommandStatus.vsCommandStatusSupported
							: vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
				}
				if (commandName == _addIn.ProgID + "." + CmdLogout)
				{
					return _controller.Connected
							? vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled
							: vsCommandStatus.vsCommandStatusSupported;
				}
				return vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
			}
			return vsCommandStatus.vsCommandStatusSupported;
		}

		/// <summary>
		/// Implements the Exec method of the IDTCommandTarget interface. This is called when the command is invoked.
		/// </summary>
		/// <param name='commandName'>
		/// The name of the command to execute.
		/// </param>
		/// <param name='executeOption'>
		/// Describes how the command should be run.
		/// </param>
		/// <param name='varIn'>
		/// Parameters passed from the caller to the command handler.
		/// </param>
		/// <param name='varOut'>
		/// Parameters passed from the command handler to the caller.
		/// </param>
		/// <param name='handled'>
		/// Informs the caller if the command was handled or not.
		/// </param>
		/// <seealso class='Exec' />
		public void Exec(string commandName, vsCommandExecOption executeOption, ref object varIn, ref object varOut, ref bool handled)
		{
			try
			{
				handled = DoExec(commandName, executeOption);
			}
			catch (Exception ex)
			{
				_listener.WriteLine(ex);
			}
		}

		private bool DoExec(string commandName, vsCommandExecOption executeOption)
		{
			if (executeOption == vsCommandExecOption.vsCommandExecOptionDoDefault)
			{
				if (commandName == _addIn.ProgID + "." + CmdLogin)
				{
					if (!_controller.Connected)
					{
						_controller.Connect(true);
					}
					return true;
				}
				if (commandName == _addIn.ProgID + "." + CmdLogout)
				{
					if (_controller.Connected)
					{
						_controller.Disconnect();
					}
					return true;
				}
				if (commandName == _addIn.ProgID + "." + CmdToDoList)
				{
					_toolWindow.Visible = true;
					return true;
				}
				if (commandName == _addIn.ProgID + "." + CmdOptions)
				{
					_controller.Options();
					return true;
				}
			}
			return false;
		}

		#endregion
	}

	/// <summary>
	/// Trace listener to write to Visual Studio output window.
	/// </summary>
	internal sealed class OutputPaneListener : TraceListener
	{
		private readonly OutputWindowPane _outputWindowPane;

		public OutputPaneListener(OutputWindowPane outputWindowPane)
		{
			_outputWindowPane = outputWindowPane;
		}

		public OutputPaneListener(OutputWindowPane outputWindowPane, string name)
			: base(name)
		{
			_outputWindowPane = outputWindowPane;
		}

		public override void Write(string message)
		{
			try
			{
				_outputWindowPane.OutputString(message);
			}
			catch (Exception)
			{
				//
			}
		}

		public override void WriteLine(string message)
		{
			try
			{
				_outputWindowPane.OutputString(message);
				_outputWindowPane.OutputString("\n");
			}
			catch (Exception)
			{
				//
			}
		}
	}

	/// <summary>
	/// Casts HWND to IWin32Window.
	/// </summary>
	internal struct WindowHandle : IWin32Window
	{
		private readonly IntPtr _handle;

		public WindowHandle(int handle)
			: this(new IntPtr(handle)) { }

		public WindowHandle(IntPtr handle)
		{
			_handle = handle;
		}

		public IntPtr Handle
		{
			get { return _handle; }
		}
	}

	/// <summary>
	/// Helper class, see <a href="http://www.eggheadcafe.com/forumarchives/vstudioextensibility/Nov2005/post24688680.asp">article</a> for details.
	/// </summary>
	internal sealed class ConvertImage : AxHost
	{
		public ConvertImage(string clsid)
			: base(clsid) { }

		public ConvertImage(string clsid, int flags)
			: base(clsid, flags) { }

		/// <summary>
		/// Exposes protected static method from the inherited class to public.
		/// </summary>
		/// <see cref="AxHost.GetIPictureDispFromPicture(System.Drawing.Image)"/>
		public static object Convert(System.Drawing.Image image)
		{
			return GetIPictureDispFromPicture(image);
		}
	}
}