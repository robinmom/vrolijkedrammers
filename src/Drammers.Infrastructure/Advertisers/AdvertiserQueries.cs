using Drammers.Modules.Membership.Advertisers;

namespace Drammers.Infrastructure.Advertisers;

/// <summary>
/// Eén plek voor de vraag "doet deze adverteerder mee?" (fase 27s). Verwijderde adverteerders en aanmeldingen uit de
/// app die nog op het bestuur wachten, horen nergens in de campagne thuis: niet in het overzicht, de export, de
/// facturen, de incasso of een mailing. Omdat dat op zes plekken wordt opgevraagd staat het filter hier, zodat een
/// nieuwe plek het niet kan vergeten.
/// </summary>
public static class AdvertiserQueries
{
    /// <summary>Niet verwijderd; een beoordeling mag open staan (voor de wachtrij in het portal en de app).</summary>
    public static IQueryable<Advertiser> NotDeleted(this IQueryable<Advertiser> advertisers) =>
        advertisers.Where(a => a.DeletedAt == null);

    /// <summary>Doet mee: niet verwijderd en niet in afwachting van goedkeuring.</summary>
    public static IQueryable<Advertiser> InCampaign(this IQueryable<Advertiser> advertisers) =>
        advertisers.Where(a => a.DeletedAt == null && (a.Review == null || a.Review == AdvertiserReview.Approved));
}
