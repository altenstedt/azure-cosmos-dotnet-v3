﻿//-----------------------------------------------------------------------
// <copyright file="CosmosPipelinedItemQueryExecutionContext.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Query
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Internal;
    using Microsoft.Azure.Cosmos.Query.ExecutionComponent;
    using Microsoft.Azure.Documents;

    /// <summary>
    /// You can imagine the pipeline to be a directed acyclic graph where documents flow from multiple sources (the partitions) to a single sink (the client who calls on ExecuteNextAsync()).
    /// The pipeline will consist of individual implementations of <see cref="IDocumentQueryExecutionContext"/>. 
    /// Every member of the pipeline has a source of documents (another member of the pipeline or an actual partition),
    /// a method of draining documents (DrainAsync()) from said source, and a flag for whether that member of the pipeline is completely drained.
    /// <para>
    /// The following is a diagram of the pipeline:
    ///     +--------------------------+    +--------------------------+    +--------------------------+
    ///     |                          |    |                          |    |                          |
    ///     | Document Producer Tree 0 |    | Document Producer Tree 1 |    | Document Producer Tree N |
    ///     |                          |    |                          |    |                          |
    ///     +--------------------------+    +--------------------------+    +--------------------------+
    ///                   |                               |                               |           
    ///                    \                              |                              /
    ///                     \                             |                             /
    ///                      +---------------------------------------------------------+
    ///                      |                                                         |
    ///                      |   Parallel / Order By Document Query Execution Context  |
    ///                      |                                                         |
    ///                      +---------------------------------------------------------+
    ///                                                   |
    ///                                                   |
    ///                                                   |
    ///                         +---------------------------------------------------+
    ///                         |                                                   |
    ///                         |    Aggregate Document Query Execution Component   |
    ///                         |                                                   |
    ///                         +---------------------------------------------------+
    ///                                                   |
    ///                                                   |
    ///                                                   |
    ///                             +------------------------------------------+
    ///                             |                                          |
    ///                             |  Top Document Query Execution Component  |
    ///                             |                                          |
    ///                             +------------------------------------------+
    ///                                                   |
    ///                                                   |
    ///                                                   |
    ///                                    +-----------------------------+
    ///                                    |                             |
    ///                                    |            Client           |
    ///                                    |                             |
    ///                                    +-----------------------------+
    /// </para>    
    /// <para>
    /// This class is responsible for constructing the pipelined described.
    /// Note that the pipeline will always have one of <see cref="OrderByDocumentQueryExecutionContext"/> or <see cref="ParallelDocumentQueryExecutionContext"/>,
    /// which both derive from <see cref="CrossPartitionQueryExecutionContext"/> as these are top level execution contexts.
    /// These top level execution contexts have <see cref="DocumentProducerTree"/> that are responsible for hitting the backend
    /// and will optionally feed into <see cref="AggregateDocumentQueryExecutionComponent"/> and <see cref="TakeDocumentQueryExecutionComponent"/>.
    /// How these components are picked is based on <see cref="PartitionedQueryExecutionInfo"/>,
    /// which is a serialized form of this class and serves as a blueprint for construction.
    /// </para>
    /// <para>
    /// Once the pipeline is constructed the client(sink of the graph) calls ExecuteNextAsync() which calls on DrainAsync(),
    /// which by definition grabs documents from the parent component of the pipeline.
    /// This bubbles down until you reach a component that has a DocumentProducer that fetches a document from the backend.
    /// </para>
    /// </summary>
    internal sealed class CosmosPipelinedItemQueryExecutionContext : CosmosQueryExecutionContext
    {
        /// <summary>
        /// The root level component that all calls will be forwarded to.
        /// </summary>
        private readonly CosmosQueryExecutionComponent component;

        /// <summary>
        /// The actual page size to drain.
        /// </summary>
        private readonly int actualPageSize;

        /// <summary>
        /// Initializes a new instance of the CosmosPipelinedItemQueryExecutionContext class.
        /// </summary>
        /// <param name="component">The root level component that all calls will be forwarded to.</param>
        /// <param name="actualPageSize">The actual page size to drain.</param>
        private CosmosPipelinedItemQueryExecutionContext(
            CosmosQueryExecutionComponent component,
            int actualPageSize)
        {
            if (component == null)
            {
                throw new ArgumentNullException($"{nameof(component)} can not be null.");
            }

            if (actualPageSize < 0)
            {
                throw new ArgumentException($"{nameof(actualPageSize)} can not be negative.");
            }

            this.component = component;
            this.actualPageSize = actualPageSize;
        }

        /// <summary>
        /// Gets a value indicating whether this execution context is done draining documents.
        /// </summary>
        internal override bool IsDone
        {
            get
            {
                return this.component.IsDone;
            }
        }

        /// <summary>
        /// Creates a CosmosPipelinedItemQueryExecutionContext.
        /// </summary>
        /// <param name="constructorParams">The parameters for constructing the base class.</param>
        /// <param name="collectionRid">The collection rid.</param>
        /// <param name="partitionedQueryExecutionInfo">The partitioned query execution info.</param>
        /// <param name="partitionKeyRanges">The partition key ranges.</param>
        /// <param name="initialPageSize">The initial page size.</param>
        /// <param name="requestContinuation">The request continuation.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task to await on, which in turn returns a CosmosPipelinedItemQueryExecutionContext.</returns>
        public static async Task<CosmosQueryExecutionContext> CreateAsync(
            CosmosQueryContext constructorParams,
            string collectionRid,
            PartitionedQueryExecutionInfo partitionedQueryExecutionInfo,
            List<PartitionKeyRange> partitionKeyRanges,
            int initialPageSize,
            string requestContinuation,
            CancellationToken cancellationToken)
        {
            DefaultTrace.TraceInformation(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}, CorrelatedActivityId: {1} | Pipelined~Context.CreateAsync",
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    constructorParams.CorrelatedActivityId));

            Func<string, Task<CosmosQueryExecutionComponent>> createComponentFunc;
            QueryInfo queryInfo = partitionedQueryExecutionInfo.QueryInfo;
            if (queryInfo.HasOrderBy)
            {
                createComponentFunc = async (continuationToken) =>
                {
                    CosmosCrossPartitionQueryExecutionContext.CrossPartitionInitParams initParams = new CosmosCrossPartitionQueryExecutionContext.CrossPartitionInitParams(
                        collectionRid,
                        partitionedQueryExecutionInfo,
                        partitionKeyRanges,
                        initialPageSize,
                        continuationToken);

                    return await CosmosOrderByItemQueryExecutionContext.CreateAsync(
                        constructorParams,
                        initParams,
                        cancellationToken);
                };
            }
            else
            {
                createComponentFunc = async (continuationToken) =>
                {
                    CosmosCrossPartitionQueryExecutionContext.CrossPartitionInitParams initParams = new CosmosCrossPartitionQueryExecutionContext.CrossPartitionInitParams(
                        collectionRid,
                        partitionedQueryExecutionInfo,
                        partitionKeyRanges,
                        initialPageSize,
                        continuationToken);

                    return await CosmosParallelItemQueryExecutionContext.CreateAsync(
                        constructorParams,
                        initParams,
                        cancellationToken);
                };
            }

            if (queryInfo.HasAggregates)
            {
                Func<string, Task<CosmosQueryExecutionComponent>> createSourceCallback = createComponentFunc;
                createComponentFunc = async (continuationToken) =>
                {
                    return await AggregateItemQueryExecutionComponent.CreateAsync(
                        queryInfo.Aggregates,
                        continuationToken,
                        createSourceCallback);
                };
            }

            if (queryInfo.HasDistinct)
            {
                Func<string, Task<CosmosQueryExecutionComponent>> createSourceCallback = createComponentFunc;
                createComponentFunc = async (continuationToken) =>
                {
                    return await DistinctItemQueryExecutionComponent.CreateAsync(
                        continuationToken,
                        createSourceCallback,
                        queryInfo.DistinctType);
                };
            }

            if (queryInfo.HasOffset)
            {
                if (!constructorParams.QueryRequestOptions.EnableCrossPartitionSkipTake)
                {
                    throw new ArgumentException("Cross Partition OFFSET / LIMIT is not supported.");
                }

                Func<string, Task<IDocumentQueryExecutionComponent>> createSourceCallback = createComponentFunc;
                createComponentFunc = async (continuationToken) =>
                {
                    return await SkipItemQueryExecutionComponent.CreateAsync(
                        queryInfo.Offset.Value,
                        continuationToken,
                        createSourceCallback);
                };
            }

            if (queryInfo.HasLimit)
            {
                if (!constructorParams.QueryRequestOptions.EnableCrossPartitionSkipTake)
                {
                    throw new ArgumentException("Cross Partition OFFSET / LIMIT is not supported.");
                }

                Func<string, Task<IDocumentQueryExecutionComponent>> createSourceCallback = createComponentFunc;
                createComponentFunc = async (continuationToken) =>
                {
                    return await TakeItemQueryExecutionComponent.CreateLimitDocumentQueryExecutionComponentAsync(
                        queryInfo.Limit.Value,
                        continuationToken,
                        createSourceCallback);
                };
            }

            if (queryInfo.HasTop)
            {
                Func<string, Task<CosmosQueryExecutionComponent>> createSourceCallback = createComponentFunc;
                createComponentFunc = async (continuationToken) =>
                {
                    return await TakeItemQueryExecutionComponent.CreateTopDocumentQueryExecutionComponentAsync(
                        queryInfo.Top.Value,
                        continuationToken,
                        createSourceCallback);
                };
            }

            return new CosmosPipelinedItemQueryExecutionContext(
                await createComponentFunc(requestContinuation), initialPageSize);
        }

        /// <summary>
        /// Disposes of this context.
        /// </summary>
        public override void Dispose()
        {
            this.component.Dispose();
        }

        /// <summary>
        /// Gets the next page of results from this context.
        /// </summary>
        /// <param name="token">The cancellation token.</param>
        /// <returns>A task to await on that in turn returns a FeedResponse of results.</returns>
        internal override async Task<CosmosQueryResponse> ExecuteNextAsync(CancellationToken token)
        {
            try
            {
                return await this.component.DrainAsync(this.actualPageSize, token);
            }
            catch (Exception)
            {
                this.component.Stop();
                throw;
            }
        }
    }
}
