﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Query.ExecutionComponent
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Internal;
    using Microsoft.Azure.Documents;
    using Newtonsoft.Json;

    internal sealed class TakeDocumentQueryExecutionComponent : DocumentQueryExecutionComponentBase
    {
        private readonly TakeEnum takeEnum;
        private int takeCount;

        private TakeDocumentQueryExecutionComponent(IDocumentQueryExecutionComponent source, int takeCount, TakeEnum takeEnum)
            : base(source)
        {
            if (takeCount < 0)
            {
                throw new ArgumentException($"{nameof(takeCount)} must be a non negative number.");
            }

            this.takeCount = takeCount;
            this.takeEnum = takeEnum;
        }

        public static async Task<TakeDocumentQueryExecutionComponent> CreateLimitDocumentQueryExecutionComponentAsync(
            int limitCount,
            string continuationToken,
            Func<string, Task<IDocumentQueryExecutionComponent>> createSourceCallback)
        {
            LimitContinuationToken limitContinuationToken;
            if (continuationToken != null)
            {
                limitContinuationToken = LimitContinuationToken.Parse(continuationToken);
            }
            else
            {
                limitContinuationToken = new LimitContinuationToken(limitCount, null);
            }

            if (limitContinuationToken.Limit > limitCount)
            {
                throw new BadRequestException($"limit count in continuation token: {limitContinuationToken.Limit} can not be greater than the limit count in the query: {limitCount}.");
            }

            return new TakeDocumentQueryExecutionComponent(
                await createSourceCallback(limitContinuationToken.SourceToken),
                limitContinuationToken.Limit,
                TakeEnum.Limit);
        }

        public static async Task<TakeDocumentQueryExecutionComponent> CreateTopDocumentQueryExecutionComponentAsync(
            int topCount,
            string continuationToken,
            Func<string, Task<IDocumentQueryExecutionComponent>> createSourceCallback)
        {
            TopContinuationToken topContinuationToken;
            if (continuationToken != null)
            {
                topContinuationToken = TopContinuationToken.Parse(continuationToken);
            }
            else
            {
                topContinuationToken = new TopContinuationToken(topCount, null);
            }

            if (topContinuationToken.Top > topCount)
            {
                throw new BadRequestException($"top count in continuation token: {topContinuationToken.Top} can not be greater than the top count in the query: {topCount}.");
            }

            return new TakeDocumentQueryExecutionComponent(
                await createSourceCallback(topContinuationToken.SourceToken),
                topContinuationToken.Top,
                TakeEnum.Top);
        }

        public override bool IsDone
        {
            get
            {
                return this.Source.IsDone || this.takeCount <= 0;
            }
        }

        public override async Task<CosmosQueryResponse> DrainAsync(int maxElements, CancellationToken token)
        {
            CosmosQueryResponse results = await base.DrainAsync(maxElements, token);
            if (!results.IsSuccess)
            {
                return results;
            }

            List<CosmosElement> takedDocuments = results.CosmosElements.Take(this.takeCount).ToList();
            results = new CosmosQueryResponse(
                takedDocuments,
                takedDocuments.Count,
                results.Headers,
                results.UseETagAsContinuation,
                results.DisallowContinuationTokenMessage,
                results.ResponseLengthBytes);

            this.takeCount -= takedDocuments.Count;

            if (results.DisallowContinuationTokenMessage == null)
            {
                if (!this.IsDone)
                {
                    string sourceContinuation = results.ResponseContinuation;
                    TakeContinuationToken takeContinuationToken;
                    switch (this.takeEnum)
                    {
                        case TakeEnum.Limit:
                            takeContinuationToken = new LimitContinuationToken(
                                this.takeCount,
                                sourceContinuation);
                            break;

                        case TakeEnum.Top:
                            takeContinuationToken = new TopContinuationToken(
                                this.takeCount,
                                sourceContinuation);
                            break;

                        default:
                            throw new ArgumentException($"Unknown {nameof(TakeEnum)}: {this.takeEnum}");
                    }

                    results.ResponseContinuation = takeContinuationToken.ToString();
                }
                else
                {
                    results.ResponseContinuation = null;
                }
            }

            return results;
        }

        private enum TakeEnum
        {
            Limit,
            Top
        }

        private abstract class TakeContinuationToken
        {
        }

        /// <summary>
        /// A LimitContinuationToken is a composition of a source continuation token and how many items we have left to drain from that source.
        /// </summary>
        private sealed class LimitContinuationToken : TakeContinuationToken
        {
            /// <summary>
            /// Initializes a new instance of the LimitContinuationToken struct.
            /// </summary>
            /// <param name="limit">The limit to the number of document drained for the remainder of the query.</param>
            /// <param name="sourceToken">The continuation token for the source component of the query.</param>
            public LimitContinuationToken(int limit, string sourceToken)
            {
                if (limit < 0)
                {
                    throw new ArgumentException($"{nameof(limit)} must be a non negative number.");
                }

                this.Limit = limit;
                this.SourceToken = sourceToken;
            }

            /// <summary>
            /// Gets the limit to the number of document drained for the remainder of the query.
            /// </summary>
            [JsonProperty("limit")]
            public int Limit
            {
                get;
            }

            /// <summary>
            /// Gets the continuation token for the source component of the query.
            /// </summary>
            [JsonProperty("sourceToken")]
            public string SourceToken
            {
                get;
            }

            /// <summary>
            /// Parses the LimitContinuationToken from it's string form.
            /// </summary>
            /// <param name="value">The string form to parse from.</param>
            /// <returns>The parsed LimitContinuationToken.</returns>
            public static LimitContinuationToken Parse(string value)
            {
                LimitContinuationToken result;
                if (!TryParse(value, out result))
                {
                    throw new BadRequestException($"Invalid LimitContinuationToken: {value}");
                }
                else
                {
                    return result;
                }
            }

            /// <summary>
            /// Tries to parse out the LimitContinuationToken.
            /// </summary>
            /// <param name="value">The value to parse from.</param>
            /// <param name="LimitContinuationToken">The result of parsing out the token.</param>
            /// <returns>Whether or not the LimitContinuationToken was successfully parsed out.</returns>
            public static bool TryParse(string value, out LimitContinuationToken LimitContinuationToken)
            {
                LimitContinuationToken = default(LimitContinuationToken);
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                try
                {
                    LimitContinuationToken = JsonConvert.DeserializeObject<LimitContinuationToken>(value);
                    return true;
                }
                catch (JsonException ex)
                {
                    DefaultTrace.TraceWarning(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} Invalid continuation token {1} for limit~Component, exception: {2}",
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                        value,
                        ex.Message));
                    return false;
                }
            }

            /// <summary>
            /// Gets the string version of the continuation token that can be passed in a response header.
            /// </summary>
            /// <returns>The string version of the continuation token that can be passed in a response header.</returns>
            public override string ToString()
            {
                return JsonConvert.SerializeObject(this);
            }
        }

        /// <summary>
        /// A TopContinuationToken is a composition of a source continuation token and how many items we have left to drain from that source.
        /// </summary>
        private sealed class TopContinuationToken : TakeContinuationToken
        {
            /// <summary>
            /// Initializes a new instance of the TopContinuationToken struct.
            /// </summary>
            /// <param name="top">The limit to the number of document drained for the remainder of the query.</param>
            /// <param name="sourceToken">The continuation token for the source component of the query.</param>
            public TopContinuationToken(int top, string sourceToken)
            {
                this.Top = top;
                this.SourceToken = sourceToken;
            }

            /// <summary>
            /// Gets the limit to the number of document drained for the remainder of the query.
            /// </summary>
            [JsonProperty("top")]
            public int Top
            {
                get;
            }

            /// <summary>
            /// Gets the continuation token for the source component of the query.
            /// </summary>
            [JsonProperty("sourceToken")]
            public string SourceToken
            {
                get;
            }

            /// <summary>
            /// Parses the TopContinuationToken from it's string form.
            /// </summary>
            /// <param name="value">The string form to parse from.</param>
            /// <returns>The parsed TopContinuationToken.</returns>
            public static TopContinuationToken Parse(string value)
            {
                TopContinuationToken result;
                if (!TryParse(value, out result))
                {
                    throw new BadRequestException($"Invalid TopContinuationToken: {value}");
                }
                else
                {
                    return result;
                }
            }

            /// <summary>
            /// Tries to parse out the TopContinuationToken.
            /// </summary>
            /// <param name="value">The value to parse from.</param>
            /// <param name="topContinuationToken">The result of parsing out the token.</param>
            /// <returns>Whether or not the TopContinuationToken was successfully parsed out.</returns>
            public static bool TryParse(string value, out TopContinuationToken topContinuationToken)
            {
                topContinuationToken = default(TopContinuationToken);
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                try
                {
                    topContinuationToken = JsonConvert.DeserializeObject<TopContinuationToken>(value);
                    return true;
                }
                catch (JsonException ex)
                {
                    DefaultTrace.TraceWarning(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} Invalid continuation token {1} for Top~Component, exception: {2}",
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                        value,
                        ex.Message));
                    return false;
                }
            }

            /// <summary>
            /// Gets the string version of the continuation token that can be passed in a response header.
            /// </summary>
            /// <returns>The string version of the continuation token that can be passed in a response header.</returns>
            public override string ToString()
            {
                return JsonConvert.SerializeObject(this);
            }
        }
    }
}