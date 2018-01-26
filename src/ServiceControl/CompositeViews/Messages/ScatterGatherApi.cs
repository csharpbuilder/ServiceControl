namespace ServiceControl.CompositeViews.Messages
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Autofac;
    using Nancy;
    using Newtonsoft.Json;
    using NServiceBus.Logging;
    using Raven.Client;
    using ServiceBus.Management.Infrastructure.Extensions;
    using ServiceBus.Management.Infrastructure.Nancy;
    using ServiceBus.Management.Infrastructure.Nancy.Modules;
    using ServiceBus.Management.Infrastructure.Settings;
    using ServiceControl.Infrastructure.Settings;
    using HttpStatusCode = System.Net.HttpStatusCode;

    public interface IApi
    {
    }

    public class ApisModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterAssemblyTypes(ThisAssembly)
                .AssignableTo<IApi>()
                .AsSelf()
                .AsImplementedInterfaces()
                .PropertiesAutowired();
        }
    }

    public abstract class ScatterGatherApi<TIn, TOut> : IApi
        where TOut : class
    {
        static JsonSerializer jsonSerializer = JsonSerializer.Create(JsonNetSerializer.CreateDefault());
        static ILog logger = LogManager.GetLogger(typeof(ScatterGatherApi<TIn, TOut>));

        private Dictionary<string, string> instanceIdToApiUri;
        private Dictionary<string, string> apiUriToInstanceId;
        private string currentInstanceId;
        private string currentInvariantApiUri;

        public IDocumentStore Store { get; set; }
        public Settings Settings { get; set; }
        public Func<HttpClient> HttpClientFactory { get; set; }

        public string CurrentInstanceId
        {
            get
            {
                if (currentInstanceId == null)
                {
                    currentInstanceId = InstanceIdGenerator.FromApiUrl(Settings.ApiUrl);
                }

                return currentInstanceId;
            }
        }

        public string CurrentInvariantApiUri
        {
            get
            {
                if (currentInvariantApiUri == null)
                {
                    currentInvariantApiUri = Settings.ApiUrl.ToLowerInvariant();
                }

                return currentInvariantApiUri;
            }
        }

        public Dictionary<string, string> InstanceIdToApiUri
        {
            get
            {
                if (instanceIdToApiUri == null)
                {
                    instanceIdToApiUri = Settings.RemoteInstances.ToDictionary(k => InstanceIdGenerator.FromApiUrl(k.ApiUri), v => v.ApiUri);
                    instanceIdToApiUri.Add(CurrentInstanceId, Settings.ApiUrl.ToLowerInvariant());
                }

                return instanceIdToApiUri;
            }
        }

        public Dictionary<string, string> ApiUriToInstanceId
        {
            get
            {
                if (apiUriToInstanceId == null)
                {
                    apiUriToInstanceId = Settings.RemoteInstances.ToDictionary(k => k.ApiUri, v => InstanceIdGenerator.FromApiUrl(v.ApiUri));
                    apiUriToInstanceId.Add(Settings.ApiUrl.ToLowerInvariant(), InstanceIdGenerator.FromApiUrl(Settings.ApiUrl));
                }

                return apiUriToInstanceId;
            }
        }

        public async Task<dynamic> Execute(BaseModule module, TIn input)
        {
            var remotes = Settings.RemoteInstances;
            var currentRequest = module.Request;
            var query = (DynamicDictionary)module.Request.Query;

			var remoteApiUrls = InstanceIdToApiUri;
            var tasks = new List<Task<QueryResult<TOut>>>(remotes.Length + 1);
            dynamic instanceId;
            if (query.TryGetValue("instance_id", out instanceId))
            {
                var id = (string) instanceId;
                if (id == CurrentInstanceId)
                {
                    tasks.Add(LocalQuery(currentRequest, input, ApiUriToInstanceId[Settings.ApiUrl.ToLowerInvariant()]));
                }
                else
                {
                    string remoteUri;
                    if (remoteApiUrls.TryGetValue(id, out remoteUri))
                    {
                        tasks.Add(FetchAndParse(currentRequest, remoteUri, id));
                    }
                }
            }
            else
            {
                tasks.Add(LocalQuery(currentRequest, input, ApiUriToInstanceId[Settings.ApiUrl.ToLowerInvariant()]));
                foreach (var remote in remotes)
                {
                    tasks.Add(FetchAndParse(currentRequest, remote.ApiUri, ApiUriToInstanceId[remote.ApiUri]));
                }
            }

            var response = AggregateResults(currentRequest, CurrentInstanceId, await Task.WhenAll(tasks));

            var negotiate = module.Negotiate;
            return negotiate.WithPartialQueryResult(response, currentRequest);
        }

        public abstract Task<QueryResult<TOut>> LocalQuery(Request request, TIn input, string instanceId);

        private QueryResult<TOut> AggregateResults(Request request, string instanceId, QueryResult<TOut>[] results)
        {
            var combinedResults = ProcessResults(request, results);

            return new QueryResult<TOut>(
                combinedResults,
                instanceId,
                AggregateStats(results)
            );
        }

        protected abstract TOut ProcessResults(Request request, QueryResult<TOut>[] results);

        protected QueryResult<TOut> Results(TOut results, string instanceId, RavenQueryStatistics stats = null)
        {
            return stats != null
                ? new QueryResult<TOut>(results, instanceId, new QueryStatsInfo(stats.IndexEtag, stats.IndexTimestamp, stats.TotalResults))
                : new QueryResult<TOut>(results, instanceId, QueryStatsInfo.Zero);
        }

        private QueryStatsInfo AggregateStats(IEnumerable<QueryResult<TOut>> results)
        {
            var infos = results.OrderBy(x => x.InstanceId, StringComparer.InvariantCultureIgnoreCase).Select(x => x.QueryStats).ToArray();

            return new QueryStatsInfo(
                string.Join("", infos.Select(x => x.ETag)),
                infos.Max(x => x.LastModified),
                infos.Sum(x => x.TotalCount),
                infos.Max(x => x.HighestTotalCountOfAllTheInstances)
            );
        }


        async Task<QueryResult<TOut>> FetchAndParse(Request currentRequest, string remoteUri, string instanceId)
        {
            var instanceUri = new Uri($"{remoteUri}{currentRequest.Path}?{currentRequest.Url.Query}");
            var httpClient = HttpClientFactory();
            try
            {
                var rawResponse = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, instanceUri)).ConfigureAwait(false);
                // special case - queried by conversation ID and nothing was found
                if (rawResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return QueryResult<TOut>.Empty(instanceId);
                }

                return await ParseResult(rawResponse, instanceId).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.Warn($"Failed to query remote instance at {remoteUri}.", exception);
                return QueryResult<TOut>.Empty(instanceId);
            }
        }

        static async Task<QueryResult<TOut>> ParseResult(HttpResponseMessage responseMessage, string instanceId)
        {
            using (var responseStream = await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var jsonReader = new JsonTextReader(new StreamReader(responseStream)))
            {
                var remoteResults = jsonSerializer.Deserialize<TOut>(jsonReader);

                IEnumerable<string> totalCounts;
                var totalCount = 0;
                if (responseMessage.Headers.TryGetValues("Total-Count", out totalCounts))
                {
                    totalCount = int.Parse(totalCounts.ElementAt(0));
                }

                IEnumerable<string> etags;
                string etag = null;
                if (responseMessage.Headers.TryGetValues("ETag", out etags))
                {
                    etag = etags.ElementAt(0);
                }

                IEnumerable<string> lastModifiedValues;
                var lastModified = DateTime.UtcNow;
                if (responseMessage.Headers.TryGetValues("Last-Modified", out lastModifiedValues))
                {
                    lastModified = DateTime.ParseExact(lastModifiedValues.ElementAt(0), "R", CultureInfo.InvariantCulture);
                }

                return new QueryResult<TOut>(remoteResults, instanceId, new QueryStatsInfo(etag, lastModified, totalCount, totalCount));
            }
        }
    }
}