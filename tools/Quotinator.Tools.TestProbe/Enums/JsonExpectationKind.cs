namespace Quotinator.Tools.TestProbe.Enums;

/// <summary>What a <c>json</c> expectation asserts about a field.</summary>
internal enum JsonExpectationKind
{
    /// <summary>The field's value is exactly the expected text (<c>--equals</c>).</summary>
    EqualTo,

    /// <summary>The field is a string containing the text, or an array with an element containing it (<c>--contains</c>).</summary>
    Contains,

    /// <summary>The field exists (<c>--present</c>).</summary>
    Present,

    /// <summary>The field exists and is not null, an empty string or an empty array (<c>--not-empty</c>).</summary>
    NotEmpty,

    /// <summary>The field is a number greater than the expected number (<c>--greater-than</c>).</summary>
    GreaterThan,

    /// <summary>The field's value is exactly another field's value (<c>--equals-field</c>).</summary>
    EqualToField,

    /// <summary>The field is an array with as many elements as another field's number (<c>--length-equals-field</c>).</summary>
    LengthEqualToField,

    /// <summary>The field is an array with exactly the expected number of elements (<c>--length-equals</c>).</summary>
    LengthEquals,
}
