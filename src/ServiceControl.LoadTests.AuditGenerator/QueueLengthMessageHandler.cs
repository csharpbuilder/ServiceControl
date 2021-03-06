﻿namespace ServiceControl.AuditLoadGenerator
{
    using System.Threading.Tasks;
    using NServiceBus;
    using ServiceControl.LoadTests.Messages;

    class QueueLengthMessageHandler : IHandleMessages<QueueLengthReport>
    {
        LoadGenerators generators;

        public QueueLengthMessageHandler(LoadGenerators generators)
        {
            this.generators = generators;
        }

        public Task Handle(QueueLengthReport message, IMessageHandlerContext context)
        {
            return generators.QueueLenghtReported(message.Queue, message.Machine, message.Length);
        }
    }
}