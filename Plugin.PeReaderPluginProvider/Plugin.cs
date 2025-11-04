using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Plugin.FilePluginProvider;
using Plugin.PeReaderPluginProvider.Reader;
using SAL.Flatbed;

namespace Plugin.PeReaderPluginProvider
{
	/// <summary>Plugins loader from file system but it's using separate sandbox to find appropriate assemblies to load</summary>
	public class Plugin : IPluginProvider
	{
		// This is used to prevent stack overflow when trying to resolve assembly recursively
		private readonly ConcurrentDictionary<String, Int32> _recursiveAssemblyNameCheck = new ConcurrentDictionary<String, Int32>();

		// The list of files to ignore during loading to prevent multiple attempts to load invalid assemblies
		private readonly HashSet<String> _badFilesList = new HashSet<String>();

		private TraceSource _trace;

		private TraceSource Trace { get => this._trace ?? (this._trace = Plugin.CreateTraceSource<Plugin>()); }

		private IHost Host { get; }

		/// <summary>Arguments passed from primary application</summary>
		private FilePluginArgs Args { get; } = new FilePluginArgs();

		/// <summary>Monitor searching for new plugins in folders</summary>
		private List<FileSystemWatcher> Monitors { get; } = new List<FileSystemWatcher>();

		/// <summary>Parent plugin provider</summary>
		IPluginProvider IPluginProvider.ParentProvider { get; set; }

		/// <summary>Create instance if <see cref="Plugin"/> with reference to <see cref="IHost"/> instance.</summary>
		/// <param name="host">The host instance reference.</param>
		/// <exception cref="ArgumentNullException">The host should be valid.</exception>
		public Plugin(IHost host)
			=> this.Host = host ?? throw new ArgumentNullException(nameof(host));

		Boolean IPlugin.OnConnection(ConnectMode mode)
			=> true;

		Boolean IPlugin.OnDisconnection(DisconnectMode mode)
		{
			if(mode == DisconnectMode.UserClosed)
				throw new NotSupportedException("Plugin Provider can't be unloaded");
			else
			{
				if(this.Monitors.Count > 0)
				{
					foreach(FileSystemWatcher monitor in this.Monitors)
						monitor.Dispose();
					this.Monitors.Clear();
				}
				return true;
			}
		}

		void IPluginProvider.LoadPlugins()
		{
			Dictionary<String, AssemblyTypesInfo> loadedAssemblies = new Dictionary<String, AssemblyTypesInfo>();

			foreach(String pluginPath in this.Args.PluginPath.Where(p => Directory.Exists(p)))
			{
				foreach(AssemblyTypesInfo info in AssemblyReader.Check(pluginPath))
				{
					if(info.AssemblyName != null && loadedAssemblies.TryGetValue(info.AssemblyName.FullName, out var loadedInfo))
						this.Trace.TraceEvent(TraceEventType.Warning, 5, "Assembly {0} from path {1} already loaded from path {2}", info.AssemblyName.FullName, info.AssemblyPath, loadedInfo.AssemblyPath);
					else
					{
						loadedAssemblies.Add(info.AssemblyName.FullName, info);
						this.LoadAssembly(info, ConnectMode.Startup);
					}
				}

				foreach(String extension in FilePluginArgs.LibraryExtensions)
				{
					FileSystemWatcher watcher = new FileSystemWatcher(pluginPath, "*" + extension);
					watcher.Changed += new FileSystemEventHandler(Monitor_Changed);
					watcher.EnableRaisingEvents = true;
					this.Monitors.Add(watcher);
				}
			}
		}

