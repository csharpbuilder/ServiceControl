namespace Particular.HealthMonitoring.Uptime
{
    using System;
    using Nancy;
    using Nancy.ModelBinding;
    using Nancy.Responses.Negotiation;
    using Particular.HealthMonitoring.Uptime.Api;

    class UptimeApiModule : NancyModule
    {
        public UptimeApiModule(EndpointInstanceMonitoring monitoring, IPersistEndpointUptimeInformation persister)
        {
            Get["/heartbeats/stats"] = _ => Negotiate.WithModel(monitoring.GetStats());

            Get["/endpoints"] = _ => Negotiate.WithModel(monitoring.GetEndpoints());

            Patch["/endpoints/{id}", true] = async (parameters, token) =>
            {
                var data = this.Bind<EndpointUpdateModel>();
                var endpointId = (Guid) parameters.id;

                IHeartbeatEvent @event;
                if (data.MonitorHeartbeat)
                {
                    @event = monitoring.EnableMonitoring(endpointId);
                }
                else
                {
                   @event = monitoring.DisableMonitoring(endpointId);
                }

                await persister.Store(new[] {@event}).ConfigureAwait(false);

                return HttpStatusCode.Accepted;
            };
        }

        new Negotiator Negotiate
        {
            get
            {
                var negotiator = new Negotiator(Context);
                negotiator.NegotiationContext.PermissableMediaRanges.Clear();

                //We don't support xml, some issues serializing ICollections and IEnumerables
                //negotiator.NegotiationContext.PermissableMediaRanges.Add(MediaRange.FromString("application/xml"));
                //negotiator.NegotiationContext.PermissableMediaRanges.Add(MediaRange.FromString("application/vnd.particular.1+xml"));

                negotiator.NegotiationContext.PermissableMediaRanges.Add(new MediaRange("application/json"));
                negotiator.NegotiationContext.PermissableMediaRanges.Add(new MediaRange("application/vnd.particular.1+json"));

                return negotiator;
            }
        }
    }
}