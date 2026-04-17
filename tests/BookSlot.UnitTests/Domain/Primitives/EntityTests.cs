using BookSlot.Domain.Primitives;

namespace BookSlot.UnitTests.Domain.Primitives;

public class EntityTests
{
    private sealed class TestEntity : Entity<Guid>
    {
        public TestEntity(Guid id) : base(id) { }
    }

    private sealed class OtherEntity : Entity<Guid>
    {
        public OtherEntity(Guid id) : base(id) { }
    }

    [Fact]
    public void Entities_with_same_id_and_type_are_equal()
    {
        var id = Guid.NewGuid();
        var a = new TestEntity(id);
        var b = new TestEntity(id);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Entities_of_different_types_are_not_equal()
    {
        var id = Guid.NewGuid();
        var a = new TestEntity(id);
        var b = new OtherEntity(id);

        ((object)a).Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Entities_with_different_ids_are_not_equal()
    {
        var a = new TestEntity(Guid.NewGuid());
        var b = new TestEntity(Guid.NewGuid());

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }
}
