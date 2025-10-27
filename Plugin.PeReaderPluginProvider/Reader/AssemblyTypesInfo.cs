using System;
using System.Reflection;

namespace Plugin.PeReaderPluginProvider.Reader
{
	internal class AssemblyTypesInfo
	{
		public String AssemblyPath { get; }

		public AssemblyName AssemblyName { get; }

		public String Error { get; }

		public String[] Types { get; }

		private AssemblyTypesInfo(String assemblyPath)
			=> this.AssemblyPath = assemblyPath;

		public AssemblyTypesInfo(String assemblyPath, AssemblyName assemblyName, String[] types)
			: this(assemblyPath)
		{
			this.Types = types;
			this.AssemblyName = assemblyName;
		}

		public AssemblyTypesInfo(String assemblyPath, String error)
			: this(assemblyPath)//We can't use Trace from different app domain directly
			=> this.Error = error;
	}
}