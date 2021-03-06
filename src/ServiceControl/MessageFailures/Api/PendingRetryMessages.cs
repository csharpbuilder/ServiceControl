﻿namespace ServiceControl.MessageFailures.Api
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Nancy;
    using Nancy.ModelBinding;
    using NServiceBus;
    using ServiceBus.Management.Infrastructure.Nancy.Modules;
    using ServiceControl.MessageFailures.InternalMessages;

    public class PendingRetryMessages : BaseModule
    {
        private class PendingRetryRequest
        {
            public string queueaddress { get; set; }
            public string from { get; set; }
            public string to { get; set; }
        }

        public PendingRetryMessages()
        {
            Post["/pendingretries/retry"] = _ =>
            {
                var ids = this.Bind<List<string>>();

                if (ids.Any(string.IsNullOrEmpty))
                {
                    return HttpStatusCode.BadRequest;
                }

                Bus.SendLocal<RetryPendingMessagesById>(m => m.MessageUniqueIds = ids.ToArray());

                return HttpStatusCode.Accepted;
            };

            Post["/pendingretries/queues/retry"] = parameters =>
            {
                var request = this.Bind<PendingRetryRequest>();

                if (string.IsNullOrWhiteSpace(request.queueaddress))
                {
                    return Negotiate.WithReasonPhrase("QueueAddress").WithStatusCode(HttpStatusCode.BadRequest);
                }

                DateTime from, to;

                try
                {
                    from = DateTime.Parse(request.from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    to = DateTime.Parse(request.to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                }
                catch (Exception)
                {
                    return Negotiate.WithReasonPhrase("From/To").WithStatusCode(HttpStatusCode.BadRequest);
                }

                Bus.SendLocal<RetryPendingMessages>(m =>
                {
                    m.QueueAddress = request.queueaddress;
                    m.PeriodFrom = from;
                    m.PeriodTo = to;
                });

                return HttpStatusCode.Accepted;
            };
        }

        public IBus Bus { get; set; }
    }


}