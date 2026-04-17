using BookSlot.Domain.Primitives;

namespace BookSlot.UnitTests.Domain.Primitives;

public class ValueObjectTests
{
    private sealed class Money : ValueObject
    {
        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public decimal Amount { get; }

        public string Currency { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Same_component_values_produce_equal_value_objects()
    {
        var a = new Money(10, "USD");
        var b = new Money(10, "USD");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Different_component_values_are_not_equal()
    {
        new Money(10, "USD").Should().NotBe(new Money(10, "EUR"));
        new Money(10, "USD").Should().NotBe(new Money(11, "USD"));
    }
}
