using System.Net;
using System.Text;
using System.Text.Json;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Xunit;

namespace Nekomata.Tests;

public sealed class CalendarServiceTransactionTests
{
    [Fact]
    public async Task Split_blocks_get_distinct_transaction_ids_but_exact_retry_is_stable()
    {
        var handler = new RecordingHandler();
        var service = new CalendarService(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
        }, new FakeAuthenticationService());
        var day = new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateFocusEventAsync("Long task", day, day.AddHours(1), "NEKOMATA:TASK:42", cancellationToken);
        await service.CreateFocusEventAsync("Long task", day.AddHours(2), day.AddHours(3), "NEKOMATA:TASK:42", cancellationToken);
        await service.CreateFocusEventAsync("Long task", day, day.AddHours(1), "NEKOMATA:TASK:42", cancellationToken);

        Assert.NotEqual(handler.TransactionIds[0], handler.TransactionIds[1]);
        Assert.Equal(handler.TransactionIds[0], handler.TransactionIds[2]);
    }

    [Fact]
    public async Task Calendar_reads_all_pages_and_preserves_attendee_addresses()
    {
        var handler = new PagedCalendarHandler();
        var service = new CalendarService(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
        }, new FakeAuthenticationService());

        var events = await service.GetEventsAsync(
            new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, events.Count);
        Assert.Equal(2, handler.Requests);
        Assert.Contains("supplier@example.com", events[1].Attendees[0]);
        Assert.Contains("owner@example.com", events[1].Organiser);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> TransactionIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(content);
            TransactionIds.Add(json.RootElement.GetProperty("transactionId").GetString()!);
            var start = json.RootElement.GetProperty("start").GetProperty("dateTime").GetString();
            var end = json.RootElement.GetProperty("end").GetProperty("dateTime").GetString();
            var response = $$"""
                {"id":"event-{{TransactionIds.Count}}","subject":"Focus · Long task","start":{"dateTime":"{{start}}"},"end":{"dateTime":"{{end}}"},"bodyPreview":"NEKOMATA:TASK:42"}
                """;
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class PagedCalendarHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var body = Requests == 1
                ? """{"value":[{"id":"one","subject":"First","start":{"dateTime":"2026-09-11T09:00:00+00:00"},"end":{"dateTime":"2026-09-11T09:30:00+00:00"}}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/calendarView?page=2"}"""
                : """{"value":[{"id":"two","subject":"Second","start":{"dateTime":"2026-09-12T10:00:00+00:00"},"end":{"dateTime":"2026-09-12T10:30:00+00:00"},"organizer":{"emailAddress":{"name":"Owner","address":"owner@example.com"}},"attendees":[{"emailAddress":{"name":"Supplier","address":"supplier@example.com"}}]}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeAuthenticationService : IMicrosoftAuthenticationService
    {
        public Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TokenResult
            {
                AccessToken = "test-token", ExpiresOn = DateTimeOffset.MaxValue, AccountName = "test@example.com"
            });
    }
}
