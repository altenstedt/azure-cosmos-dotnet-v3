﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Linq
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Query;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Collections;
    using Newtonsoft.Json;

    internal sealed class DocumentQuery<T> : IDocumentQuery<T>, IOrderedQueryable<T>
    {
        public static readonly FeedResponse<dynamic> EmptyFeedResponse = new FeedResponse<dynamic>(Enumerable.Empty<dynamic>(), 0, new StringKeyValueCollection());

        private readonly IDocumentQueryClient client;
        private readonly ResourceType resourceTypeEnum;
        private readonly Type resourceType;
        private readonly string documentsFeedOrDatabaseLink;
        private readonly FeedOptions feedOptions;
        private readonly object partitionKey;

        private readonly Expression expression;
        private readonly DocumentQueryProvider queryProvider;
        private readonly SchedulingStopwatch executeNextAysncMetrics;
        private readonly Guid correlatedActivityId;

        private IDocumentQueryExecutionContext queryExecutionContext;

        private bool tracedFirstExecution;
        private bool tracedLastExecution;


        // Root Query.
        public DocumentQuery(
            IDocumentQueryClient client,
            ResourceType resourceTypeEnum,
            Type resourceType,
            string documentsFeedOrDatabaseLink,
            Expression expression,
            FeedOptions feedOptions,
            object partitionKey = null)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            this.client = client;
            this.resourceTypeEnum = resourceTypeEnum;
            this.resourceType = resourceType;
            this.documentsFeedOrDatabaseLink = documentsFeedOrDatabaseLink;
            this.feedOptions = feedOptions == null ? new FeedOptions() : new FeedOptions(feedOptions);

            // Swapping out negative values in feedOptions for int.MaxValue
            if (this.feedOptions.MaxBufferedItemCount < 0)
            {
                this.feedOptions.MaxBufferedItemCount = int.MaxValue;
            }

            if (this.feedOptions.MaxDegreeOfParallelism < 0)
            {
                this.feedOptions.MaxDegreeOfParallelism = int.MaxValue;
            }

            if (this.feedOptions.MaxItemCount < 0)
            {
                this.feedOptions.MaxItemCount = int.MaxValue;
            }

            this.partitionKey = partitionKey;

            this.expression = expression ?? Expression.Constant(this);
            this.queryProvider = new DocumentQueryProvider(
                client,
                resourceTypeEnum,
                resourceType,
                documentsFeedOrDatabaseLink,
                feedOptions,
                partitionKey,
                this.client.OnExecuteScalarQueryCallback);
            this.executeNextAysncMetrics = new SchedulingStopwatch();
            this.executeNextAysncMetrics.Ready();
            this.correlatedActivityId = Guid.NewGuid();
        }

        public DocumentQuery(
            DocumentClient client,
            ResourceType resourceTypeEnum,
            Type resourceType,
            string documentsFeedOrDatabaseLink,
            Expression expression,
            FeedOptions feedOptions,
            object partitionKey = null) :
            this(
                new DocumentQueryClient(client),
                resourceTypeEnum,
                resourceType,
                documentsFeedOrDatabaseLink,
                expression,
                feedOptions,
                partitionKey)
        {
        }

        public DocumentQuery(
            IDocumentQueryClient client,
            ResourceType resourceTypeEnum,
            Type resourceType,
            string documentsFeedOrDatabaseLink,
            FeedOptions feedOptions,
            object partitionKey = null) :
            this(
                client,
                resourceTypeEnum,
                resourceType,
                documentsFeedOrDatabaseLink,
                null,
                feedOptions,
                partitionKey)
        {
        }

        public DocumentQuery(
            DocumentClient client,
            ResourceType resourceTypeEnum,
            Type resourceType,
            string documentsFeedOrDatabaseLink,
            FeedOptions feedOptions,
            object partitionKey = null) :
            this(
                new DocumentQueryClient(client),
                resourceTypeEnum,
                resourceType,
                documentsFeedOrDatabaseLink,
                null,
                feedOptions,
                partitionKey)
        {
        }

        public Type ElementType
        {
            get { return typeof(T); }
        }

        public Expression Expression
        {
            get { return this.expression; }
        }

        public IQueryProvider Provider
        {
            get { return this.queryProvider; }
        }

        /// <summary>
        /// Gets a value indicating whether there are additional results to retrieve. 
        /// </summary>
        public bool HasMoreResults
        {
            get
            {
                return this.queryExecutionContext == null || !this.queryExecutionContext.IsDone;
            }
        }

        /// <summary>
        /// Gets the unique ID for this instance of DocumentQuery used to correlate all activityIds generated when fetching from a partition collection.
        /// </summary>
        public Guid CorrelatedActivityId
        {
            get
            {
                return this.correlatedActivityId;
            }
        }

        public void Dispose()
        {
            if (this.queryExecutionContext != null)
            {
                this.queryExecutionContext.Dispose();
                DefaultTrace.TraceInformation(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}, CorrelatedActivityId: {1} | Disposing DocumentQuery",
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                        this.CorrelatedActivityId));
            }
        }

        /// <summary>
        /// Executes the query to retrieve the next page of results.
        /// </summary>
        /// <returns></returns>        
        public Task<FeedResponse<dynamic>> ExecuteNextAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.ExecuteNextAsync<dynamic>(cancellationToken);
        }

        /// <summary>
        /// Executes the query to retrieve the next page of results.
        /// </summary>
        /// <returns></returns>
        public Task<CosmosQueryResponse> ExecuteNextQueryStreamAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (!tracedFirstExecution)
                {
                    DefaultTrace.TraceInformation(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}, CorrelatedActivityId: {1} | First ExecuteNextAsync",
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                        this.CorrelatedActivityId));
                    tracedFirstExecution = true;
                }

                this.executeNextAysncMetrics.Start();
                return TaskHelper.InlineIfPossible<CosmosQueryResponse>(() => this.ExecuteNextQueryStreamPrivateAsync(cancellationToken), null, cancellationToken);
            }
            finally
            {
                this.executeNextAysncMetrics.Stop();
                if (!this.HasMoreResults && !tracedLastExecution)
                {
                    DefaultTrace.TraceInformation(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}, CorrelatedActivityId: {1} | Last ExecuteNextAsync with ExecuteNextAsyncMetrics: [{2}]",
                            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                            this.CorrelatedActivityId,
                            this.executeNextAysncMetrics));
                    tracedLastExecution = true;
                }
            }
        }

        /// <summary>
        /// Executes the query to retrieve the next page of results.
        /// </summary>
        /// <returns></returns>
        public Task<FeedResponse<TResponse>> ExecuteNextAsync<TResponse>(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (!tracedFirstExecution)
                {
                    DefaultTrace.TraceInformation(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}, CorrelatedActivityId: {1} | First ExecuteNextAsync",
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                        this.CorrelatedActivityId));
                    tracedFirstExecution = true;
                }

                this.executeNextAysncMetrics.Start();
                return TaskHelper.InlineIfPossible(() => this.ExecuteNextPrivateAsync<TResponse>(cancellationToken), null, cancellationToken);
            }
            finally
            {
                this.executeNextAysncMetrics.Stop();
                if (!this.HasMoreResults && !tracedLastExecution)
                {
                    DefaultTrace.TraceInformation(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}, CorrelatedActivityId: {1} | Last ExecuteNextAsync with ExecuteNextAsyncMetrics: [{2}]",
                            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                            this.CorrelatedActivityId,
                            this.executeNextAysncMetrics));
                    tracedLastExecution = true;
                }
            }
        }

        /// <summary>
        /// Retrieves an object that can iterate through the individual results of the query.
        /// </summary>
        /// <remarks>
        /// This triggers a synchronous multi-page load.
        /// </remarks>
        /// <returns></returns>
        public IEnumerator<T> GetEnumerator()
        {
            using (IDocumentQueryExecutionContext localQueryExecutionContext =
                TaskHelper.InlineIfPossible(() => this.CreateDocumentQueryExecutionContextAsync(false, CancellationToken.None), null).Result)
            {
                while (!localQueryExecutionContext.IsDone)
                {
                    FeedResponse<CosmosElement> feedResponse = TaskHelper.InlineIfPossible(() => localQueryExecutionContext.ExecuteNextAsync(CancellationToken.None), null).Result;
                    FeedResponse<T> typedFeedResponse = FeedResponseBinder.ConvertCosmosElementFeed<T>(
                        feedResponse, 
                        this.resourceTypeEnum,
                        this.feedOptions.JsonSerializerSettings);
                    foreach (T item in typedFeedResponse)
                    {
                        yield return item;
                    }
                }
            }
        }

        /// <summary>
        /// Synchronous Multi-Page load
        /// </summary>
        /// <returns></returns>        
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public override string ToString()
        {
            SqlQuerySpec querySpec = DocumentQueryEvaluator.Evaluate(this.expression);
            if (querySpec != null)
            {
                return JsonConvert.SerializeObject(querySpec);
            }

            return new Uri(this.client.ServiceEndpoint, this.documentsFeedOrDatabaseLink).ToString();
        }

        private Task<IDocumentQueryExecutionContext> CreateDocumentQueryExecutionContextAsync(bool isContinuationExpected, CancellationToken cancellationToken)
        {
            return DocumentQueryExecutionContextFactory.CreateDocumentQueryExecutionContextAsync(
                this.client,
                this.resourceTypeEnum,
                this.resourceType,
                this.expression,
                this.feedOptions,
                this.documentsFeedOrDatabaseLink,
                isContinuationExpected,
                cancellationToken,
                this.CorrelatedActivityId);
        }

        internal async Task<List<T>> ExecuteAllAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            List<T> result = new List<T>();
            using (IDocumentQueryExecutionContext localQueryExecutionContext =
                await TaskHelper.InlineIfPossible(() => this.CreateDocumentQueryExecutionContextAsync(false, cancellationToken), null, cancellationToken))
            {
                while (!localQueryExecutionContext.IsDone)
                {
                    FeedResponse<T> partialResult = await (dynamic)TaskHelper.InlineIfPossible(() => localQueryExecutionContext.ExecuteNextAsync(cancellationToken), null, cancellationToken);
                    result.AddRange(partialResult);
                }
            }

            return result;
        }

        private async Task<CosmosQueryResponse> ExecuteNextQueryStreamPrivateAsync(CancellationToken cancellationToken)
        {
            if (this.queryExecutionContext == null)
            {
                this.queryExecutionContext = await this.CreateDocumentQueryExecutionContextAsync(true, cancellationToken);
            }
            else if (this.queryExecutionContext.IsDone)
            {
                this.queryExecutionContext.Dispose();
                this.queryExecutionContext = await this.CreateDocumentQueryExecutionContextAsync(true, cancellationToken);
            }

            FeedResponse<CosmosElement> response = await this.queryExecutionContext.ExecuteNextAsync(cancellationToken);
            CosmosQueryResponse typedFeedResponse = FeedResponseBinder.ConvertToCosmosQueryResponse(
                       response,
                       this.feedOptions.CosmosSerializationOptions);

            if (!this.HasMoreResults && !tracedLastExecution)
            {
                DefaultTrace.TraceInformation(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}, CorrelatedActivityId: {1} | Last ExecuteNextAsync with ExecuteNextAsyncMetrics: [{2}]",
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                        this.CorrelatedActivityId,
                        this.executeNextAysncMetrics));
                tracedLastExecution = true;
            }
            return typedFeedResponse;
        }

        private async Task<FeedResponse<TResponse>> ExecuteNextPrivateAsync<TResponse>(CancellationToken cancellationToken)
        {
            if (this.queryExecutionContext == null)
            {
                this.queryExecutionContext = await this.CreateDocumentQueryExecutionContextAsync(true, cancellationToken);
            }
            else if (this.queryExecutionContext.IsDone)
            {
                this.queryExecutionContext.Dispose();
                this.queryExecutionContext = await this.CreateDocumentQueryExecutionContextAsync(true, cancellationToken);
            }

            FeedResponse<CosmosElement> response = await this.queryExecutionContext.ExecuteNextAsync(cancellationToken);
            FeedResponse<TResponse> typedFeedResponse = FeedResponseBinder.ConvertCosmosElementFeed<TResponse>(
                response, 
                this.resourceTypeEnum,
                this.feedOptions.JsonSerializerSettings);

            if (!this.HasMoreResults && !tracedLastExecution)
            {
                DefaultTrace.TraceInformation(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}, CorrelatedActivityId: {1} | Last ExecuteNextAsync with ExecuteNextAsyncMetrics: [{2}]",
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                        this.CorrelatedActivityId,
                        this.executeNextAysncMetrics));
                tracedLastExecution = true;
            }
            return typedFeedResponse;
        }
    }
}