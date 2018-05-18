// ReSharper disable UnassignedField.Global
// ReSharper disable MemberCanBePrivate.Global
namespace ServiceControlInstaller.PowerShell
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Management.Automation;
    using ServiceControlInstaller.Engine;
    using ServiceControlInstaller.Engine.Configuration.ServiceControl;
    using ServiceControlInstaller.Engine.Instances;
    using ServiceControlInstaller.Engine.Unattended;

    [Cmdlet(VerbsLifecycle.Invoke, "ServiceControlInstanceUpgrade")]
    public class InvokeServiceControlInstanceUpgrade : PSCmdlet
    {
        [ValidateNotNullOrEmpty]
        [Parameter(Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, Position = 0, HelpMessage = "Specify the name of the ServiceControl Instance to update")]
        public string[] Name;

        [Parameter(ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, Position = 1, HelpMessage = "Specify if error messages are forwarded to the queue specified by ErrorLogQueue. This setting if appsetting is not set, this occurs when upgrading versions 1.11.1 and below")]
        public bool? ForwardErrorMessages;

        [Parameter(HelpMessage = "Specify the timespan to keep Audit Data")]
        [ValidateTimeSpanRange(MinimumHours = 1, MaximumHours = 8760)] //1 hour to 365 days
        public TimeSpan? AuditRetentionPeriod { get; set; }

        [Parameter(HelpMessage = "Specify the timespan to keep Error Data")]
        [ValidateTimeSpanRange(MinimumHours = 240, MaximumHours = 1080)] //10 to 45 days
        public TimeSpan? ErrorRetentionPeriod { get; set; }

        [Parameter(Mandatory = false, HelpMessage = "Do not automatically create new queues")]
        public SwitchParameter SkipQueueCreation { get; set; }

        [Parameter(Mandatory = false, HelpMessage = "Port for exposing RavenDB in the maintenance mode")]
        [ValidateRange(1, 49151)]
        public int? DatabaseMaintenancePort { get; set; }


        protected override void BeginProcessing()
        {
            Account.TestIfAdmin();
        }

        protected override void ProcessRecord()
        {
            var logger = new PSLogger(Host);
            InvokeUpgrade(Path.GetDirectoryName(MyInvocation.MyCommand.Module.Path), logger, ThrowTerminatingError);
        }

        public void InvokeUpgrade(string zipFolder, ILogging logger, Action<ErrorRecord> throwTerminatingError)
        {
            var installer = new UnattendServiceControlInstaller(logger, zipFolder);
            var allInstances = InstanceFinder.ServiceControlInstances();
            var usedPorts = allInstances
                .SelectMany(p => new[] { p.Port, p.DatabaseMaintenancePort })
                .Where(p => p.HasValue)
                .Select(p => p.Value)
                .Distinct()
                .ToList();

            foreach (var name in Name)
            {
                var options = new ServiceControlUpgradeOptions
                {
                    AuditRetentionPeriod = AuditRetentionPeriod,
                    ErrorRetentionPeriod = ErrorRetentionPeriod,
                    OverrideEnableErrorForwarding = ForwardErrorMessages,
                    SkipQueueCreation = SkipQueueCreation,
                    MaintenancePort = DatabaseMaintenancePort
                };
                var instance = allInstances.Single(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (instance == null)
                {
                    logger.Warn($"No action taken. An instance called {name} was not found");
                    break;
                }

                options.OverrideEnableErrorForwarding = ForwardErrorMessages;

                // Migrate Value
                if (!options.AuditRetentionPeriod.HasValue)
                {
                    if (instance.AppConfig.AppSettingExists(SettingsList.HoursToKeepMessagesBeforeExpiring.Name))
                    {
                        var i = instance.AppConfig.Read(SettingsList.HoursToKeepMessagesBeforeExpiring.Name, -1);
                        if (i != -1)
                        {
                            options.AuditRetentionPeriod = TimeSpan.FromHours(i);
                        }
                    }
                }

                // From Version 2.0 require database maintenance port to be specified explicitly
                if (installer.ZipInfo.Version.Major >= 2 && !options.MaintenancePort.HasValue && !instance.AppConfig.AppSettingExists(SettingsList.DatabaseMaintenancePort.Name))
                {
                    throwTerminatingError(new ErrorRecord(new Exception($"Upgrade of {instance.Name} aborted. DatabaseMaintenancePort parameter must be specified because the configuration file has no setting for DatabaseMaintenancePort. This setting is mandatory as of version 2.0"), "UpgradeFailure", ErrorCategory.InvalidArgument, null));
                }

                // If changing maintenance port it must be unique
                if (options.MaintenancePort.HasValue && options.MaintenancePort != instance.DatabaseMaintenancePort && usedPorts.Contains(options.MaintenancePort.Value))
                {
                    throwTerminatingError(new ErrorRecord(new Exception($"Upgrade of {instance.Name} aborted. DatabaseMaintenancePort parameter must be unique."), "UpgradeFailure", ErrorCategory.InvalidArgument, null));
                }

                if (!options.OverrideEnableErrorForwarding.HasValue & !instance.AppConfig.AppSettingExists(SettingsList.ForwardErrorMessages.Name))
                {
                    throwTerminatingError(new ErrorRecord(new Exception($"Upgrade of {instance.Name} aborted. ForwardErrorMessages parameter must be set to true or false because the configuration file has no setting for ForwardErrorMessages. This setting is mandatory as of version 1.12"), "UpgradeFailure", ErrorCategory.InvalidArgument, null));
                }

                if (!options.ErrorRetentionPeriod.HasValue & !instance.AppConfig.AppSettingExists(SettingsList.ErrorRetentionPeriod.Name))
                {
                    throwTerminatingError(new ErrorRecord(new Exception($"Upgrade of {instance.Name} aborted. ErrorRetentionPeriod parameter must be set to timespan because the configuration file has no setting for ErrorRetentionPeriod. This setting is mandatory as of version 1.13"), "UpgradeFailure", ErrorCategory.InvalidArgument, null));
                }

                if (!options.AuditRetentionPeriod.HasValue & !instance.AppConfig.AppSettingExists(SettingsList.AuditRetentionPeriod.Name))
                {
                    throwTerminatingError(new ErrorRecord(new Exception($"Upgrade of {instance.Name} aborted. AuditRetentionPeriod parameter must be set to timespan because the configuration file has no setting for AuditRetentionPeriod. This setting is mandatory as of version 1.13"), "UpgradeFailure", ErrorCategory.InvalidArgument, null));
                }

                if (!installer.Upgrade(instance, options))
                {
                    throwTerminatingError(new ErrorRecord(new Exception($"Upgrade of {instance.Name} failed"), "UpgradeFailure", ErrorCategory.InvalidResult, null));
                }
            }
        }
    }
}