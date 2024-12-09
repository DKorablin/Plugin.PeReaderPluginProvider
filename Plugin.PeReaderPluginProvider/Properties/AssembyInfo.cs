using System.Reflection;
using System.Runtime.InteropServices;

[assembly: Guid("840d8167-ba17-4330-95c3-3b20f535da27")]
[assembly: ComVisible(false)]
[assembly: System.CLSCompliant(true)]

#if NETSTANDARD || NETCOREAPP
[assembly: AssemblyMetadata("ProjectUrl", "https://github.com/DKorablin/Plugin.PeReaderPluginProvider")]
#else

[assembly: AssemblyTitle("Plugin.PeReaderPluginProvider")]
[assembly: AssemblyProduct("Plugin loader assemblty with PE raw read before loading")]
[assembly: AssemblyCompany("Danila Korablin")]
[assembly: AssemblyCopyright("Copyright © Danila Korablin 2023-2024")]

#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif

#endif