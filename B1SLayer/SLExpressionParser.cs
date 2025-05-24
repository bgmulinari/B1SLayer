using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace B1SLayer;

internal class SLExpressionParser : ExpressionVisitor
{
    private const int MaxCacheSize = 1000;
    private static readonly HashSet<string> ValidMethods = ["Equals", "Contains", "StartsWith", "EndsWith"];
    private static readonly ConcurrentDictionary<ExpressionCacheKey, string> Cache = new();

    private readonly StringBuilder _builder = new();

    /// <summary>
    ///     Entry point to parse a predicate expression into a query string.
    /// </summary>
    public string Parse<T>(Expression<Func<T, bool>> expr)
    {
        if (expr == null)
            throw new ArgumentNullException(nameof(expr));

        var cacheKey = new ExpressionCacheKey(expr);

        if (Cache.TryGetValue(cacheKey, out var cachedResult))
            return cachedResult;

        var parser = new SLExpressionParser();
        parser.Visit(expr.Body);
        var result = parser._builder.ToString();

        switch (Cache.Count)
        {
            case < MaxCacheSize:
                Cache.TryAdd(cacheKey, result);
                break;
            case >= MaxCacheSize when !Cache.ContainsKey(cacheKey):
                // Simple cache eviction: clear half the cache when full
                ClearHalfCache();
                Cache.TryAdd(cacheKey, result);
                break;
        }

        return result;
    }

    public string Parse<T>(Expression<Func<T, object>> expr)
    {
        if (expr == null)
            throw new ArgumentNullException(nameof(expr));

        var cacheKey = new ExpressionCacheKey(expr);

        if (Cache.TryGetValue(cacheKey, out var cachedResult))
            return cachedResult;

        var parser = new SLExpressionParser();
        parser.Visit(expr.Body);
        var result = parser._builder.ToString();

        switch (Cache.Count)
        {
            case < MaxCacheSize:
                Cache.TryAdd(cacheKey, result);
                break;
            case >= MaxCacheSize when !Cache.ContainsKey(cacheKey):
                // Simple cache eviction: clear half the cache when full
                ClearHalfCache();
                Cache.TryAdd(cacheKey, result);
                break;
        }

        return result;
    }

    private static void ClearHalfCache()
    {
        var keysToRemove = Cache.Keys.Take(Cache.Count / 2).ToList();
        foreach (var key in keysToRemove)
        {
            Cache.TryRemove(key, out _);
        }
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.OrElse)
            _builder.Append("(");

        Visit(node.Left);
        _builder.Append($" {GetOperator(node.NodeType)} ");
        Visit(node.Right);

        if (node.NodeType == ExpressionType.OrElse)
            _builder.Append(")");

        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // If this method call doesn't reference the query parameter, treat it as a constant
        if (!ContainsParameter(node))
        {
            var value = EvaluateExpression(node);
            _builder.Append(FormatConstant(value));
            return node;
        }

        // Handle the pattern where Contains is used on a collection.
        if (node.Method.Name == "Contains" &&
            node.Arguments.Count is 1 or 2 &&
            node.Object?.Type != typeof(string))
        {
            var evaluatedValue = EvaluateExpression(node.Object ?? node.Arguments[0]);
            if (evaluatedValue == null)
                throw new ArgumentException("Cannot evaluate a 'Contains' operation on a null collection");

            if (evaluatedValue is IEnumerable collection)
            {
                var items = collection.Cast<object>().ToList();
                if (items.Count == 0)
                    throw new ArgumentException("Cannot evaluate a 'Contains' operation on an empty collection");

                var propertyExpr = node.Arguments.Last();
                var propertyName = EvaluateAndConvert(propertyExpr);
                var conditionsList = items
                    .Select(item => $"{propertyName} eq {FormatConstant(item)}")
                    .ToList();
                var conditions = conditionsList.Count > 1
                    ? $"({string.Join(" or ", conditionsList)})"
                    : conditionsList.First();
                _builder.Append(conditions);
                return node;
            }

            throw new NotSupportedException($"Expected IEnumerable but got {evaluatedValue.GetType()}");
        }

        if (node.Object is null || !ValidMethods.Contains(node.Method.Name) || node.Arguments.Count > 1)
            throw new UnsupportedMethodCallException(node);

