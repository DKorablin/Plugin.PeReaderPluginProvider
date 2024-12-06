using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Plugin.FilePluginProvider;
using Plugin.PeReaderPluginProvider.Reader;
using SAL.Flatbed;

namespace Plugin.PeReaderPluginProvider
{
	/// <summary>Plugins loader from file system but it's using separate sandbox to find appropriate assemblies to load</summary>
	public class Plugin : IPluginProvider
	{
		private HashSet<String> _recursiveAssemblyNameCheck = new HashSet<String>();
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
			//System.Diagnostics.Debugger.Launch();
			foreach(String pluginPath in this.Args.PluginPath)
				if(Directory.Exists(pluginPath))
				{
					foreach(AssemblyTypesInfo info in AssemblyReader.Check(pluginPath))
						this.LoadAssembly(info, ConnectMode.Startup);

					FileSystemWatcher watcher = new FileSystemWatcher(pluginPath, Constant.LibrarySearchExtension);
					watcher.Changed += new FileSystemEventHandler(Monitor_Changed);
					watcher.EnableRaisingEvents = true;
					this.Monitors.Add(watcher);
				}
		}

		Assembly IPluginProvider.ResolveAssembly(String assemblyName)
		{
			if(String.IsNullOrEmpty(assemblyName))
				throw new ArgumentNullException(nameof(assemblyName), "Assembly name is required to resolve it");

			Boolean isSuccess = this._recursiveAssemblyNameCheck.Add(assemblyName);
			if(isSuccess)
			{//This check is used when PluginProvied tries to resolve assembly while resolving assembly with the same name
				AssemblyName targetName = new AssemblyName(assemblyName);
				foreach(String pluginPath in this.Args.PluginPath)
					if(Directory.Exists(pluginPath))
						foreach(String file in Directory.GetFiles(pluginPath, Constant.LibrarySearchExtension, SearchOption.AllDirectories))//Поиск только файлов с расширением .dll
							try
							{
								AssemblyName name = AssemblyName.GetAssemblyName(file);
								if(name.FullName == targetName.FullName)
									return Assembly.LoadFile(file);
								//return assembly;//TODO: Reference DLL из оперативной памяти не цепляются!
							} catch(BadImageFormatException)
							{
								continue;
							} catch(FileLoadException)
							{
								continue;
							} catch(Exception exc)
							{
								exc.Data.Add("Library", file);
								this.Trace.TraceData(TraceEventType.Error, 1, exc);
							}
				this._recursiveAssemblyNameCheck.Remove(assemblyName);
				this.Trace.TraceEvent(TraceEventType.Warning, 5, "Assembly {0} can't be resolved in path {1} by provider {2}", assemblyName, String.Join(",", this.Args.PluginPath), this.GetType());
			} else
				this.Trace.TraceEvent(TraceEventType.Information, 5, "StackOverflowPrevention: Assembly {0} already requested to load", assemblyName);

			IPluginProvider parentProvider = ((IPluginProvider)this).ParentProvider;
			return parentProvider == null
				? null
				: parentProvider.ResolveAssembly(assemblyName);
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

				// Проверяем что плагин с таким источником ещё не загружен, если его уже загрузил родительский провайдер.
				// Загрузка из ФС так что источник должен быть по любому уникальный.
				foreach(IPluginDescription plugin in this.Host.Plugins)
					if(info.AssemblyPath.Equals(plugin.Source, StringComparison.InvariantCultureIgnoreCase))
						return;

				Assembly assembly = Assembly.LoadFile(info.AssemblyPath);
				foreach(String type in info.Types)
					this.Host.Plugins.LoadPlugin(assembly, type, info.AssemblyPath, mode);

			} catch(BadImageFormatException exc)//Ошибка загрузки плагина. Можно почитать заголовок загружаемого файла, но мне влом
			{
				exc.Data.Add("Library", info.AssemblyPath);
				this.Trace.TraceData(TraceEventType.Error, 1, exc);
				return;
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