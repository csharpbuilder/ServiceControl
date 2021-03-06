﻿namespace ServiceBus.Management.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.Features;
    using NServiceBus.Settings;
    using NUnit.Framework;
    using ServiceBus.Management.AcceptanceTests.Contexts;
    using ServiceControl.Infrastructure;
    using ServiceControl.MessageFailures;

    public class When_a_retry_for_a_failed_message_fails : AcceptanceTest
    {
        [Test]
        public async Task It_should_be_marked_as_unresolved()
        {
            var context = new MyContext { Succeed = false };

            FailedMessage failure = null;

            await Define(context)
                .WithEndpoint<FailureEndpoint>(b =>
                    b.Given(bus => bus.SendLocal(new MyMessage()))
                        .When(async ctx => await CheckProcessingAttemptsIs(ctx, 1),
                            (bus, ctx) => Post<object>($"/api/errors/{ctx.UniqueMessageId}/retry"))
                        .When(async ctx => await CheckProcessingAttemptsIs(ctx, 2),
                            (bus, ctx) => Post<object>($"/api/errors/{ctx.UniqueMessageId}/retry"))
                )
                .Done(async ctx =>
                {
                    var result = await GetFailedMessage(ctx, f => f.ProcessingAttempts.Count == 3);
                    failure = result;
                    return result;
                })
                .Run(TimeSpan.FromMinutes(4));

            Assert.IsNotNull(failure, "Failure should not be null");
            Assert.AreEqual(FailedMessageStatus.Unresolved, failure.Status);
        }

        [Test]
        public async Task It_should_be_able_to_be_retried_successfully()
        {
            var context = new MyContext { Succeed = false };

            FailedMessage failure = null;

            await Define(context)
                .WithEndpoint<FailureEndpoint>(b =>
                    b.Given(bus => bus.SendLocal(new MyMessage()))
                     .When(async ctx => await CheckProcessingAttemptsIs(ctx, 1),
                          (bus, ctx) => Post<object>($"/api/errors/{ctx.UniqueMessageId}/retry"))
                     .When(async ctx => await CheckProcessingAttemptsIs(ctx, 2),
                          (bus, ctx) => Post<object>($"/api/errors/{ctx.UniqueMessageId}/retry"))
                     .When(async ctx => await CheckProcessingAttemptsIs(ctx, 3),
                         async (bus, ctx) =>
                         {
                             ctx.Succeed = true;
                             await Post<object>($"/api/errors/{ctx.UniqueMessageId}/retry");
                         })
                    )
                .Done(async ctx =>
                {
                    var result = await GetFailedMessage(ctx, f => f.Status == FailedMessageStatus.Resolved);
                    failure = result;
                    return result;
                })
                .Run(TimeSpan.FromMinutes(4));

            Assert.IsNotNull(failure, "Failure should not be null");
            Assert.AreEqual(FailedMessageStatus.Resolved, failure.Status);
        }

        Task<SingleResult<FailedMessage>> CheckProcessingAttemptsIs(MyContext ctx, int count)
        {
            return GetFailedMessage(ctx, f => f.ProcessingAttempts.Count == count);
        }

        async Task<SingleResult<FailedMessage>> GetFailedMessage(MyContext c, Predicate<FailedMessage> condition)
        {
            if (c.UniqueMessageId == null)
            {
                return SingleResult<FailedMessage>.Empty;
            }

            return await TryGet($"/api/errors/{c.UniqueMessageId}", condition);
        }

        public class FailureEndpoint : EndpointConfigurationBuilder
        {
            public FailureEndpoint()
            {
                EndpointSetup<DefaultServerWithAudit>(c => c.DisableFeature<SecondLevelRetries>());
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyContext Context { get; set; }

                public IBus Bus { get; set; }
                public ReadOnlySettings Settings { get; set; }

                public void Handle(MyMessage message)
                {
                    Console.WriteLine("Attempting to process message");

                    Context.EndpointNameOfReceivingEndpoint = Settings.LocalAddress().Queue;
                    Context.MessageId = Bus.CurrentMessageContext.Id.Replace(@"\", "-");

                    if (!Context.Succeed) //simulate that the exception will be resolved with the retry
                    {
                        Console.WriteLine("Message processing failure");
                        throw new Exception("Simulated exception");
                    }
                    Console.WriteLine("Message processing success");
                }
            }
        }

        [Serializable]
        public class MyMessage : ICommand
        {
        }

        public class MyContext : ScenarioContext
        {
            public string MessageId { get; set; }

            public string EndpointNameOfReceivingEndpoint { get; set; }

            public bool Succeed { get; set; }

            public string UniqueMessageId => DeterministicGuid.MakeId(MessageId, EndpointNameOfReceivingEndpoint).ToString();
        }
    }
}