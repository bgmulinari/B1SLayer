using System.Linq.Expressions;

using B1SLayer.Test.Models;

namespace B1SLayer.Test.Tests;

public class SLExpressionParserTests
{
    private readonly SLExpressionParser _parser = new();

    #region Basic Operators

    [Fact]
    public void Parse_EqualityOperator_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.CardCode == "C001";

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("CardCode eq 'C001'", result);
    }

    [Fact]
    public void Parse_NotEqualOperator_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.CardCode != "C001";

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("CardCode ne 'C001'", result);
    }

    [Fact]
    public void Parse_GreaterThanOperator_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocTotal > 1000;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocTotal gt 1000", result);
    }

    [Fact]
    public void Parse_GreaterThanOrEqualOperator_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocTotal >= 1000;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocTotal ge 1000", result);
    }

    [Fact]
    public void Parse_LessThanOperator_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocTotal < 5000;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocTotal lt 5000", result);
    }

    [Fact]
    public void Parse_LessThanOrEqualOperator_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocTotal <= 5000;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocTotal le 5000", result);
    }

    #endregion

    #region String Operations

    [Fact]
    public void Parse_StringContains_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.CardName.Contains("Corp");

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("contains(CardName, 'Corp')", result);
    }

    [Fact]
    public void Parse_StringStartsWith_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.CardCode.StartsWith("C");

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("startswith(CardCode, 'C')", result);
    }

    [Fact]
    public void Parse_StringEndsWith_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocCurrency.EndsWith("USD");

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("endswith(DocCurrency, 'USD')", result);
    }

    [Fact]
    public void Parse_StringEquals_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocStatus.Equals("O");

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocStatus eq 'O'", result);
    }

    #endregion

    #region Complex Expressions

    [Fact]
    public void Parse_AndExpression_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.CardCode == "C001" && x.DocTotal > 1000;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("CardCode eq 'C001' and DocTotal gt 1000", result);
    }

    [Fact]
    public void Parse_OrExpression_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocStatus == "O" || x.DocStatus == "P";

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("(DocStatus eq 'O' or DocStatus eq 'P')", result);
    }

    [Fact]
    public void Parse_NotExpression_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => !(x.Cancelled == true);

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("not (Cancelled eq true)", result);
    }

    [Fact]
    public void Parse_ComplexNestedExpression_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => (x.DocStatus == "O" || x.DocStatus == "P") && x.DocTotal > 1000;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("(DocStatus eq 'O' or DocStatus eq 'P') and DocTotal gt 1000", result);
    }

    #endregion

    #region Collection Contains

    [Fact]
    public void Parse_CollectionContains_ReturnsCorrectODataFilter()
    {
        // Arrange
        var cardCodes = new List<string> { "C001", "C002", "C003" };
        Expression<Func<MarketingDocument, bool>> expr = x => cardCodes.Contains(x.CardCode);

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("(CardCode eq 'C001' or CardCode eq 'C002' or CardCode eq 'C003')", result);
    }

    [Fact]
    public void Parse_ArrayContains_ReturnsCorrectODataFilter()
    {
        // Arrange
        var docEntries = new[] { 100, 101, 102 };
        Expression<Func<MarketingDocument, bool>> expr = x => docEntries.Contains(x.DocEntry);

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("(DocEntry eq 100 or DocEntry eq 101 or DocEntry eq 102)", result);
    }

    [Fact]
    public void Parse_SingleItemCollectionContains_ReturnsCorrectODataFilter()
    {
        // Arrange
        var cardCodes = new List<string> { "C001" };
        Expression<Func<MarketingDocument, bool>> expr = x => cardCodes.Contains(x.CardCode);

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("CardCode eq 'C001'", result);
    }

    [Fact]
    public void Parse_EmptyCollectionContains_ThrowsArgumentException()
    {
        // Arrange
        var cardCodes = new List<string>();
        Expression<Func<MarketingDocument, bool>> expr = x => cardCodes.Contains(x.CardCode);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _parser.Parse(expr));
        Assert.Contains("empty collection", exception.Message);
    }

    #endregion

    #region Data Types

    [Fact]
    public void Parse_BooleanValue_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.Cancelled == true;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("Cancelled eq true", result);
    }

    [Fact]
    public void Parse_DateTimeValue_ReturnsCorrectODataFilter()
    {
        // Arrange
        var date = new DateTime(2024, 1, 15, 10, 30, 45);
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocDate == date;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocDate eq '2024-01-15T10:30:45'", result);
    }

    [Fact]
    public void Parse_DateTimeOffsetValue_ReturnsCorrectODataFilter()
    {
        // Arrange
        var dateOffset = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.FromHours(-5));
        Expression<Func<MarketingDocument, bool>> expr = x => x.UpdateDateTime == dateOffset;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("UpdateDateTime eq '2024-01-15T10:30:45-05:00'", result);
    }

    [Fact]
    public void Parse_NullValue_ReturnsCorrectODataFilter()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.Comments == null;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("Comments eq null", result);
    }

    [Fact]
    public void Parse_CharValue_ReturnsCorrectODataFilter()
    {
        // Arrange
        // For SAP B1, document status is typically a string, not char
        // Let's test with a string comparison instead
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocStatus == "O";

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocStatus eq 'O'", result);
    }

    [Fact]
    public void Parse_CharPropertyComparison_ReturnsCorrectODataFilter()
    {
        // Arrange
        // Test actual char property comparison
        var statusChar = 'O';
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocumentStatus == statusChar;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocumentStatus eq 'O'", result);
    }

    #endregion

    #region JsonPropertyName Support

    [Fact]
    public void Parse_PropertyWithJsonPropertyName_UsesJsonName()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.BranchId == 123;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("BPL_IDAssignedToInvoice eq 123", result);
    }

    [Fact]
    public void Parse_PropertyWithJsonPropertyNameInComplexExpression_UsesJsonName()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.BranchId > 100 && x.UserDefinedField.Contains("Important");

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("BPL_IDAssignedToInvoice gt 100 and contains(U_CustomField, 'Important')", result);
    }

    #endregion

    #region Expression Caching

    [Fact]
    public void Parse_SameExpressionMultipleTimes_UsesCachedResult()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr1 = x => x.CardCode == "C001";
        Expression<Func<MarketingDocument, bool>> expr2 = x => x.CardCode == "C001";

        // Act
        var result1 = _parser.Parse(expr1);
        var result2 = _parser.Parse(expr2);

        // Assert
        Assert.Equal(result1, result2);
        // Note: Can't directly test cache hit without exposing internals,
        // but performance should be better on second call
    }

    [Fact]
    public void Parse_DifferentExpressionsWithSameStructure_ReturnsSameResult()
    {
        // Arrange
        var cardCode = "C001";
        Expression<Func<MarketingDocument, bool>> expr1 = x => x.CardCode == cardCode;
        Expression<Func<MarketingDocument, bool>> expr2 = x => x.CardCode == "C001";

        // Act
        var result1 = _parser.Parse(expr1);
        var result2 = _parser.Parse(expr2);

        // Assert
        Assert.Equal(result1, result2);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void Parse_NullExpression_ThrowsArgumentNullException()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _parser.Parse(expr));
    }

    [Fact]
    public void Parse_UnsupportedMethodCall_ThrowsNotSupportedException()
    {
        // Arrange
        Expression<Func<MarketingDocument, bool>> expr = x => x.CardCode.ToLower() == "c001";

        // Act & Assert
        // The parser throws a custom exception that inherits from NotSupportedException
        var exception = Assert.ThrowsAny<NotSupportedException>(() => _parser.Parse(expr));
        Assert.Contains("Unsupported method call", exception.Message);
    }

    [Fact]
    public void Parse_NullCollectionContains_ThrowsArgumentException()
    {
        // Arrange
        List<string> cardCodes = null;
        Expression<Func<MarketingDocument, bool>> expr = x => cardCodes.Contains(x.CardCode);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _parser.Parse(expr));
        Assert.Contains("null collection", exception.Message);
    }

    #endregion

    #region Select Expressions

    [Fact]
    public void Parse_SelectSingleProperty_ReturnsPropertyName()
    {
        // Arrange
        Expression<Func<MarketingDocument, object>> expr = x => x.CardCode;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("CardCode", result);
    }

    [Fact]
    public void Parse_SelectMultipleProperties_ReturnsCommaSeparatedNames()
    {
        // Arrange
        Expression<Func<MarketingDocument, object>> expr = x => new { x.CardCode, x.DocTotal };

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("CardCode,DocTotal", result);
    }

    [Fact]
    public void Parse_SelectWithJsonPropertyName_UsesJsonName()
    {
        // Arrange
        Expression<Func<MarketingDocument, object>> expr = x => x.BranchId;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("BPL_IDAssignedToInvoice", result);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void Parse_VariableInExpression_EvaluatesCorrectly()
    {
        // Arrange
        var minTotal = 1000m;
        var maxTotal = 5000m;
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocTotal >= minTotal && x.DocTotal <= maxTotal;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocTotal ge 1000 and DocTotal le 5000", result);
    }

    [Fact]
    public void Parse_MethodCallOnVariable_EvaluatesCorrectly()
    {
        // Arrange
        var searchCode = "C001";
        Expression<Func<MarketingDocument, bool>> expr = x => x.CardCode == searchCode.ToUpper();

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("CardCode eq 'C001'", result);
    }

    [Fact]
    public void Parse_ComplexObjectProperty_EvaluatesCorrectly()
    {
        // Arrange
        var filter = new { MinAmount = 1000m, MaxAmount = 10000m };
        Expression<Func<MarketingDocument, bool>> expr = x => x.DocTotal >= filter.MinAmount && x.DocTotal <= filter.MaxAmount;

        // Act
        var result = _parser.Parse(expr);

        // Assert
        Assert.Equal("DocTotal ge 1000 and DocTotal le 10000", result);
    }

    #endregion
}