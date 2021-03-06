﻿namespace ServiceBus.Management.AcceptanceTests.HeartbeatMonitoring
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Contexts;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NUnit.Framework;
    using ServiceControl.CompositeViews.Endpoints;
    using ServiceControl.Monitoring;
    using Conventions = NServiceBus.AcceptanceTesting.Customization.Conventions;

    public class When_an_unmonitored_endpoint_is_marked_as_monitored : AcceptanceTest
    {
        enum State
        {
            WaitingForEndpointDetection,
            WaitingForHeartbeatFailure
        }

        static string EndpointName => Conventions.EndpointNamingConvention(typeof(MyEndpoint));

        [Test]
        public async Task It_is_shown_as_inactive_if_it_does_not_send_heartbeats()
        {
            var context = new MyContext();
            List<EndpointsView> endpoints = null;
            var state = State.WaitingForEndpointDetection;

            await Define(context)
                .WithEndpoint<MyEndpoint>()
                .Done(async c =>
                {
                    if (state == State.WaitingForEndpointDetection)
                    {
                        var intermediateResult = await TryGetMany<EndpointsView>("/api/endpoints/", e => e.Name == EndpointName && !e.Monitored);
                        endpoints = intermediateResult;
                        if (intermediateResult)
                        {
                            var endpointId = endpoints.First(e => e.Name == EndpointName).Id;
                            await Patch($"/api/endpoints/{endpointId}",new EndpointUpdateModel
                            {
                                MonitorHeartbeat = true
                            });
                            state = State.WaitingForHeartbeatFailure;
                            Console.WriteLine("Patch successful");
                        }
                        return false;
                    }

                    var result = await TryGetMany<EndpointsView>("/api/endpoints/", e => e.Name == EndpointName && e.MonitorHeartbeat && e.Monitored && !e.IsSendingHeartbeats);
                    endpoints = result;
                    return state == State.WaitingForHeartbeatFailure && result;
                })
                .Run();

            var myEndpoint = endpoints.FirstOrDefault(e => e.Name == EndpointName);
            Assert.NotNull(myEndpoint);
            Assert.IsTrue(myEndpoint.Monitored);
            Assert.IsTrue(myEndpoint.MonitorHeartbeat);
            Assert.IsFalse(myEndpoint.IsSendingHeartbeats);
        }

        public class MyContext : ScenarioContext
        {
        }

        public class MyEndpoint : EndpointConfigurationBuilder
        {
            public MyEndpoint()
            {
                EndpointSetup<DefaultServerWithAudit>();
            }

            class SendMessage : IWantToRunWhenBusStartsAndStops
            {
                readonly IBus bus;

                public SendMessage(IBus bus)
                {
                    this.bus = bus;
                }

                public void Start()
                {
                    bus.SendLocal(new MyMessage());
                }

                public void Stop()
                {
                }
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public void Handle(MyMessage message)
                {
                }
            }
        }

        public class MyMessage : IMessage
        {
        }
    }
}