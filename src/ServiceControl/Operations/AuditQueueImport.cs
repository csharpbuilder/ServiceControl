﻿namespace ServiceControl.Operations
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Metrics;
    using NServiceBus;
    using NServiceBus.Logging;
    using NServiceBus.ObjectBuilder;
    using NServiceBus.Satellites;
    using NServiceBus.Transports;
    using NServiceBus.Unicast;
    using NServiceBus.Unicast.Transport;
    using Raven.Client;
    using ServiceBus.Management.Infrastructure.Settings;

    public class AuditQueueImport : IAdvancedSatellite, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(AuditQueueImport));
        private readonly IBuilder builder;
        private readonly CriticalError criticalError;
        private readonly IEnrichImportedMessages[] enrichers;
        private readonly ISendMessages forwarder;
        private readonly LoggingSettings loggingSettings;
        private readonly Settings settings;
        private readonly AuditImporter auditImporter;
        private readonly IDocumentStore store;
        private SatelliteImportFailuresHandler satelliteImportFailuresHandler;
        private readonly Timer timer = Metric.Timer("Audit messages processed", Unit.Custom("Messages"));

        public AuditQueueImport(IBuilder builder, ISendMessages forwarder, IDocumentStore store, CriticalError criticalError, LoggingSettings loggingSettings, Settings settings, AuditImporter auditImporter)
        {
            this.builder = builder;
            this.forwarder = forwarder;
            this.store = store;

            this.criticalError = criticalError;
            this.loggingSettings = loggingSettings;
            this.settings = settings;
            this.auditImporter = auditImporter;

            enrichers = builder.BuildAll<IEnrichImportedMessages>().Where(e => e.EnrichAudits).ToArray();
        }

        public bool Handle(TransportMessage message)
        {
            using (timer.NewContext())
            {
                InnerHandle(message);
            }

            return true;
        }

        public void Start()
        {
            if (!TerminateIfForwardingIsEnabledButQueueNotWritable())
            {
                Logger.Info($"Audit import is now started, feeding audit messages from: {InputAddress}");
            }
        }

        public void Stop()
        {
        }

        public Address InputAddress => settings.AuditQueue;

        public bool Disabled => InputAddress == Address.Undefined || InputAddress == null || !settings.IngestAuditMessages;

        public Action<TransportReceiver> GetReceiverCustomization()
        {
            satelliteImportFailuresHandler = new SatelliteImportFailuresHandler(builder.Build<IDocumentStore>(),
                Path.Combine(loggingSettings.LogPath, @"FailedImports\Audit"), tm => new FailedAuditImport
                {
                    Message = new FailedTransportMessage()
                    {
                        Id = tm.Id,
                        Headers = tm.Headers,
                        Body = tm.Body
                    }
                },
                criticalError);

            return receiver => { receiver.FailureManager = satelliteImportFailuresHandler; };
        }

        public void Dispose()
        {
            satelliteImportFailuresHandler?.Dispose();
        }

        private void InnerHandle(TransportMessage message)
        {
            var entity = auditImporter.ConvertToSaveMessage(message);
            using (var session = store.OpenSession())
            {
                session.Store(entity);
                session.SaveChanges();
            }

            if (settings.ForwardAuditMessages)
            {
                TransportMessageCleaner.CleanForForwarding(message);
                forwarder.Send(message, new SendOptions(settings.AuditLogQueue));
            }
        }

        private bool TerminateIfForwardingIsEnabledButQueueNotWritable()
        {
            if (!settings.ForwardAuditMessages)
            {
                return false;
            }

            try
            {
                //Send a message to test the forwarding queue
                var testMessage = new TransportMessage(Guid.Empty.ToString("N"), new Dictionary<string, string>());
                forwarder.Send(testMessage, new SendOptions(settings.AuditLogQueue));
                return false;
            }
            catch (Exception messageForwardingException)
            {
                criticalError.Raise("Audit Import cannot start", messageForwardingException);
                return true;
            }
        }
    }
}