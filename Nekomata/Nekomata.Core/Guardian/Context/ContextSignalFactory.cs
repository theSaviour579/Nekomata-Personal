using System.Security.Cryptography;
using System.Text;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Context;

public static class ContextSignalFactory
{
    public static ContextSignal FromCalendar(CalendarEvent value)=>new()
    {
        Source="Microsoft Calendar",Kind=ContextSignalKind.Meeting,ExternalId=value.Id,Title=value.Subject,
        Body=$"{value.Location} {value.BodyPreview} {string.Join(" ",value.Attendees)}",Person=value.Organiser,Owner=value.Organiser,
        Status=value.End<=DateTimeOffset.Now?"Observed":"Upcoming",DueAt=value.Start,ObservedAt=value.Start,
        SourceEventId=StableId("Microsoft Calendar",value.Id)
    };
    public static Guid StableId(string source,string externalId)=>new(SHA256.HashData(Encoding.UTF8.GetBytes($"context-signal:{source}:{externalId}"))[..16]);
}