        var member = EvaluateAndConvert(node.Object);
        var argument = EvaluateAndConvert(node.Arguments[0]);
        var function = node.Method.Name switch
        {
            "Equals" => $"{member} eq {argument}",
            "Contains" => $"contains({member}, {argument})",
            "StartsWith" => $"startswith({member}, {argument})",
            "EndsWith" => $"endswith({member}, {argument})",
            _ => throw new UnsupportedMethodCallException(node)
        };
        _builder.Append(function);
        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            _builder.Append("not (");
            Visit(node.Operand);
            _builder.Append(")");
        }
        else
            Visit(node.Operand);

        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // If the member comes directly from the lambda parameter, output its name using JsonPropertyName if available.
        if (node.Expression?.NodeType == ExpressionType.Parameter)
        {
            var jsonPropertyNameAttr = node.Member.GetCustomAttribute<JsonPropertyNameAttribute>();
            _builder.Append(jsonPropertyNameAttr is not null
                ? jsonPropertyNameAttr.Name
                : node.Member.Name);
        }
        else
        {
            var value = EvaluateExpression(node);
            _builder.Append(FormatConstant(value));
        }

        return node;
    }

    protected override Expression VisitNew(NewExpression node)
    {
        if (node.Members?.Count > 0)
        {
            for (var i = 0; i < node.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    _builder.Append(",");
                }

                Visit(node.Arguments[i]);
            }
        }
        else
        {
            foreach (var arg in node.Arguments)
                Visit(arg);
        }

        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        // Check if this is a char type constant
        if (node.Type == typeof(char))
        {
            // The value might be boxed as int in some expression trees
            var charValue = node.Value switch
            {
                char c => c,
                int i => (char)i,
                _ => node.Value
            };
            _builder.Append(FormatConstant(charValue));
        }
        else
        {
            _builder.Append(FormatConstant(node.Value));
        }

        return node;
    }

    private static string GetOperator(ExpressionType type)
    {
        return type switch
        {
            ExpressionType.AndAlso => "and",
            ExpressionType.OrElse => "or",
            ExpressionType.Equal => "eq",
            ExpressionType.NotEqual => "ne",
            ExpressionType.GreaterThan => "gt",
            ExpressionType.GreaterThanOrEqual => "ge",
            ExpressionType.LessThan => "lt",
            ExpressionType.LessThanOrEqual => "le",
            _ => throw new NotSupportedException($"Unsupported operator: {type}")
        };
    }

    private static object EvaluateExpression(Expression expr)
    {
        try
        {
            var lambda = Expression.Lambda(expr);
            var compiled = lambda.Compile();
            return compiled.DynamicInvoke();
        }
        catch (Exception ex)
        {
            throw new NotSupportedException($"Unsupported expression: {expr}", ex);
        }
    }

    private static string EvaluateAndConvert(Expression expr)
    {
        if (ContainsParameter(expr))
        {
            if (expr is not MemberExpression me || me.Expression?.NodeType != ExpressionType.Parameter)
                throw new InvalidOperationException("Expression contains a parameter and cannot be evaluated.");

            var jsonPropertyNameAttr = me.Member.GetCustomAttribute<JsonPropertyNameAttribute>();
            return jsonPropertyNameAttr is not null
                ? jsonPropertyNameAttr.Name
                : me.Member.Name;
        }

        var value = EvaluateExpression(expr);
        return FormatConstant(value);
    }

    private static string FormatConstant(object value)
    {
        return value switch
        {
            string s => $"'{s}'",
            char c => $"'{c}'",
            DateTime dt => $"'{dt:yyyy-MM-ddTHH:mm:ss}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-ddTHH:mm:sszzz}'",
            bool b => b.ToString().ToLower(),
            null => "null",
            _ => value?.ToString() ?? "null"
        };
    }

    private static bool ContainsParameter(Expression expr)
    {
        // Use iterative approach with explicit stack to avoid stack overflow
        var stack = new Stack<Expression>();
        stack.Push(expr);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == null)
                continue;

            if (current is ParameterExpression)
                return true;

            switch (current)
            {
                case MemberExpression member:
                    stack.Push(member.Expression);
                    break;
                case BinaryExpression binary:
                    stack.Push(binary.Left);
                    stack.Push(binary.Right);
                    break;
                case UnaryExpression unary:
                    stack.Push(unary.Operand);
                    break;
                case MethodCallExpression methodCall:
                    if (methodCall.Object != null)
                    {
                        stack.Push(methodCall.Object);
                    }

                    foreach (var arg in methodCall.Arguments)
                        stack.Push(arg);
                    break;
            }
        }

        return false;
    }

    private sealed class UnsupportedMethodCallException(MethodCallExpression node)
        : NotSupportedException($"Unsupported method call: {node?.Method.DeclaringType?.Name}.{node?.Method.Name} with {node?.Arguments.Count ?? 0} arguments");

    /// <summary>
    ///     Cache key for expressions that provides proper equality comparison
    /// </summary>
    private sealed class ExpressionCacheKey : IEquatable<ExpressionCacheKey>
    {
        private readonly Expression _expression;
        private readonly int _hashCode;

        public ExpressionCacheKey(Expression expression)
        {
            _expression = expression ?? throw new ArgumentNullException(nameof(expression));
            _hashCode = ExpressionEqualityComparer.Instance.GetHashCode(_expression);
        }

        public bool Equals(ExpressionCacheKey other)
        {
            if (other is null) return false;

            return ReferenceEquals(this, other) || ExpressionEqualityComparer.Instance.Equals(_expression, other._expression);
        }

        public override bool Equals(object obj)
        {
            return obj is ExpressionCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }
    }

    /// <summary>
    ///     Compares expressions for structural equality
    /// </summary>
    private sealed class ExpressionEqualityComparer : IEqualityComparer<Expression>
    {
        public static readonly ExpressionEqualityComparer Instance = new();

        public bool Equals(Expression x, Expression y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.NodeType != y.NodeType || x.Type != y.Type) return false;

            return x switch
            {
                BinaryExpression bx when y is BinaryExpression by =>
                    bx.Method == by.Method &&
                    Equals(bx.Left, by.Left) &&
                    Equals(bx.Right, by.Right),

                UnaryExpression ux when y is UnaryExpression uy =>
                    ux.Method == uy.Method &&
                    Equals(ux.Operand, uy.Operand),

                MemberExpression mx when y is MemberExpression my =>
                    mx.Member == my.Member &&
                    Equals(mx.Expression, my.Expression),

                MethodCallExpression mcx when y is MethodCallExpression mcy =>
                    mcx.Method == mcy.Method &&
                    Equals(mcx.Object, mcy.Object) &&
                    mcx.Arguments.Count == mcy.Arguments.Count &&
                    mcx.Arguments.Zip(mcy.Arguments, Equals).All(equal => equal),

                ParameterExpression px when y is ParameterExpression py =>
                    px.Name == py.Name,

                ConstantExpression cx when y is ConstantExpression cy =>
                    Equals(cx.Value, cy.Value),

                LambdaExpression lx when y is LambdaExpression ly =>
                    lx.Parameters.Count == ly.Parameters.Count &&
                    lx.Parameters.Zip(ly.Parameters, Equals).All(equal => equal) &&
                    Equals(lx.Body, ly.Body),

                NewExpression nx when y is NewExpression ny =>
                    nx.Constructor == ny.Constructor &&
                    nx.Arguments.Count == ny.Arguments.Count &&
                    nx.Arguments.Zip(ny.Arguments, Equals).All(equal => equal),

                _ => x.ToString() == y.ToString()
            };
        }

        public int GetHashCode(Expression obj)
        {
            if (obj == null) return 0;
            
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + obj.NodeType.GetHashCode();
                hash = hash * 31 + (obj.Type?.GetHashCode() ?? 0);

                switch (obj)
                {
                    case BinaryExpression b:
                        hash = hash * 31 + (b.Method?.GetHashCode() ?? 0);
                        hash = hash * 31 + GetHashCode(b.Left);
                        hash = hash * 31 + GetHashCode(b.Right);
                        break;

                    case UnaryExpression u:
                        hash = hash * 31 + (u.Method?.GetHashCode() ?? 0);
                        hash = hash * 31 + GetHashCode(u.Operand);
                        break;

                    case MemberExpression m:
                        hash = hash * 31 + (m.Member?.GetHashCode() ?? 0);
                        if (m.Expression != null)
                            hash = hash * 31 + GetHashCode(m.Expression);
                        break;

                    case MethodCallExpression mc:
                        hash = hash * 31 + (mc.Method?.GetHashCode() ?? 0);
                        if (mc.Object != null)
                            hash = hash * 31 + GetHashCode(mc.Object);
                        hash = mc.Arguments.Aggregate(hash, (current, arg) => current * 31 + GetHashCode(arg));
                        break;

                    case ParameterExpression p:
                        hash = hash * 31 + (p.Name?.GetHashCode() ?? 0);
                        break;

                    case ConstantExpression c:
                        hash = hash * 31 + (c.Value?.GetHashCode() ?? 0);
                        break;

                    case LambdaExpression l:
                        hash = hash * 31 + GetHashCode(l.Body);
                        hash = l.Parameters.Aggregate(hash, (current, param) => current * 31 + GetHashCode(param));
                        break;

                    case NewExpression n:
                        hash = hash * 31 + (n.Constructor?.GetHashCode() ?? 0);
                        hash = n.Arguments.Aggregate(hash, (current, arg) => current * 31 + GetHashCode(arg));
                        break;

                    default:
                        hash = hash * 31 + (obj.ToString()?.GetHashCode() ?? 0);
                        break;
                }

                return hash;
            }
        }
    }
}