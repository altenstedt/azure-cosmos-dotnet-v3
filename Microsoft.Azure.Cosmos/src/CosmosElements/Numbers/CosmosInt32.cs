﻿//-----------------------------------------------------------------------
// <copyright file="CosmosInt32.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.CosmosElements
{
    using System;
    using Microsoft.Azure.Cosmos.Json;

    internal abstract partial class CosmosInt32 : CosmosNumber
    {
        protected CosmosInt32()
            : base(CosmosNumberType.Int32)
        {
        }

        public override bool IsFloatingPoint => false;

        public override bool IsInteger => true;

        public static CosmosInt32 Create(
            IJsonNavigator jsonNavigator,
            IJsonNavigatorNode jsonNavigatorNode)
        {
            return new LazyCosmosInt32(jsonNavigator, jsonNavigatorNode);
        }

        public static CosmosInt32 Create(int number)
        {
            return new EagerCosmosInt32(number);
        }

        public override double? AsFloatingPoint()
        {
            return (double)this.GetValue();
        }

        public override long? AsInteger()
        {
            return this.GetValue();
        }

        public override void WriteTo(IJsonWriter jsonWriter)
        {
            if (jsonWriter == null)
            {
                throw new ArgumentNullException($"{nameof(jsonWriter)}");
            }

            jsonWriter.WriteInt32Value(this.GetValue());
        }

        protected abstract int GetValue();
    }
}
