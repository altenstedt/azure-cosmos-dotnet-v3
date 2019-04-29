﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Client.Core.Tests;
    using Microsoft.Azure.Cosmos.Internal;
    using Microsoft.Azure.Cosmos.Query.ExecutionComponent;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Microsoft.Azure.Cosmos.Query;
    using Moq;
    using System.Threading;

    [TestClass]
    public class CosmosQueryUnitTests
    {
        [TestMethod]
        public async Task TestCosmosQueryExecutionComponentOnFailure()
        {
            List<AggregateOperator> operators = new List<AggregateOperator>()
            {
                AggregateOperator.Average,
                AggregateOperator.Count,
                AggregateOperator.Max,
                AggregateOperator.Min,
                AggregateOperator.Sum
            };

            (Func<string, Task<CosmosQueryExecutionComponent>>, CosmosQueryResponse) setupContext = this.SetupBaseContextToVerifyFailureScenario();
            ItemQueryExecutionComponentBase executionContext = await AggregateItemQueryExecutionComponent.CreateAsync(
                operators.ToArray(),
                null,
                setupContext.Item1);

           CosmosQueryResponse response = await executionContext.DrainAsync(1, default(CancellationToken));
            Assert.AreEqual(setupContext.Item2, response);

             executionContext = await DistinctItemQueryExecutionComponent.CreateAsync(
                      null,
                      setupContext.Item1,
                      DistinctQueryType.Ordered);

            response = await executionContext.DrainAsync(1, default(CancellationToken));
            Assert.AreEqual(setupContext.Item2, response);

            executionContext = await SkipItemQueryExecutionComponent.CreateAsync(
                       5,
                       null,
                       setupContext.Item1);

            response = await executionContext.DrainAsync(1, default(CancellationToken));
            Assert.AreEqual(setupContext.Item2, response);

            executionContext = await TakeItemQueryExecutionComponent.CreateLimitDocumentQueryExecutionComponentAsync(
                      5,
                      null,
                      setupContext.Item1);

            response = await executionContext.DrainAsync(1, default(CancellationToken));
            Assert.AreEqual(setupContext.Item2, response);

            executionContext = await TakeItemQueryExecutionComponent.CreateTopDocumentQueryExecutionComponentAsync(
                       5,
                       null,
                       setupContext.Item1);

            response = await executionContext.DrainAsync(1, default(CancellationToken));
            Assert.AreEqual(setupContext.Item2, response);
        }

        private (Func<string, Task<CosmosQueryExecutionComponent>>, CosmosQueryResponse) SetupBaseContextToVerifyFailureScenario()
        {
            Mock<CosmosQueryResponse> mockResponseMessage = new Mock<CosmosQueryResponse>();
            mockResponseMessage.Setup(x => x.IsSuccess).Returns(false);
            // Throw an exception if the context accesses the CosmosElements array
            mockResponseMessage.Setup(x => x.CosmosElements).Throws(new ArgumentException("Context tried to access the Cosmos Elements of a failed response. Context should just return failed response."));

            Mock<CosmosQueryExecutionComponent> baseContext = new Mock<CosmosQueryExecutionComponent>();
            baseContext.Setup(x => x.DrainAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult<CosmosQueryResponse>(mockResponseMessage.Object));
            Func<string, Task<CosmosQueryExecutionComponent>> callBack = x => Task.FromResult<CosmosQueryExecutionComponent>(baseContext.Object);
            return (callBack, mockResponseMessage.Object);
        }
    }
}