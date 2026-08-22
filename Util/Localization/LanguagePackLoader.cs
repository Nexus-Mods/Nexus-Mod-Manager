using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Nexus.Client.Util.Localization
{
	/// <summary>
	/// Loads and validates NMM UI language packs.
	/// </summary>
	internal static class LanguagePackLoader
	{
		private const long MaximumLanguagePackSize = 16L * 1024L * 1024L;

		[DataContract]
		private sealed class LanguagePackMetadata
		{
			[DataMember(Name = "formatVersion", IsRequired = true)]
			public int FormatVersion { get; set; }

			[DataMember(Name = "id", IsRequired = true)]
			public string Id { get; set; }

			[DataMember(Name = "name", IsRequired = true)]
			public string Name { get; set; }

			[DataMember(Name = "culture", EmitDefaultValue = false)]
			public string Culture { get; set; }

			[DataMember(Name = "author", EmitDefaultValue = false)]
			public string Author { get; set; }
		}

		public static bool TryReadInfo(string filePath, out LanguagePackInfo info)
		{
			info = null;

			try
			{
				LanguagePackMetadata metadata = Deserialize<LanguagePackMetadata>(filePath);
				if (!ValidateMetadata(metadata.FormatVersion, metadata.Id, metadata.Name, filePath))
					return false;

				info = new LanguagePackInfo(
					metadata.FormatVersion,
					metadata.Id.Trim(),
					metadata.Name.Trim(),
					NormalizeOptionalMetadata(metadata.Culture),
					NormalizeOptionalMetadata(metadata.Author),
					filePath,
					false);
				return true;
			}
			catch (Exception ex)
			{
				TraceLanguagePackError(filePath, ex);
				return false;
			}
		}

		public static bool TryLoad(string filePath, out LanguagePack pack)
		{
			pack = null;

			try
			{
				LanguagePack loadedPack = Deserialize<LanguagePack>(filePath);
				if (!ValidateMetadata(loadedPack.FormatVersion, loadedPack.Id, loadedPack.Name, filePath))
					return false;

				loadedPack.Id = loadedPack.Id.Trim();
				loadedPack.Name = loadedPack.Name.Trim();
				loadedPack.Culture = NormalizeOptionalMetadata(loadedPack.Culture);
				loadedPack.Author = NormalizeOptionalMetadata(loadedPack.Author);

				Dictionary<string, string> strings = new Dictionary<string, string>(StringComparer.Ordinal);
				if (loadedPack.Strings != null)
				{
					foreach (KeyValuePair<string, string> entry in loadedPack.Strings)
					{
						if (string.IsNullOrWhiteSpace(entry.Key))
						{
							Trace.TraceWarning("Ignoring an empty localization key in language pack '{0}'.", filePath);
							continue;
						}

						if (entry.Value == null)
						{
							Trace.TraceWarning("Ignoring localization key '{0}' with a null value in language pack '{1}'.", entry.Key, filePath);
							continue;
						}

						strings[entry.Key] = entry.Value;
					}
				}

				loadedPack.Strings = strings;
				pack = loadedPack;
				return true;
			}
			catch (Exception ex)
			{
				TraceLanguagePackError(filePath, ex);
				return false;
			}
		}

		private static T Deserialize<T>(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath))
				throw new ArgumentException("A language pack path is required.", "filePath");

			FileInfo fileInfo = new FileInfo(filePath);
			if (!fileInfo.Exists)
				throw new FileNotFoundException("The language pack file does not exist.", filePath);

			if (fileInfo.Length > MaximumLanguagePackSize)
				throw new SerializationException("The language pack exceeds the maximum supported size of 16 MB.");

			DataContractJsonSerializer serializer = new DataContractJsonSerializer(
				typeof(T),
				new DataContractJsonSerializerSettings
				{
					UseSimpleDictionaryFormat = true
				});

			using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				SkipUtf8ByteOrderMark(stream);
				return (T)serializer.ReadObject(stream);
			}
		}

		private static void SkipUtf8ByteOrderMark(Stream stream)
		{
			if (stream.Length < 3)
				return;

			int firstByte = stream.ReadByte();
			int secondByte = stream.ReadByte();
			int thirdByte = stream.ReadByte();

			if (firstByte != 0xEF || secondByte != 0xBB || thirdByte != 0xBF)
				stream.Position = 0;
		}

		private static string NormalizeOptionalMetadata(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		}

		private static bool ValidateMetadata(int formatVersion, string id, string name, string filePath)
		{
			if (formatVersion != LanguageManager.CurrentPackFormatVersion)
			{
				Trace.TraceWarning(
					"Ignoring language pack '{0}': unsupported format version {1} (expected {2}).",
					filePath,
					formatVersion,
					LanguageManager.CurrentPackFormatVersion);
				return false;
			}

			if (string.IsNullOrWhiteSpace(id))
			{
				Trace.TraceWarning("Ignoring language pack '{0}': missing language id.", filePath);
				return false;
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				Trace.TraceWarning("Ignoring language pack '{0}': missing language name.", filePath);
				return false;
			}

			return true;
		}

		private static void TraceLanguagePackError(string filePath, Exception exception)
		{
			Trace.TraceWarning(
				"Unable to load NMM language pack '{0}'. The pack will be ignored. {1}",
				filePath ?? string.Empty,
				exception.Message);
		}
	}
}
