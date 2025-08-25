using System.Linq.Expressions;

namespace ActualChat.Expressions;

public static class ExpressionExt
{
    public static string GetPropertyName<T>(this Expression<Func<T, object>> propertyGetter)
        => propertyGetter.GetMember().Name;

    public static Type GetPropertyType<T>(this Expression<Func<T, object>> propertyGetter)
        => propertyGetter.GetMember().ReturnType();

    public static MemberInfo GetMember<T>(this Expression<Func<T, object>> propertyGetter)
    {
        if (propertyGetter == null)
            throw new ArgumentNullException(nameof(propertyGetter));

        var expression = propertyGetter.Body;
        // Handle the case where the expression is a UnaryExpression (e.g., for value types being boxed to object)
        if (expression is UnaryExpression unaryExpression)
            expression = unaryExpression.Operand;

        // Handle the MemberExpression to get the property name
        if (expression is MemberExpression memberExpression)
            return memberExpression.Member;

        throw new ArgumentException("Invalid expression format. Must be a property access expression.", nameof(propertyGetter));
    }

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
