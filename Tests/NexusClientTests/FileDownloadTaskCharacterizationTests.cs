using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.DownloadManagement;
using Nexus.Client.ModRepositories;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Covers the shared downloader mirror/retry boundaries required before collection-scale acquisition.
	/// </summary>
	[TestFixture]
	public class FileDownloadTaskCharacterizationTests
	{
		/// <summary>
		/// Empty mirror input terminates through the task contract rather than indexing past the list.
		/// </summary>
		[Test]
		public void DownloadAsync_EmptyMirrorList_EndsAsErrorWithoutThrowing()
		{
			using (FileDownloadTask task = CreateTask())
			{
				int ended = 0;
				task.TaskEnded += (sender, args) => Interlocked.Increment(ref ended);

				task.DownloadAsync(new List<Uri>(), Path.GetTempPath(), true);

				Assert.AreEqual(TaskStatus.Error, task.Status);
				Assert.AreEqual(1, ended);
			}
		}

		/// <summary>
		/// A missing only mirror terminates normally without indexing beyond the mirror list.
		/// </summary>
		[Test]
		public void DownloadAsync_SingleMissingMirror_EndsAsErrorAtMirrorBoundary()
		{
			using (LoopbackHttpServer server = new LoopbackHttpServer(HttpStatusCode.NotFound))
			using (FileDownloadTask task = CreateTask())
			{
				SetPrivateInt(task, "m_intRetryInterval", 1);
				int ended = 0;
				task.TaskEnded += (sender, args) => Interlocked.Increment(ref ended);

				task.DownloadAsync(new List<Uri> { server.Uri }, Path.GetTempPath(), true);

				Assert.AreEqual(TaskStatus.Error, task.Status);
				Assert.AreEqual(1, server.RequestCount);
				Assert.AreEqual(1, ended);
			}
		}

		/// <summary>
		/// Exhausting all missing mirrors produces one bounded terminal result.
		/// </summary>
		[Test]
		public void DownloadAsync_AllMirrorsMissing_EndsAfterFinalMirror()
		{
			using (LoopbackHttpServer server = new LoopbackHttpServer(HttpStatusCode.NotFound))
			using (FileDownloadTask task = CreateTask())
			{
				SetPrivateInt(task, "m_intRetryInterval", 1);
				var mirrors = new List<Uri> { server.Uri, server.Uri };
				int ended = 0;
				task.TaskEnded += (sender, args) => Interlocked.Increment(ref ended);

				task.DownloadAsync(mirrors, Path.GetTempPath(), true);

				Assert.AreEqual(TaskStatus.Error, task.Status);
				Assert.AreEqual(2, server.RequestCount);
				Assert.AreEqual(1, ended);
			}
		}

		/// <summary>
		/// Retry exhaustion is explicitly bounded by the configured retry limit.
		/// </summary>
		[Test]
		public void DownloadAsync_ServerBusy_TerminatesAtRetryLimit()
		{
			using (LoopbackHttpServer server = new LoopbackHttpServer(HttpStatusCode.ServiceUnavailable))
			using (FileDownloadTask task = CreateTask())
			{
				SetPrivateInt(task, "m_intRetries", 2);
				SetPrivateInt(task, "m_intRetryInterval", 1);
				int ended = 0;
				task.TaskEnded += (sender, args) => Interlocked.Increment(ref ended);

				task.DownloadAsync(new List<Uri> { server.Uri }, Path.GetTempPath(), true);

				Assert.AreEqual(TaskStatus.Error, task.Status);
				Assert.AreEqual(2, server.RequestCount);
				Assert.AreEqual(1, ended);
			}
		}

		/// <summary>
		/// Cancelling during retry backoff wakes the wait and terminates without probing the server again.
		/// </summary>
		[Test]
		public void DownloadAsync_CancelDuringRetryWait_EndsCancelledWithoutAnotherProbe()
		{
			using (LoopbackHttpServer server = new LoopbackHttpServer(HttpStatusCode.ServiceUnavailable))
			using (FileDownloadTask task = CreateTask())
			{
				SetPrivateInt(task, "m_intRetryInterval", 10000);
				Exception workerFailure = null;
				int ended = 0;
				task.TaskEnded += (sender, args) => Interlocked.Increment(ref ended);
				Thread worker = new Thread(() =>
				{
					try
					{
						task.DownloadAsync(new List<Uri> { server.Uri }, Path.GetTempPath(), true);
					}
					catch (Exception ex)
					{
						workerFailure = ex;
					}
				});
				worker.IsBackground = true;
				worker.Start();

				Assert.IsTrue(SpinWait.SpinUntil(() => task.Status == TaskStatus.Retrying, 3000), "Download never entered the retry wait.");
				task.Cancel();

				Assert.IsTrue(worker.Join(3000), "Cancellation did not release the retry path promptly.");
				Assert.IsNull(workerFailure);
				Assert.AreEqual(TaskStatus.Cancelled, task.Status);
				Assert.AreEqual(1, server.RequestCount);
				Assert.AreEqual(1, ended);
			}
		}

		/// <summary>
		/// Synchronous callers are released when cancellation interrupts retry backoff.
		/// </summary>
		[Test]
		public void Download_CancelDuringRetryWait_UnblocksSynchronousCaller()
		{
			using (LoopbackHttpServer server = new LoopbackHttpServer(HttpStatusCode.ServiceUnavailable))
			using (FileDownloadTask task = CreateTask())
			{
				SetPrivateInt(task, "m_intRetryInterval", 10000);
				Exception workerFailure = null;
				Thread worker = new Thread(() =>
				{
					try
					{
						task.Download(server.Uri, Path.GetTempPath(), true);
					}
					catch (Exception ex)
					{
						workerFailure = ex;
					}
				});
				worker.IsBackground = true;
				worker.Start();

				Assert.IsTrue(SpinWait.SpinUntil(() => task.Status == TaskStatus.Retrying, 3000), "Download never entered the retry wait.");
				task.Cancel();

				Assert.IsTrue(worker.Join(3000), "Synchronous download remained blocked after retry cancellation.");
				Assert.IsNull(workerFailure);
				Assert.AreEqual(TaskStatus.Cancelled, task.Status);
				Assert.AreEqual(1, server.RequestCount);
			}
		}

		/// <summary>
		/// Pausing during retry backoff wakes the wait without starting another probe.
		/// </summary>
		[Test]
		public void DownloadAsync_PauseDuringRetryWait_ReturnsPromptlyWithoutAnotherProbe()
		{
			using (LoopbackHttpServer server = new LoopbackHttpServer(HttpStatusCode.ServiceUnavailable))
			using (FileDownloadTask task = CreateTask())
			{
				SetPrivateInt(task, "m_intRetryInterval", 10000);
				Exception workerFailure = null;
				Thread worker = new Thread(() =>
				{
					try
					{
						task.DownloadAsync(new List<Uri> { server.Uri }, Path.GetTempPath(), true);
					}
					catch (Exception ex)
					{
						workerFailure = ex;
					}
				});
				worker.IsBackground = true;
				worker.Start();

				Assert.IsTrue(SpinWait.SpinUntil(() => task.Status == TaskStatus.Retrying, 3000), "Download never entered the retry wait.");
				task.Pause();

				Assert.IsTrue(worker.Join(3000), "Pause did not release the retry path promptly.");
				Assert.IsNull(workerFailure);
				Assert.AreEqual(TaskStatus.Paused, task.Status);
				Assert.AreEqual(1, server.RequestCount);
			}
		}

		/// <summary>
		/// Queuing during retry backoff wakes the wait without starting another probe.
		/// </summary>
		[Test]
		public void DownloadAsync_QueueDuringRetryWait_ReturnsPromptlyWithoutAnotherProbe()
		{
			using (LoopbackHttpServer server = new LoopbackHttpServer(HttpStatusCode.ServiceUnavailable))
			using (FileDownloadTask task = CreateTask())
			{
				SetPrivateInt(task, "m_intRetryInterval", 10000);
				Exception workerFailure = null;
				Thread worker = new Thread(() =>
				{
					try
					{
						task.DownloadAsync(new List<Uri> { server.Uri }, Path.GetTempPath(), true);
					}
					catch (Exception ex)
					{
						workerFailure = ex;
					}
				});
				worker.IsBackground = true;
				worker.Start();

				Assert.IsTrue(SpinWait.SpinUntil(() => task.Status == TaskStatus.Retrying, 3000), "Download never entered the retry wait.");
				task.Queue();

				Assert.IsTrue(worker.Join(3000), "Queue did not release the retry path promptly.");
				Assert.IsNull(workerFailure);
				Assert.AreEqual(TaskStatus.Queued, task.Status);
				Assert.AreEqual(1, server.RequestCount);
			}
		}

		private static FileDownloadTask CreateTask()
		{
			return new FileDownloadTask(CreateRepository(), 4, 1024, "NMM-C4-retry-tests");
		}

		private static IModRepository CreateRepository()
		{
			return new InterfaceProxy<IModRepository>(call =>
			{
				if (call.MethodName == "get_IsOffline")
					return false;
				return GetDefaultValue(((MethodInfo)call.MethodBase).ReturnType);
			}).Object;
		}

		private static void SetPrivateInt(FileDownloadTask task, string fieldName, int value)
		{
			FieldInfo field = typeof(FileDownloadTask).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(field, "Expected private field was not found: " + fieldName);
			field.SetValue(task, value);
		}

		private static object GetDefaultValue(Type type)
		{
			return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
		}

		private sealed class InterfaceProxy<T> : RealProxy where T : class
		{
			private readonly Func<IMethodCallMessage, object> m_handler;

			public InterfaceProxy(Func<IMethodCallMessage, object> handler)
				: base(typeof(T))
			{
				m_handler = handler;
			}

			public T Object => (T)GetTransparentProxy();

			public override IMessage Invoke(IMessage msg)
			{
				IMethodCallMessage call = (IMethodCallMessage)msg;
				try
				{
					object result = m_handler(call);
					return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
				}
				catch (Exception ex)
				{
					return new ReturnMessage(ex, call);
				}
			}
		}

		private sealed class LoopbackHttpServer : IDisposable
		{
			private readonly TcpListener m_listener;
			private readonly Thread m_thread;
			private readonly HttpStatusCode m_statusCode;
			private volatile bool m_disposed;
			private int m_requestCount;

			public LoopbackHttpServer(HttpStatusCode statusCode)
			{
				m_statusCode = statusCode;
				m_listener = new TcpListener(IPAddress.Loopback, 0);
				m_listener.Start();
				int port = ((IPEndPoint)m_listener.LocalEndpoint).Port;
				Uri = new Uri("http://127.0.0.1:" + port + "/" + Guid.NewGuid().ToString("N") + ".bin");
				m_thread = new Thread(Run);
				m_thread.IsBackground = true;
				m_thread.Start();
			}

			public Uri Uri { get; }

			public int RequestCount => Volatile.Read(ref m_requestCount);

			public void Dispose()
			{
				m_disposed = true;
				m_listener.Stop();
				m_thread.Join(1000);
			}

			private void Run()
			{
				while (!m_disposed)
				{
					try
					{
						using (TcpClient client = m_listener.AcceptTcpClient())
						using (NetworkStream stream = client.GetStream())
						{
							ReadHeaders(stream);
							Interlocked.Increment(ref m_requestCount);
							byte[] response = Encoding.ASCII.GetBytes(BuildResponse());
							stream.Write(response, 0, response.Length);
							stream.Flush();
						}
					}
					catch (SocketException)
					{
						if (!m_disposed)
							throw;
					}
					catch (ObjectDisposedException)
					{
						if (!m_disposed)
							throw;
					}
				}
			}

			private static void ReadHeaders(NetworkStream stream)
			{
				int matched = 0;
				byte[] terminator = { 13, 10, 13, 10 };
				while (matched < terminator.Length)
				{
					int value = stream.ReadByte();
					if (value < 0)
						return;
					if (value == terminator[matched])
						matched++;
					else
						matched = value == terminator[0] ? 1 : 0;
				}
			}

			private string BuildResponse()
			{
				return "HTTP/1.1 " + (int)m_statusCode + " " + GetReasonPhrase(m_statusCode) + "\r\n" +
					"Content-Length: 0\r\n" +
					"Connection: close\r\n\r\n";
			}

			private static string GetReasonPhrase(HttpStatusCode statusCode)
			{
				switch (statusCode)
				{
					case HttpStatusCode.NotFound:
						return "Not Found";
					case HttpStatusCode.ServiceUnavailable:
						return "Service Unavailable";
					default:
						return statusCode.ToString();
				}
			}
		}
	}
}
