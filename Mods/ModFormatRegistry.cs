using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.Mods
{
	/// <summary>
	/// A registry of all supported mod formats.
	/// </summary>
	public class ModFormatRegistry : IModFormatRegistry
	{
		/// <summary>
		/// Searches for mod format assemblies in the specified path, and loads
		/// any mod formats that are found into a registry.
		/// </summary>
		/// <remarks>
		/// A mod format is loaded if the class implements <see cref="IModFormat"/> and is
		/// not abstract. Once loaded, the format is added to the registry.
		/// </remarks>
		/// <param name="p_mcmModCacheManager">The manager being used to manage mod caches.</param>
		/// <param name="p_stgScriptTypeRegistry">The registry listing the supported script types.</param>
		/// <param name="p_strSearchPath">The path in which to search for mod format assemblies.</param>
		/// <returns>A registry containing all of the discovered mod formats.</returns>
		public static IModFormatRegistry DiscoverFormats(IModCacheManager p_mcmModCacheManager, IScriptTypeRegistry p_stgScriptTypeRegistry, string p_strSearchPath)
		{
			Trace.TraceInformation("Discovering Mod Formats...");
			Trace.Indent();

			Trace.TraceInformation("Looking in: {0}", p_strSearchPath);

			IModFormatRegistry mfrRegistry = new ModFormatRegistry();
			if (!Directory.Exists(p_strSearchPath))
			{
				Trace.TraceError("Format search path does not exist.");
				Trace.Unindent();
				return mfrRegistry;
			}

			string[] strAssemblies = Directory.GetFiles(p_strSearchPath, "*.dll");
			foreach (string strAssembly in strAssemblies)
			{
				Trace.TraceInformation("Checking: {0}", Path.GetFileName(strAssembly));
				Trace.Indent();

				Assembly asmGameMode = Assembly.LoadFile(strAssembly);
				Type[] tpeTypes = asmGameMode.GetExportedTypes();
				foreach (Type tpeType in tpeTypes)
				{
					if (typeof(IModFormat).IsAssignableFrom(tpeType) && !tpeType.IsAbstract)
					{
						Trace.TraceInformation("Initializing: {0}", tpeType.FullName);
						Trace.Indent();

						ConstructorInfo cifConstructor = tpeType.GetConstructor(new Type[] { typeof(IModCacheManager), typeof(IScriptTypeRegistry) });
						IModFormat mftModFormat = null;
						if (cifConstructor != null)
							mftModFormat = (IModFormat)cifConstructor.Invoke(new object[] { p_mcmModCacheManager, p_stgScriptTypeRegistry });
						else
						{
							cifConstructor = tpeType.GetConstructor(new Type[] { });
							if (cifConstructor != null)
								mftModFormat = (IModFormat)cifConstructor.Invoke(null);
						}
						if (mftModFormat != null)
							mfrRegistry.RegisterFormat(mftModFormat);

						Trace.Unindent();
					}
				}
				Trace.Unindent();
			}
			Trace.Unindent();
			return mfrRegistry;
		}

		#region Properties

		/// <summary>
		/// Gets or sets the list of registered <see cref="IModFormat"/>s.
		/// </summary>
		/// <value>The list of registered <see cref="IModFormat"/>s.</value>
		protected Dictionary<string, IModFormat> ModFormats { get; set; }

		/// <summary>
		/// Gets the registered <see cref="IModFormat"/>s.
		/// </summary>
		/// <value>The registered <see cref="IModFormat"/>s.</value>
		public ICollection<IModFormat> Formats
		{
			get
			{
				return ModFormats.Values;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ModFormatRegistry()
		{
			ModFormats = new Dictionary<string, IModFormat>();
		}

		#endregion

		/// <summary>
		/// Registers the given <see cref="IModFormat"/>.
		/// </summary>
		/// <param name="p_mftFormat">A <see cref="IModFormat"/> to register.</param>
		public void RegisterFormat(IModFormat p_mftFormat)
		{
			ModFormats[p_mftFormat.Id] = p_mftFormat;
		}

		/// <summary>
		/// Gets the specified <see cref="IModFormat"/>.
		/// </summary>
		/// <param name="p_strModFormatId">The id of the <see cref="IModFormat"/> to retrieve.</param>
		/// <returns>The <see cref="IModFormat"/> whose id matches the given id. <c>null</c> is returned
		/// if no <see cref="IModFormat"/> with the given id is in the registry.</returns>
		public IModFormat GetFormat(string p_strModFormatId)
		{
			IModFormat mftFormat = null;
			ModFormats.TryGetValue(p_strModFormatId, out mftFormat);
			return mftFormat;
		}
	}
}
