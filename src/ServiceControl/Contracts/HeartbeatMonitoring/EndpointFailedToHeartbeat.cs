namespace ServiceControl.Contracts.HeartbeatMonitoring
{
    using System;
    using Operations;
    using ServiceControl.Infrastructure.DomainEvents;

    public class EndpointFailedToHeartbeat : IDomainEvent
    {
        public EndpointDetails Endpoint { get; set; }
        public DateTime LastReceivedAt { get; set; }
        public DateTime DetectedAt { get; set; }
    }
}