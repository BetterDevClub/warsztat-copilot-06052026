using BookSlot.Domain.Primitives;

namespace BookSlot.UnitTests.Domain.Primitives;

public class AggregateRootTests
{
    private sealed record TestEvent : DomainEvent;

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate() : base(Guid.NewGuid()) { }

        public void DoSomething() => RaiseDomainEvent(new TestEvent());
    }

    [Fact]
    public void RaiseDomainEvent_adds_to_domain_events()
    {
        var aggregate = new TestAggregate();

        aggregate.DoSomething();
        aggregate.DoSomething();

        aggregate.DomainEvents.Should().HaveCount(2);
    }

    [Fact]
    public void ClearDomainEvents_removes_all_events()
    {
        var aggregate = new TestAggregate();
        aggregate.DoSomething();

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void New_aggregate_has_no_events()
    {
        var aggregate = new TestAggregate();
        aggregate.DomainEvents.Should().BeEmpty();
    }
}
