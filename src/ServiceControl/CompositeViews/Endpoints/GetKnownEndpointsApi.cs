﻿namespace ServiceControl.CompositeViews.Endpoints
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Nancy;
    using Nancy.Extensions;
    using ServiceControl.CompositeViews.Messages;
    using ServiceControl.Monitoring;

    public class GetKnownEndpointsApi : ScatterGatherApi<NoInput, List<KnownEndpointsView>>
    {
        public EndpointInstanceMonitoring EndpointInstanceMonitoring { get; set; }

        public override Task<QueryResult<List<KnownEndpointsView>>> LocalQuery(Request request, NoInput input)
        {
            var result = EndpointInstanceMonitoring.GetKnownEndpoints();
            return Task.FromResult(Results(result));
        }

        protected override List<KnownEndpointsView> ProcessResults(Request request, QueryResult<List<KnownEndpointsView>>[] results)
        {
            return results.SelectMany(p => p.Results).DistinctBy(e => e.Id).ToList();
        }
    }
}