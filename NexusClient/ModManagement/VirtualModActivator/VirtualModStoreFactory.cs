namespace Nexus.Client.ModManagement
{
	using System;
	using System.Diagnostics;
	using System.IO;

	internal sealed class VirtualModStoreFactory : IVirtualModStoreFactory
	{
		public IVirtualModStore Create(string xmlFilePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion)
		{
			XmlVirtualModStore xmlStore = new XmlVirtualModStore();
			string sqlitePath = SQLiteVirtualModStore.GetDefaultDatabasePath(xmlFilePath);

			try
			{
				if (File.Exists(sqlitePath))
				{
					SQLiteVirtualModStore sqliteStore = new SQLiteVirtualModStore(xmlFilePath, sqlitePath, currentVersion, isValidVersion, xmlStore);
					if (sqliteStore.CanLoadExistingStore())
						return sqliteStore;

					Trace.TraceWarning("Existing SQLite virtual mod store failed validation. Falling back to XML store: {0}", sqlitePath);
					return xmlStore;
				}

				if (TryMigrateXmlStore(xmlStore, xmlFilePath, sqlitePath, currentVersion, isValidVersion, getMissingFileVersion))
				{
					SQLiteVirtualModStore sqliteStore = new SQLiteVirtualModStore(xmlFilePath, sqlitePath, currentVersion, isValidVersion, xmlStore);
					sqliteStore.Initialize();
					return sqliteStore;
				}
			}
			catch (Exception e)
			{
				Trace.TraceWarning("Could not initialize SQLite virtual mod store. Falling back to XML store: {0}", e.Message);
			}

			return xmlStore;
		}

		private static bool TryMigrateXmlStore(XmlVirtualModStore xmlStore, string xmlFilePath, string sqlitePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion)
		{
			string tempSqlitePath = sqlitePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

			try
			{
				string directory = Path.GetDirectoryName(Path.GetFullPath(sqlitePath));
				if (!Directory.Exists(directory))
					Directory.CreateDirectory(directory);

				SQLiteVirtualModStore sqliteStore = new SQLiteVirtualModStore(xmlFilePath, tempSqlitePath, currentVersion, isValidVersion, xmlStore);
				sqliteStore.Initialize();

				VirtualModStoreData data;
				if (File.Exists(xmlFilePath))
					data = xmlStore.Load(xmlFilePath, currentVersion, isValidVersion, getMissingFileVersion);
				else
					data = new VirtualModStoreData(new System.Collections.Generic.List<IVirtualModInfo>(), new System.Collections.Generic.List<IVirtualModLink>());

				sqliteStore.SaveStoreData(currentVersion, data);
				File.Move(tempSqlitePath, sqlitePath);
				return true;
			}
			catch (Exception e)
			{
				Trace.TraceWarning("Could not migrate virtual mod XML store to SQLite. XML store will remain active: {0}", e.Message);
				DeleteTempDatabase(tempSqlitePath);
				return false;
			}
		}

		private static void DeleteTempDatabase(string path)
		{
			try
			{
				if (File.Exists(path))
					File.Delete(path);
			}
			catch (Exception e)
			{
				Trace.TraceWarning("Could not delete temporary virtual mod SQLite database \"{0}\": {1}", path, e.Message);
			}
		}
	}
}
