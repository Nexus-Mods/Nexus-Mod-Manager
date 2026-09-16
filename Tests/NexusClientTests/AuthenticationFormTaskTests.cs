namespace NexusClientTests
{
    using Nexus.Client.BackgroundTasks;
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
