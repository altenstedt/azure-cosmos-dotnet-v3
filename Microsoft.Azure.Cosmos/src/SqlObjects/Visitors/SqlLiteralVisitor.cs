﻿//-----------------------------------------------------------------------------------------------------------------------------------------
// <copyright file="SqlSelectSpecVisitor.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------------------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Sql
{
    using System;

    internal abstract class SqlLiteralVisitor
    {
        public abstract void Visit(SqlBooleanLiteral literal);
        public abstract void Visit(SqlNullLiteral literal);
        public abstract void Visit(SqlNumberLiteral literal);
        public abstract void Visit(SqlObjectLiteral literal);
        public abstract void Visit(SqlStringLiteral literal);
        public abstract void Visit(SqlUndefinedLiteral literal);
    }

    internal abstract class SqlLiteralVisitor<TResult>
    {
        public abstract TResult Visit(SqlBooleanLiteral literal);
        public abstract TResult Visit(SqlNullLiteral literal);
        public abstract TResult Visit(SqlNumberLiteral literal);
        public abstract TResult Visit(SqlObjectLiteral literal);
        public abstract TResult Visit(SqlStringLiteral literal);
        public abstract TResult Visit(SqlUndefinedLiteral literal);
    }
}
