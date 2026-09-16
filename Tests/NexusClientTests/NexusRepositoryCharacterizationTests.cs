namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Reflection;
	using System.Runtime.Remoting.Messaging;
	using System.Runtime.Remoting.Proxies;
	using System.Runtime.Serialization;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModRepositories;
	using Nexus.Client.OnlineServices.NexusMods.V1;
	using Nexus.Client.Mods;
	using NUnit.Framework;

	/// <summary>
	/// Characterizes the Nexus repository behavior that must survive the API-layer replacement.
	/// </summary>
	public class NexusRepositoryCharacterizationTests
	{
		/// <summary>
		/// Ensures packed update-check metadata keeps its current mod, file and filename field semantics.
		/// </summary>
		[Test]
		public void PackedRepositoryMetadata_ParsersPreserveCurrentFields()
		{
			var repository = CreateRepository();
			const string packed = "Display Name|mod-123|file-456|archive-name.7z";

			Assert.AreEqual("123", InvokePrivate<string>(repository, "ParseModId", packed));
			Assert.AreEqual("456", InvokePrivate<string>(repository, "ParseDownloadId", packed));
			Assert.AreEqual("archive-name.7z", InvokePrivate<string>(repository, "ParseFilename", packed));
		}

		/// <summary>
		/// Ensures the current Nexus archive naming convention extracts the mod ID located before the file ID.
		/// </summary>
		[Test]
		public void NewNexusArchiveName_ExtractsModIdBeforeFileId()
		{
			var args = new object[]
			{
				@"C:\Mods\Example Mod 12345 67890 2026-09-16T10-30Z.7z",
				null
			};

			var parsed = (bool)InvokePrivateStatic("TryParseNewNexusArchiveModIdFromFilename", args);

			Assert.IsTrue(parsed);
			Assert.AreEqual("12345", args[1]);
		}

		/// <summary>
		/// Ensures filenames without the expected Nexus timestamp are not treated as the new archive format.
		/// </summary>
		[Test]
		public void NewNexusArchiveName_InvalidTimestampIsNotRecognized()
		{
			var args = new object[]
			{
				@"C:\Mods\Example Mod 12345 67890 2026-09-16T10_30Z.7z",
				null
			};

			var parsed = (bool)InvokePrivateStatic("TryParseNewNexusArchiveModIdFromFilename", args);

			Assert.IsFalse(parsed);
			Assert.IsNull(args[1]);
		}

		/// <summary>
		/// Ensures NMM-facing repository contracts expose only NMM-owned models.
		/// </summary>
		[Test]
		public void RepositoryContracts_DoNotExposeProtocolModels()
		{
			Assert.AreEqual(typeof(RepositoryUserStatus), typeof(IModRepository).GetProperty("UserStatus").PropertyType);
			Assert.AreEqual(typeof(RepositoryRateLimit), typeof(IModRepository).GetProperty("RateLimit").PropertyType);
			Assert.AreEqual(typeof(List<RepositoryDownloadLink>), typeof(IModRepository).GetMethod("GetFilePartInfo").ReturnType);
			Assert.AreEqual(typeof(RepositoryRateLimit), typeof(RateLimitExceededArgs).GetProperty("RateLimit").PropertyType);

			var exposedConstructors = typeof(ModInfo).GetConstructors()
				.Concat(typeof(ModFileInfo).GetConstructors())
				.Concat(typeof(CategoriesInfo).GetConstructors());

			Assert.IsFalse(exposedConstructors
				.SelectMany(constructor => constructor.GetParameters())
				.Any(parameter => parameter.ParameterType.Namespace != null && parameter.ParameterType.Namespace.StartsWith("Nexus.Client.OnlineServices.NexusMods.V1", StringComparison.Ordinal)));
		}

		/// <summary>
		/// Ensures a hash match with file metadata uses the Nexus file identity and version over the parent mod version.
		/// </summary>
		[Test]
		public void HashResult_WithFileUsesFileIdentityAndVersion()
		{
			var result = InvokeCreateModInfoFromHashResult(true);

			Assert.AreEqual("100", result.Id);
			Assert.AreEqual("200", result.DownloadId);
			Assert.AreEqual("specific-file.7z", result.FileName);
			Assert.AreEqual("1.2", result.HumanReadableVersion);
			Assert.AreEqual("1.2", result.LastKnownVersion);
			Assert.IsNull(result.MachineVersion);
		}

		/// <summary>
		/// Ensures a hash match that identifies only the mod does not incorrectly retain the mod-page version as file metadata.
		/// </summary>
		[Test]
		public void HashResult_WithoutFileClearsVersionFields()
		{
			var result = InvokeCreateModInfoFromHashResult(false);

			Assert.AreEqual("100", result.Id);
			Assert.IsNull(result.DownloadId);
			Assert.IsNull(result.HumanReadableVersion);
			Assert.IsNull(result.LastKnownVersion);
			Assert.IsNull(result.MachineVersion);
		}

		/// <summary>
		/// Ensures update checks continue following Nexus file-update relationships to the newest reachable file.
		/// </summary>
		[Test]
		public void FileUpdateChain_FollowsLatestReachableFile()
		{
			var updates = new[]
			{
				new NexusV1ModFileUpdate { OldFileId = 100, NewFileId = 200 },
				new NexusV1ModFileUpdate { OldFileId = 200, NewFileId = 300 }
			};

			Assert.AreEqual(300, InvokePrivateStatic("ResolveLatestFileId", 100, updates));
		}

		/// <summary>
		/// Ensures exact file recognition is considered complete and does not trigger another file-list lookup.
		/// </summary>
		[Test]
		public void AutoTagger_ExactFileRecognitionDoesNotExpandFileList()
		{
			var behavior = new RepositoryBehavior
			{
				FileRecognitionResult = new ModInfo
				{
					Id = "100",
					DownloadId = "200",
					FileName = "specific-file.7z",
					HumanReadableVersion = "1.2"
				}
			};
			var tagger = new AutoTagger(behavior.CreateRepository());

			var candidates = tagger.GetTagInfoCandidates(CreateMod("100")).ToList();

			Assert.AreEqual(1, candidates.Count);
			Assert.AreEqual("200", candidates[0].DownloadId);
			Assert.AreEqual(1, behavior.GetModInfoForFileCalls);
			Assert.AreEqual(0, behavior.GetModInfoCalls);
			Assert.AreEqual(0, behavior.GetModFileInfoCalls);
		}

		/// <summary>
		/// Ensures mod-only recognition expands file candidates and applies file-specific version metadata.
		/// </summary>
		[Test]
		public void AutoTagger_ModOnlyRecognitionExpandsFileCandidates()
		{
			var behavior = new RepositoryBehavior
			{
				FileRecognitionResult = new ModInfo
				{
					Id = "100",
					ModName = "Parent Mod",
					HumanReadableVersion = "9.0",
					LastKnownVersion = "9.0",
					MachineVersion = new Version(9, 0)
				},
				Files = new List<IModFileInfo>
				{
					new ModFileInfo("200", "specific-file.7z", "Specific File", "1.2")
				}
			};
			var tagger = new AutoTagger(behavior.CreateRepository());

			var candidates = tagger.GetTagInfoCandidates(CreateMod("100")).ToList();

			Assert.AreEqual(1, candidates.Count);
			Assert.AreEqual("200", candidates[0].DownloadId);
			Assert.AreEqual("specific-file.7z", candidates[0].FileName);
			Assert.AreEqual("1.2", candidates[0].HumanReadableVersion);
			Assert.AreEqual("1.2", candidates[0].LastKnownVersion);
			Assert.IsNull(candidates[0].MachineVersion);
			Assert.AreEqual(1, behavior.GetModFileInfoCalls);
		}

		/// <summary>
		/// Ensures the locally stored Nexus mod ID remains the final fallback when archive recognition cannot identify the mod.
		/// </summary>
		[Test]
		public void AutoTagger_StoredModIdRemainsFinalRecognitionFallback()
		{
			var behavior = new RepositoryBehavior
			{
				StoredModResult = new ModInfo
				{
					Id = "100",
					ModName = "Stored Mod"
				},
				Files = null
			};
			var tagger = new AutoTagger(behavior.CreateRepository());

			var candidates = tagger.GetTagInfoCandidates(CreateMod("100")).ToList();

			Assert.AreEqual(1, candidates.Count);
			Assert.AreEqual("100", candidates[0].Id);
			Assert.AreEqual(1, behavior.GetModInfoForFileCalls);
			Assert.AreEqual(1, behavior.GetModInfoCalls);
			Assert.AreEqual("100", behavior.LastRequestedModId);
			Assert.AreEqual(1, behavior.GetModFileInfoCalls);
		}

		private static NexusModsApiRepository CreateRepository()
		{
			return new NexusModsApiRepository("skyrim", null);
		}

		private static T InvokePrivate<T>(object instance, string methodName, params object[] args)
		{
			return (T)GetPrivateMethod(methodName, false).Invoke(instance, args);
		}

		private static object InvokePrivateStatic(string methodName, params object[] args)
		{
			return GetPrivateMethod(methodName, true).Invoke(null, args);
		}

		private static MethodInfo GetPrivateMethod(string methodName, bool isStatic)
		{
			var flags = BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
			var method = typeof(NexusModsApiRepository).GetMethod(methodName, flags);
			Assert.IsNotNull(method, "Expected repository method {0} was not found.", methodName);
			return method;
		}

		private static IModInfo InvokeCreateModInfoFromHashResult(bool includeFile)
		{
			var method = GetPrivateMethod("CreateModInfoFromHashResult", true);
			var hashResultType = method.GetParameters()[0].ParameterType;
			var hashResult = CreateRuntimeObject(hashResultType);
			var mod = CreateRuntimeObject(GetMemberType(hashResultType, "Mod"));

			SetMember(mod, "ModId", 100);
			SetMember(mod, "Name", "Parent Mod");
			SetMember(mod, "Version", "9.0");
			SetMember(mod, "Author", "Author");
			SetMember(mod, "CategoryId", 1);
			SetMember(mod, "Description", "Description");
			SetMember(mod, "DomainName", "skyrim");
			SetMember(hashResult, "Mod", mod);

			if (includeFile)
			{
				var file = CreateRuntimeObject(GetMemberType(hashResultType, "File"));
				SetMember(file, "FileId", 200);
				SetMember(file, "FileName", "specific-file.7z");
				SetMember(file, "Name", "Specific File");
				SetMember(file, "ModVersion", "1.2");
				SetMember(hashResult, "File", file);
			}

			return (IModInfo)method.Invoke(null, new[] { hashResult });
		}

		private static Type GetMemberType(Type type, string memberName)
		{
			var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (property != null)
			{
				return property.PropertyType;
			}

			var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
				type.GetField("<" + memberName + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.IsNotNull(field, "Expected member {0}.{1} was not found.", type.FullName, memberName);
			return field.FieldType;
		}

		private static object CreateRuntimeObject(Type type)
		{
			return type.IsValueType
				? Activator.CreateInstance(type)
				: FormatterServices.GetUninitializedObject(type);
		}

		private static void SetMember(object target, string memberName, object value)
		{
			var type = target.GetType();
			var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			var setter = property?.GetSetMethod(true);
			if (setter != null)
			{
				setter.Invoke(target, new[] { ConvertValue(value, property.PropertyType) });
				return;
			}

			var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
				type.GetField("<" + memberName + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.IsNotNull(field, "Expected writable member {0}.{1} was not found.", type.FullName, memberName);
			field.SetValue(target, ConvertValue(value, field.FieldType));
		}

		private static object ConvertValue(object value, Type destinationType)
		{
			if (value == null || destinationType.IsInstanceOfType(value))
			{
				return value;
			}

			var targetType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
			return Convert.ChangeType(value, targetType);
		}

		private static IMod CreateMod(string storedModId)
		{
			return new InterfaceProxy<IMod>(call =>
			{
				switch (call.MethodName)
				{
					case "get_ModArchivePath":
						return @"C:\Mods\archive.7z";
					case "get_Id":
						return storedModId;
					default:
						return GetDefaultValue(((MethodInfo)call.MethodBase).ReturnType);
				}
			}).Object;
		}

		private static object GetDefaultValue(Type type)
		{
			return type == typeof(void) || !type.IsValueType
				? null
				: Activator.CreateInstance(type);
		}

		private sealed class RepositoryBehavior
		{
			public IModInfo FileRecognitionResult { get; set; }

			public IModInfo StoredModResult { get; set; }

			public IList<IModFileInfo> Files { get; set; }

			public int GetModInfoForFileCalls { get; private set; }

			public int GetModInfoCalls { get; private set; }

			public int GetModFileInfoCalls { get; private set; }

			public string LastRequestedModId { get; private set; }

			public IModRepository CreateRepository()
			{
				return new InterfaceProxy<IModRepository>(Invoke).Object;
			}

			private object Invoke(IMethodCallMessage call)
			{
				switch (call.MethodName)
				{
					case "GetModInfoForFile":
						GetModInfoForFileCalls++;
						return FileRecognitionResult;
					case "GetModInfo":
						GetModInfoCalls++;
						LastRequestedModId = call.Args[0] as string;
						return StoredModResult;
					case "GetModFileInfo":
						GetModFileInfoCalls++;
						return Files;
					case "get_GameDomainName":
						return "skyrim";
					default:
						return GetDefaultValue(((MethodInfo)call.MethodBase).ReturnType);
				}
			}
		}

		private sealed class InterfaceProxy<T> : RealProxy where T : class
		{
			private readonly Func<IMethodCallMessage, object> _handler;

			public InterfaceProxy(Func<IMethodCallMessage, object> handler)
				: base(typeof(T))
			{
				_handler = handler;
			}

			public T Object => (T)GetTransparentProxy();

			public override IMessage Invoke(IMessage msg)
			{
				var call = (IMethodCallMessage)msg;
				try
				{
					var result = _handler(call);
					return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
				}
				catch (Exception ex)
				{
					return new ReturnMessage(ex, call);
				}
			}
		}
	}
}
