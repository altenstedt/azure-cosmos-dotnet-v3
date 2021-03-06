﻿//-----------------------------------------------------------------------
// <copyright file="CosmosFloat32.EagerCosmosFloat32.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.CosmosElements
{
    internal abstract partial class CosmosFloat32 : CosmosNumber
    {
        private sealed class EagerCosmosFloat32 : CosmosFloat32
        {
            private readonly float number;

            public EagerCosmosFloat32(float number)
            {
                this.number = number;
            }

            protected override float GetValue()
            {
                return this.number;
            }
        }
    }
}