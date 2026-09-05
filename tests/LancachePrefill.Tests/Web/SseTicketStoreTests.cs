using LancachePrefill;
using Xunit;

namespace LancachePrefill.Tests;

public class SseTicketStoreTests
{
    [Fact]
    public void Ticket_RedeemsExactlyOnce()
    {
        var store = new SseTicketStore();
        var ticket = store.Issue();
        Assert.True(store.Redeem(ticket));
        Assert.False(store.Redeem(ticket)); // single-use
    }

    [Fact]
    public void BogusOrEmptyTicket_DoesNotRedeem()
    {
        var store = new SseTicketStore();
        Assert.False(store.Redeem("not-a-ticket"));
        Assert.False(store.Redeem(""));
        Assert.False(store.Redeem(null));
    }

    [Fact]
    public void Tickets_AreUnique()
    {
        var store = new SseTicketStore();
        Assert.NotEqual(store.Issue(), store.Issue());
    }
}
