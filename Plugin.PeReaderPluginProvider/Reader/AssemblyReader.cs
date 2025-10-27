using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using AlphaOmega.Debug;
using AlphaOmega.Debug.CorDirectory.Meta.Tables;
using Plugin.FilePluginProvider;
using SAL.Flatbed;

namespace Plugin.PeReaderPluginProvider.Reader
{
	internal class AssemblyReader
	{
		private static readonly Type PluginType = typeof(IPlugin);

		private String[] FilePath { get; }

		private ManualResetEvent OnDone { get; set; }

		public AssemblyTypesInfo[] Info { get; private set; }

		public static IEnumerable<AssemblyTypesInfo> Check(String path)
		{
			List<ManualResetEvent> onDone = new List<ManualResetEvent>();
			List<AssemblyReader> readers = new List<AssemblyReader>();

			foreach(String filePath in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
				.Where(p => FilePluginArgs.CheckFileExtension(p)))
				{
					ManualResetEvent evt = new ManualResetEvent(false);
					AssemblyReader reader = new AssemblyReader(new String[] { filePath }, evt);
					onDone.Add(evt);
					readers.Add(reader);

					ThreadPool.QueueUserWorkItem(reader.Read);
				}

			foreach(ManualResetEvent evt in onDone)
				evt.WaitOne();

			foreach(AssemblyReader reader in readers)
				foreach(AssemblyTypesInfo info in reader.Info)
				{
					// reader.OnDone.WaitOne()
					if(info != null)
						yield return info;
				}
		}

		protected AssemblyReader(String[] filePath, ManualResetEvent onDone)
		{
			this.FilePath = filePath;
			this.Info = new AssemblyTypesInfo[filePath.Length];
			this.OnDone = onDone;
		}

		public void Read(Object threadContext)
		{
			this.Info = Array.ConvertAll(this.FilePath, (p) => { return GetAssemblyTypes(p); });
			this.OnDone.Set();
		}

		public static AssemblyTypesInfo GetAssemblyTypes(String filePath)
		{
			List<String> types = new List<String>();
			AssemblyName assemblyName = null;

			try
			{
				using(PEFile file = new PEFile(filePath, StreamLoader.FromFile(filePath)))
					if(file.Header.IsValid)
					{
						var tables = file.ComDescriptor?.MetaData?.StreamTables;
						if(tables != null)
						{
							AssemblyRefRow assemblyRef = tables.AssemblyRef.FirstOrDefault(a => a.AssemblyName.FullName == PluginType.Assembly.FullName);
							if(assemblyRef != null)
							{
								foreach(InterfaceImplRow interfaceImpl in tables.InterfaceImpl)
									if(interfaceImpl.Interface.RowIndex != null)
									{
										TypeRefRow typeRef = tables.TypeRef[interfaceImpl.Interface.RowIndex.Value];
										String typeName = typeRef.TypeNamespace + "." + typeRef.TypeName;
										if(typeName == PluginType.FullName && typeRef.ResolutionScope.RowIndex == assemblyRef.Index)
										{
											TypeDefRow typeDef = interfaceImpl.Class;
											if(typeDef.VisibilityMask == TypeAttributes.Public
												&& typeDef.ClassSemanticsMask == TypeAttributes.AnsiClass)
												types.Add(typeDef.TypeNamespace + "." + typeDef.TypeName);
										}
									}

								if(types.Count > 0)
									assemblyName = tables.Assembly.First().AssemblyName;
							}
						}
					}
			} catch(Exception exc)
			{
				Exception exc1 = exc?.InnerException;
				return new AssemblyTypesInfo(filePath, exc1.Message);
			}

			return types.Count == 0
				? null
				: new AssemblyTypesInfo(filePath, assemblyName, types.ToArray());
		}
	}
}