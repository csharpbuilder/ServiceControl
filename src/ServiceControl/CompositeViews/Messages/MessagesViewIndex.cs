namespace ServiceControl.CompositeViews.Messages
{
    using System;
    using System.Linq;
    using Contracts.Operations;
    using Lucene.Net.Analysis.Standard;
    using MessageAuditing;
    using MessageFailures;
    using Raven.Abstractions.Indexing;
    using Raven.Client.Indexes;

    public class MessagesViewIndex : AbstractMultiMapIndexCreationTask<MessagesView>
    {
        public MessagesViewIndex()
        {
            AddMap<ProcessedMessage>(messages => messages.Select(message => new
            {
                Id = message.UniqueMessageId,
                MessageType = message.MessageMetadata["MessageType"].Value,
                MessageIntent = message.MessageMetadata["MessageIntent"].Value,
                IsSystemMessage = message.MessageMetadata["IsSystemMessage"].Value,
                Status = MessageStatus.Successful,
                message.ProcessedAt,
                SendingEndpointName = ((EndpointDetails) message.MessageMetadata["SendingEndpoint"].Value).Name,
                ReceivingEndpointName = ((EndpointDetails) message.MessageMetadata["ReceivingEndpoint"].Value).Name,
                ConversationId = message.MessageMetadata["ConversationId"].Value,
                TimeSent = message.MessageMetadata["TimeSent"].Value,
                ProcessingTime = message.MessageMetadata["ProcessingTime"].Value,
                CriticalTime = message.MessageMetadata["CriticalTime"].Value,
                message.Headers,
                Query = message.MessageMetadata.SelectMany(kvp => kvp.Value.SearchTokens).ToArray()
            }));


            AddMap<FailedMessage>(messages => messages.Select(message => new
            {
                message.Id,
                MessageType = message.MostRecentAttempt.MessageMetadata["MessageType"].Value,
                MessageIntent = message.MostRecentAttempt.MessageMetadata["MessageIntent"].Value,
                IsSystemMessage = message.MostRecentAttempt.MessageMetadata["IsSystemMessage"].Value,
                Status = message.ProcessingAttempts.Count() == 1 ? MessageStatus.Failed : MessageStatus.RepeatedFailure,
                ProcessedAt = message.MostRecentAttempt.FailureDetails.TimeOfFailure,
                SendingEndpointName =
                    ((EndpointDetails) message.MostRecentAttempt.MessageMetadata["SendingEndpoint"].Value).Name,
                ReceivingEndpointName =
                    ((EndpointDetails) message.MostRecentAttempt.MessageMetadata["ReceivingEndpoint"].Value).Name,
                ConversationId = message.MostRecentAttempt.MessageMetadata["ConversationId"].Value,
                TimeSent = message.MostRecentAttempt.MessageMetadata["TimeSent"].Value,
                ProcessingTime = (object) TimeSpan.Zero,
                CriticalTime = (object) TimeSpan.Zero,
                message.MostRecentAttempt.Headers,
                Query = message.MostRecentAttempt.MessageMetadata.SelectMany(kvp => kvp.Value.SearchTokens).ToArray()
            }));

            Reduce = results => from message in results
                group message by message.Id
                into g
                let d = g.OrderByDescending(m => m.ProcessedAt).FirstOrDefault()
                select new MessagesView
                {
                    Id = g.Key,
                    MessageType = d.MessageType,
                    MessageIntent = d.MessageIntent,
                    IsSystemMessage = d.IsSystemMessage,
                    Status = d.Status,
                    ProcessedAt = d.ProcessedAt,
                    SendingEndpointName = d.SendingEndpointName,
                    ReceivingEndpointName = d.ReceivingEndpointName,
                    ConversationId = d.ConversationId,
                    TimeSent = d.TimeSent,
                    ProcessingTime = d.ProcessingTime,
                    CriticalTime = d.CriticalTime,
                    Headers = d.Headers,
                    Query = d.Query,
                };

            Index(x => x.Query, FieldIndexing.Analyzed);
            Index(x => x.ReceivingEndpointName, FieldIndexing.Default);
            Index(x => x.CriticalTime, FieldIndexing.Default);
            Index(x => x.ProcessingTime, FieldIndexing.Default);
            Index(x => x.ProcessedAt, FieldIndexing.Default);

            Sort(x => x.CriticalTime, SortOptions.Long);
            Sort(x => x.ProcessingTime, SortOptions.Long);

            Analyze(x => x.Query, typeof(StandardAnalyzer).AssemblyQualifiedName);
        }
    }
}