		Assembly IPluginProvider.ResolveAssembly(String assemblyName)
		{
			if(String.IsNullOrEmpty(assemblyName))
				throw new ArgumentNullException(nameof(assemblyName), "Assembly name is required to resolve it");

			Int32 requestCount = _recursiveAssemblyNameCheck.AddOrUpdate(assemblyName, 1, (key, value) => value + 1);
			if(requestCount > 2)
			{//This check is used when PluginProvider tries to resolve assembly while resolving assembly with the same name. (Fixed issues while loading: System.ServiceModel.Primitives.dll and CoreWCF.Primitives.dll)
				this.Trace.TraceEvent(TraceEventType.Information, 5, "StackOverflowPrevention: Assembly {0} already requested to load", assemblyName);
				return FallBackToParent();
			}

			try
			{
				AssemblyName targetName = new AssemblyName(assemblyName);
				foreach(String pluginPath in this.Args.PluginPath.Where(p => Directory.Exists(p)))
					foreach(String file in Directory.EnumerateFiles(pluginPath, "*.*", SearchOption.AllDirectories)
						.Where(f => FilePluginArgs.CheckFileExtension(f) && !_badFilesList.Contains(f)))
						try
						{
							AssemblyName name = AssemblyName.GetAssemblyName(file);
							if(name.FullName == targetName.FullName)
								return Assembly.LoadFile(file);
							//return assembly;//TODO: Reference DLL from operating system are not working
						} catch(BadImageFormatException)
						{
							_badFilesList.Add(file);
							// Ignoring BadImageFormatException
						} catch(FileLoadException)
						{
							_badFilesList.Add(file);
							// Ignoring FileLoadException
						} catch(Exception exc)
						{
							_badFilesList.Add(file);

							exc.Data.Add("Library", file);
							this.Trace.TraceData(TraceEventType.Error, 1, exc);
						}

				this.Trace.TraceEvent(TraceEventType.Warning, 5, "Assembly {0} can't be resolved in path {1} by provider {2} (attempt {3})", assemblyName, String.Join(",", this.Args.PluginPath), this.GetType(), requestCount);
				return FallBackToParent();
			} finally
			{
				_recursiveAssemblyNameCheck.AddOrUpdate(assemblyName, 0, (key, value) => value - 1);
			}

			Assembly FallBackToParent()
			{
				this.Trace.TraceEvent(TraceEventType.Warning, 5, "The provider {2} is unable to locate the assembly {0} in the path {1}", assemblyName, String.Join(",", this.Args.PluginPath), this.GetType());
				IPluginProvider parentProvider = ((IPluginProvider)this).ParentProvider;
				return parentProvider?.ResolveAssembly(assemblyName);
			}
		}

		/// <summary>New file for check is available</summary>
		/// <param name="sender">Message sender</param>
		/// <param name="e">Event arguments</param>
		private void Monitor_Changed(Object sender, FileSystemEventArgs e)
		{
			if(e.ChangeType == WatcherChangeTypes.Changed)
			{
				AssemblyTypesInfo info = AssemblyReader.GetAssemblyTypes(e.FullPath);
				if(info != null)
					this.LoadAssembly(info, ConnectMode.AfterStartup);
			}
		}

		private void LoadAssembly(AssemblyTypesInfo info, ConnectMode mode)
		{
			if(info.Error != null)
			{
				this.Trace.TraceEvent(TraceEventType.Error, 1, "Path: {0} Error: {1}", info.AssemblyPath, info.Error);
				return;
			}
			try
			{
				if(info.Types.Length == 0)
					throw new InvalidOperationException("Types is empty");

				// We check that the plugin with this source is not yet loaded if it has already been loaded by the parent provider.
				// Loading from FS, so the source must be unique.
				foreach(IPluginDescription plugin in this.Host.Plugins)
					if(info.AssemblyPath.Equals(plugin.Source, StringComparison.InvariantCultureIgnoreCase))
						return;

				Assembly assembly = Assembly.LoadFile(info.AssemblyPath);
				foreach(String type in info.Types)
					this.Host.Plugins.LoadPlugin(assembly, type, info.AssemblyPath, mode);

			} catch(BadImageFormatException exc)//Plugin loading error. I could read the title of the file being loaded, but I'm too lazy.
			{
				exc.Data.Add("Library", info.AssemblyPath);
				this.Trace.TraceData(TraceEventType.Error, 1, exc);
			} catch(Exception exc)
			{
				exc.Data.Add("Library", info.AssemblyPath);
				this.Trace.TraceData(TraceEventType.Error, 1, exc);
			}
		}

		internal static TraceSource CreateTraceSource<T>(String name = null) where T : IPlugin
		{
			TraceSource result = new TraceSource(typeof(T).Assembly.GetName().Name + name);
			result.Switch.Level = SourceLevels.All;
			result.Listeners.Remove("Default");
			result.Listeners.AddRange(System.Diagnostics.Trace.Listeners);
			return result;
		}
	}
}