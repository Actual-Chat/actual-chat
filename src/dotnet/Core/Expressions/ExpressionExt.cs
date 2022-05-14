using System.Linq.Expressions;

namespace ActualChat.Expressions;

public static class ExpressionExt
{
    public static Expression<TOutput> InlineParameter<TInput, TOutput>(
        this Expression<TInput> expression,
        ParameterExpression source,
        Expression target)
        => new ParameterReplacerVisitor<TOutput>(source, target).VisitAndConvert(expression);

    // Private methods & classes

    private class ParameterReplacerVisitor<TOutput> : ExpressionVisitor
    {
        private readonly ParameterExpression _source;
        private readonly Expression _target;

        public ParameterReplacerVisitor(ParameterExpression source, Expression target)
        {
            _source = source;
            _target = target;
        }

        internal Expression<TOutput> VisitAndConvert<T>(Expression<T> root)
            => (Expression<TOutput>)VisitLambda(root);

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            var parameters = node.Parameters.Where(p => p != _source);
            return Expression.Lambda<TOutput>(Visit(node.Body), parameters);
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _source ? _target : base.VisitParameter(node);
    }
}
