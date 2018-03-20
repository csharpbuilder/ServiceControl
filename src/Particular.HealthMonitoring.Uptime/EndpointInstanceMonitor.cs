namespace ServiceControl.Monitoring
{
    using System;
    using ServiceControl.CompositeViews.Endpoints;
    using ServiceControl.Contracts.HeartbeatMonitoring;
    using ServiceControl.Contracts.Operations;
    using ServiceControl.EndpointControl.Contracts;
    using ServiceControl.HeartbeatMonitoring;
    using ServiceControl.Infrastructure.DomainEvents;

    public class EndpointInstanceMonitor
    {
        IDomainEvents domainEvents;

        public EndpointInstanceId Id { get; }
        DateTime? lastSeen;
        HeartbeatStatus status;

        public bool Monitored { get; private set; }

        public EndpointInstanceMonitor(EndpointInstanceId endpointInstanceId, IDomainEvents domainEvents)
        {
            Id = endpointInstanceId;
            this.domainEvents = domainEvents;
        }

        public bool Apply(IHeartbeatEvent @event)
        {
            return TryApply<MonitoringEnabledForEndpoint>(@event, Apply)
                   || TryApply<MonitoringDisabledForEndpoint>(@event, Apply)
                   || TryApply<HeartbeatingEndpointDetected>(@event, Apply)
                   || TryApply<EndpointHeartbeatRestored>(@event, Apply)
                   || TryApply<EndpointFailedToHeartbeat>(@event, Apply);
        }

        static bool TryApply<T>(IHeartbeatEvent @event, Action<T> applyFunc)
            where T : class, IHeartbeatEvent
        {
            var typed = @event as T;
            if (typed != null)
            {
                applyFunc(typed);
                return true;
            }
            return false;
        }

        void Publish(IHeartbeatEvent @event)
        {
            Apply(@event);
            domainEvents.Raise(@event);
        }

        public void EnableMonitoring()
        {
            domainEvents.Raise(new MonitoringEnabledForEndpoint
            {
                EndpointInstanceId = Id.UniqueId,
                Endpoint = Convert(Id)
            });
        }

        public void DisableMonitoring()
        {
            domainEvents.Raise(new MonitoringDisabledForEndpoint
            {
                EndpointInstanceId = Id.UniqueId,
                Endpoint = Convert(Id)
            });
        }

        public void Initialize()
        {
            domainEvents.Raise(new EndpointDetected
            {
                EndpointInstanceId = Id.UniqueId,
                Endpoint = Convert(Id)
            });
        }

        public void UpdateStatus(HeartbeatStatus newStatus, DateTime? latestTimestamp, DateTime currentTime)
        {
            if (newStatus == status)
            {
                return;
            }

            if (newStatus == HeartbeatStatus.Alive)
            {
                if (status == HeartbeatStatus.Unknown)
                {
                    // NOTE: If an endpoint starts randomly sending heartbeats we monitor it by default
                    // NOTE: This means we'll start monitoring endpoints sending heartbeats after a restart
                    Publish(new HeartbeatingEndpointDetected
                    {
                        EndpointInstanceId = Id.UniqueId,
                        Endpoint = Convert(Id),
                        DetectedAt = latestTimestamp ?? DateTime.UtcNow
                    });
                }
                else if (status == HeartbeatStatus.Dead && Monitored)
                {
                    Publish(new EndpointHeartbeatRestored
                    {
                        EndpointInstanceId = Id.UniqueId,
                        Endpoint = Convert(Id),
                        RestoredAt = latestTimestamp ?? DateTime.UtcNow
                    });
                }
            }
            else if (newStatus == HeartbeatStatus.Dead && Monitored)
            {
                Publish(new EndpointFailedToHeartbeat
                {
                    EndpointInstanceId = Id.UniqueId,
                    Endpoint = Convert(Id),
                    DetectedAt = currentTime,
                    LastReceivedAt = latestTimestamp ?? DateTime.MinValue
                });
            }
        }

        void Apply(HeartbeatingEndpointDetected @event)
        {
            Monitored = true;
            status = HeartbeatStatus.Alive;
            lastSeen = @event.DetectedAt;
        }

        void Apply(EndpointHeartbeatRestored @event)
        {
            status = HeartbeatStatus.Alive;
            lastSeen = @event.RestoredAt;
        }

        void Apply(EndpointFailedToHeartbeat @event)
        {
            status = HeartbeatStatus.Dead;
            lastSeen = @event.LastReceivedAt;
        }

        void Apply(MonitoringEnabledForEndpoint @event)
        {
            Monitored = true;
        }

        void Apply(MonitoringDisabledForEndpoint @event)
        {
            Monitored = false;
        }

        public void AddTo(EndpointMonitoringStats stats)
        {
            if (Monitored)
            {
                if (status == HeartbeatStatus.Alive)
                    stats.RecordActive();
                else
                    stats.RecordFailing();
            }
        }

        EndpointDetails Convert(EndpointInstanceId endpointInstanceId)
        {
            return new EndpointDetails
            {
                Host = endpointInstanceId.HostName,
                HostId = endpointInstanceId.HostGuid,
                Name = endpointInstanceId.LogicalName
            };
        }

        public EndpointsView GetView()
        {
            return new EndpointsView
            {
                Id = Id.UniqueId,
                Name = Id.LogicalName,
                HostDisplayName = Id.HostName,
                Monitored = Monitored,
                MonitorHeartbeat = Monitored,
                HeartbeatInformation = new HeartbeatInformation
                {
                    ReportedStatus = status == HeartbeatStatus.Alive ? Status.Beating : Status.Dead,
                    LastReportAt = lastSeen ?? DateTime.MinValue
                }
            };
        }
    }
}