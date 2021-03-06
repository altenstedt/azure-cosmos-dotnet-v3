﻿//-----------------------------------------------------------------------------------------------------------------------------------------
// <copyright file="SqlJoinCollectionExpression.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------------------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Sql
{
    using System.Text;

    internal sealed class SqlJoinCollectionExpression : SqlCollectionExpression
    {
        private SqlJoinCollectionExpression(
            SqlCollectionExpression leftExpression,
            SqlCollectionExpression rightExpression)
            : base(SqlObjectKind.JoinCollectionExpression)
        {
            this.LeftExpression = leftExpression;
            this.RightExpression = rightExpression;
        }

        public SqlCollectionExpression LeftExpression
        {
            get;
        }

        public SqlCollectionExpression RightExpression
        {
            get;
        }

        public static SqlJoinCollectionExpression Create(
            SqlCollectionExpression leftExpression,
            SqlCollectionExpression rightExpression)
        {
            return new SqlJoinCollectionExpression(leftExpression, rightExpression);
        }

        public override void Accept(SqlObjectVisitor visitor)
        {
            visitor.Visit(this);
        }
        
        public override TResult Accept<TResult>(SqlObjectVisitor<TResult> visitor)
        {
            return visitor.Visit(this);
        }

        public override TResult Accept<T, TResult>(SqlObjectVisitor<T, TResult> visitor, T input)
        {
            return visitor.Visit(this, input);
        }

        public override void Accept(SqlCollectionExpressionVisitor visitor)
        {
            visitor.Visit(this);
        }

        public override TResult Accept<TResult>(SqlCollectionExpressionVisitor<TResult> visitor)
        {
            return visitor.Visit(this);
        }

        public override TResult Accept<T, TResult>(SqlCollectionExpressionVisitor<T, TResult> visitor, T input)
        {
            return visitor.Visit(this, input);
        }
    }
}
