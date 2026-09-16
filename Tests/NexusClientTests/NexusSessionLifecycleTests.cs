namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Reflection;
    using Nexus.Client;
    using Nexus.Client.ModRepositories;
    using Nexus.Client.OnlineServices.NexusMods;
    using Nexus.Client.OnlineServices.NexusMods.V1;
    using Nexus.Client.Settings;
    using NUnit.Framework;

    /// <summary>
    /// Verifies the NMM-owned Nexus credential and session lifecycle used by repository API calls.
    /// </summary>
    [TestFixture]
    public class NexusSessionLifecycleTests
    {
        /// <summary>
        /// Ensures credential replacement invalidates the previous session while logout clears owned authentication state.
        /// </summary>
        [Test]
        public void CredentialReplacementAndLogout_UpdateOwnedSessionState()
        {
            string apiKey = "first-api-key";
            int saveCount = 0;
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_ApiKey":
                        return apiKey;
                    case "set_ApiKey":
                        apiKey = (string)args[0];
                        return null;
                    case "Save":
                        saveCount++;
                        return null;
                    default:
                        return null;
                }
            });
            IEnvironmentInfo environmentInfo = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
                method.Name == "get_Settings" ? settings : null);

            ApiCallManager manager = CreateManager(environmentInfo);
            NexusModsService service = GetOwnedService(manager);
            try
            {
                NexusSessionContext firstSession = service.CaptureSession();
                Assert.AreEqual(NexusCredentialKind.ApiKey, firstSession.Credentials.Kind);
                Assert.AreEqual("first-api-key", GetRequestApiKey(service, firstSession));
                Assert.IsNotNull(GetOwnedV1Client(manager));

                settings.ApiKey = "replacement-api-key";
                manager.UpdateNexusClient();

                NexusSessionContext replacementSession = service.CaptureSession();
                Assert.Greater(replacementSession.Generation, firstSession.Generation);
                Assert.IsTrue(firstSession.Token.IsCancellationRequested);
                Assert.AreEqual(NexusCredentialKind.ApiKey, replacementSession.Credentials.Kind);
                Assert.AreEqual("replacement-api-key", GetRequestApiKey(service, replacementSession));
                Assert.IsNotNull(GetOwnedV1Client(manager));

                manager.ClearApiKey();

                NexusSessionContext loggedOutSession = service.CaptureSession();
                Assert.Greater(loggedOutSession.Generation, replacementSession.Generation);
                Assert.IsTrue(replacementSession.Token.IsCancellationRequested);
                Assert.AreEqual(NexusCredentialKind.None, loggedOutSession.Credentials.Kind);
                Assert.IsNull(GetRequestApiKey(service, loggedOutSession));
                Assert.IsNull(GetOwnedV1Client(manager));
                Assert.AreEqual(string.Empty, settings.ApiKey);
                Assert.AreEqual(1, saveCount);
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>
        /// Creates an isolated API-call manager without touching its process-wide singleton instance.
        /// </summary>
        private static ApiCallManager CreateManager(IEnvironmentInfo environmentInfo)
        {
            ConstructorInfo constructor = typeof(ApiCallManager).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(IEnvironmentInfo) },
                null);
            Assert.IsNotNull(constructor);
            return (ApiCallManager)constructor.Invoke(new object[] { environmentInfo });
        }

        /// <summary>
        /// Reads the internal owned service used for future REST v1 migration without widening the production API surface.
        /// </summary>
        private static NexusModsService GetOwnedService(ApiCallManager manager)
        {
            PropertyInfo property = typeof(ApiCallManager).GetProperty("NexusService", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(property);
            return (NexusModsService)property.GetValue(manager, null);
        }

        /// <summary>
        /// Reads the owned v1 compatibility boundary without widening its production visibility.
        /// </summary>
        private static NexusV1Client GetOwnedV1Client(ApiCallManager manager)
        {
            PropertyInfo property = typeof(ApiCallManager).GetProperty("V1", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(property);
            return (NexusV1Client)property.GetValue(manager, null);
        }

        /// <summary>
        /// Materializes one approved request so the effective per-request API key can be compared without performing network I/O.
        /// </summary>
        private static string GetRequestApiKey(NexusModsService service, NexusSessionContext session)
        {
            using (HttpRequestMessage request = service.RequestPolicy.CreateRequest(
                HttpMethod.Get,
                new Uri("https://api.nexusmods.com/v1/games/skyrim.json"),
                session.Credentials))
            {
                return request.Headers.TryGetValues("apikey", out IEnumerable<string> values)
                    ? string.Join(",", values)
                    : null;
            }
        }
    }
}
