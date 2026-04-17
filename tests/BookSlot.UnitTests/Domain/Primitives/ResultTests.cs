using BookSlot.Domain.Primitives;

namespace BookSlot.UnitTests.Domain.Primitives;

public class ResultTests
{
    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var error = Error.Validation("Test.Bad", "bad");

        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Typed_success_exposes_value()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Typed_failure_access_throws()
    {
        var result = Result.Failure<int>(Error.Validation("X", "x"));

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Success_with_error_throws()
    {
        var act = () => Result.Failure(Error.None);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Implicit_conversion_from_value_produces_success()
    {
        Result<string> result = "hello";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void Implicit_conversion_from_null_produces_failure()
    {
        Result<string> result = (string?)null!;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.NullValue);
    }
}
