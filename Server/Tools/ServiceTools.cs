using ModelContextProtocol.Server;
using System.ComponentModel;
using System.ServiceProcess;

namespace Server.Tools
{
	public sealed record WindowsServiceInfo(
		string ServiceName,
		string DisplayName,
		string Status,
		string ServiceType,
		string MachineName,
		bool CanStop,
		bool CanPauseAndContinue,
		bool CanShutdown);

	public sealed record WindowsServiceActionInfo(
		string ServiceName,
		string DisplayName,
		string Status,
		bool ActionRequested,
		string Message);

	public sealed class ServiceTools
	{
		[McpServerTool]
		[Description("Lists Windows services, optionally filtering by service name or display name.")]
		public IReadOnlyList<WindowsServiceInfo> ListWindowsServices(
			[Description("Optional text to match against service name or display name.")] string? nameContains = null)
		{
			return ServiceController.GetServices()
				.Where(service => string.IsNullOrWhiteSpace(nameContains) ||
					service.ServiceName.Contains(nameContains, StringComparison.OrdinalIgnoreCase) ||
					service.DisplayName.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
				.OrderBy(service => service.ServiceName, StringComparer.OrdinalIgnoreCase)
				.Select(ToInfo)
				.ToArray();
		}

		[McpServerTool]
		[Description("Gets status and metadata for a Windows service by service name or display name.")]
		public WindowsServiceInfo GetWindowsService(
			[Description("Service name or display name.")] string name)
		{
			using var service = ResolveService(name);
			return ToInfo(service);
		}

		[McpServerTool]
		[Description("Starts a Windows service and optionally waits for it to report Running.")]
		public WindowsServiceActionInfo StartWindowsService(
			[Description("Service name or display name.")] string name,
			[Description("How long to wait for Running status after start, in milliseconds. Use 0 to return immediately.")] int timeoutMilliseconds = 30000)
		{
			using var service = ResolveService(name);
			service.Refresh();

			if (service.Status == ServiceControllerStatus.Running)
			{
				return ToActionInfo(service, actionRequested: false, "Service is already running.");
			}

			service.Start();
			WaitForStatus(service, ServiceControllerStatus.Running, timeoutMilliseconds);
			service.Refresh();

			return ToActionInfo(service, actionRequested: true, "Start requested.");
		}

		[McpServerTool]
		[Description("Stops a Windows service and optionally waits for it to report Stopped.")]
		public WindowsServiceActionInfo StopWindowsService(
			[Description("Service name or display name.")] string name,
			[Description("How long to wait for Stopped status after stop, in milliseconds. Use 0 to return immediately.")] int timeoutMilliseconds = 30000)
		{
			using var service = ResolveService(name);
			service.Refresh();

			if (service.Status == ServiceControllerStatus.Stopped)
			{
				return ToActionInfo(service, actionRequested: false, "Service is already stopped.");
			}

			if (!service.CanStop)
			{
				throw new InvalidOperationException($"Service '{service.ServiceName}' cannot be stopped.");
			}

			service.Stop();
			WaitForStatus(service, ServiceControllerStatus.Stopped, timeoutMilliseconds);
			service.Refresh();

			return ToActionInfo(service, actionRequested: true, "Stop requested.");
		}

		[McpServerTool]
		[Description("Restarts a Windows service by stopping and then starting it.")]
		public WindowsServiceActionInfo RestartWindowsService(
			[Description("Service name or display name.")] string name,
			[Description("How long to wait for each stop/start phase, in milliseconds. Use 0 to return immediately after requests.")] int timeoutMilliseconds = 30000)
		{
			using var service = ResolveService(name);
			service.Refresh();

			if (service.Status != ServiceControllerStatus.Stopped)
			{
				if (!service.CanStop)
				{
					throw new InvalidOperationException($"Service '{service.ServiceName}' cannot be stopped.");
				}

				service.Stop();
				WaitForStatus(service, ServiceControllerStatus.Stopped, timeoutMilliseconds);
				service.Refresh();
			}

			service.Start();
			WaitForStatus(service, ServiceControllerStatus.Running, timeoutMilliseconds);
			service.Refresh();

			return ToActionInfo(service, actionRequested: true, "Restart requested.");
		}

		private static ServiceController ResolveService(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("Service name cannot be empty.", nameof(name));
			}

			var matches = ServiceController.GetServices()
				.Where(service =>
					string.Equals(service.ServiceName, name.Trim(), StringComparison.OrdinalIgnoreCase) ||
					string.Equals(service.DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase))
				.ToArray();

			if (matches.Length == 0)
			{
				throw new InvalidOperationException($"No Windows service matched '{name}'.");
			}

			if (matches.Length > 1)
			{
				var matchList = string.Join(", ", matches.Select(service => service.ServiceName).Take(8));
				throw new InvalidOperationException($"Service name '{name}' matched multiple services: {matchList}.");
			}

			return matches[0];
		}

		private static void WaitForStatus(
			ServiceController service,
			ServiceControllerStatus status,
			int timeoutMilliseconds)
		{
			if (timeoutMilliseconds < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), timeoutMilliseconds, "Timeout cannot be negative.");
			}

			if (timeoutMilliseconds == 0)
			{
				return;
			}

			service.WaitForStatus(status, TimeSpan.FromMilliseconds(timeoutMilliseconds));
		}

		private static WindowsServiceInfo ToInfo(ServiceController service)
		{
			service.Refresh();
			return new WindowsServiceInfo(
				service.ServiceName,
				service.DisplayName,
				service.Status.ToString(),
				service.ServiceType.ToString(),
				service.MachineName,
				service.CanStop,
				service.CanPauseAndContinue,
				service.CanShutdown);
		}

		private static WindowsServiceActionInfo ToActionInfo(
			ServiceController service,
			bool actionRequested,
			string message)
		{
			service.Refresh();
			return new WindowsServiceActionInfo(
				service.ServiceName,
				service.DisplayName,
				service.Status.ToString(),
				actionRequested,
				message);
		}
	}
}
