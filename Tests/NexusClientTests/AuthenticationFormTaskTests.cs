namespace NexusClientTests
{
    using System.Reflection;
    using System.Runtime.Serialization;
    using Nexus.Client;
    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.ModManagement;
    using Nexus.Client.ModRepositories;
    using Nexus.Client.Settings;
    using Nexus.Client.SSO;
    using NUnit.Framework;

    /// <summary>
    /// Verifies the authentication task does not convert a logout-reset background attempt into a new interactive login.
    /// </summary>
    [TestFixture]
    public class AuthenticationFormTaskTests
    {
        /// <summary>
        /// Ensures logout/reset during token validation stops the task instead of opening the interactive login dialog.
        /// </summary>
        [Test]
        public void DoWork_WhenTokenLoginIsReset_DoesNotAttemptInteractiveLogin()
        {
            var task = new TestAuthenticationFormTask(resetDuringTokenLogin: true);

            object result = task.Execute();

            Assert.AreEqual(false, result);
            Assert.AreEqual(TaskStatus.Error, task.Status);
            Assert.IsFalse(task.InteractiveLoginAttempted);
        }

        /// <summary>
        /// Ensures ordinary token-login failure still falls back to the interactive authorization flow.
        /// </summary>
        [Test]
        public void DoWork_WhenTokenLoginFailsNormally_AttemptsInteractiveLogin()
        {
            var task = new TestAuthenticationFormTask(resetDuringTokenLogin: false);

            object result = task.Execute();

            Assert.AreEqual(true, result);
            Assert.IsTrue(task.InteractiveLoginAttempted);
        }

        /// <summary>Saved credentials survive temporary service failures and remain retryable.</summary>
        [TestCase(AuthenticationStatus.NetworkError)]
        [TestCase(AuthenticationStatus.Unknown)]
        public void DoWork_WhenSavedKeyValidationFailsTemporarily_DoesNotAuthorizeAgain(AuthenticationStatus result)
        {
            string apiKey = "test-api-key";
            int validations = 0;
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
            {
                if (method.Name == "get_ApiKey") return apiKey;
                if (method.Name == "set_ApiKey") apiKey = (string)args[0];
                return null;
            });
            IEnvironmentInfo environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
                method.Name == "get_Settings" ? settings : null);
            RepositoryUserStatus user = null;
            IModRepository repository = InterfaceStub<IModRepository>.Create((method, args) =>
            {
                if (method.Name == "Authenticate")
                {
                    validations++;
                    if (validations > 1) { user = new RepositoryUserStatus("test-user", false, false); return AuthenticationStatus.Successful; }
                    return result;
                }
                return method.Name == "get_UserStatus" ? user : null;
            });
            ModManager manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
            typeof(ModManager).GetField("<EnvironmentInfo>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, environment);
            typeof(ModManager).GetField("<ModRepository>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, repository);
            SavedKeyAuthenticationTask task = new SavedKeyAuthenticationTask(manager);

            Assert.AreEqual(false, task.Execute());
            Assert.IsFalse(task.InteractiveLoginAttempted);
            Assert.IsTrue(task.LoggedOut);
            Assert.AreEqual("test-api-key", apiKey);
            Assert.AreEqual(true, task.Execute());
            Assert.IsTrue(task.LoggedIn);
            Assert.IsFalse(task.InteractiveLoginAttempted);
        }

        /// <summary>Runs real saved-key validation while recording any interactive fallback.</summary>
        private sealed class SavedKeyAuthenticationTask : AuthenticationFormTask
        {
            /// <summary>Uses a manager with in-memory settings and repository state.</summary>
            public SavedKeyAuthenticationTask(ModManager manager) : base(manager) { }

            public bool InteractiveLoginAttempted { get; private set; }

            /// <summary>Runs one login attempt synchronously for assertions.</summary>
            public object Execute() { return DoWork(new object[0]); }

            /// <summary>Records an authorization prompt without displaying a window.</summary>
            protected override bool LoginUser() { InteractiveLoginAttempted = true; return false; }
        }

        private sealed class TestAuthenticationFormTask : AuthenticationFormTask
        {
            private readonly bool _resetDuringTokenLogin;

            public TestAuthenticationFormTask(bool resetDuringTokenLogin)
                : base(null)
            {
                _resetDuringTokenLogin = resetDuringTokenLogin;
            }

            public bool InteractiveLoginAttempted { get; private set; }

            public object Execute()
            {
                return DoWork(new object[0]);
            }

            public override bool TokenLogin()
            {
                if (_resetDuringTokenLogin)
                {
                    Reset();
                    return false;
                }

                Status = TaskStatus.Incomplete;
                return false;
            }

            protected override bool LoginUser()
            {
                InteractiveLoginAttempted = true;
                return true;
            }
        }
    }
}
