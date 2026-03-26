using System;

namespace Plugin.PeReaderPluginProvider
{
	/// <summary>The list of plugin constant values.</summary>
	internal static class Constants
	{
		/// <summary>
		/// Sometimes, I don't know why yet, ResolveAssembly requesting the same assembly many times.
		/// But also if we have circular references between assemblies (For example if references assembly is missed for current plugin), we can get infinite recursion.
		/// </summary>
		public const Int32 MaxRecursiveAssemblyResolve = 5;
	}
}