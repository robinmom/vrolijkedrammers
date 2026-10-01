using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages;

/// <summary>
/// 404 in de huisstijl voor websiteadressen die niet bestaan (via re-execute, zie <see cref="WebsiteSetup"/>). Andere
/// statuscodes (bijvoorbeeld 401) houden ProblemDetails, net als in de API.
/// </summary>
public sealed class NietGevondenModel(IProblemDetailsService problems) : SitePage
{
    public async Task<IActionResult> OnGetAsync()
    {
        var status = Response.StatusCode;
        if (status is not (StatusCodes.Status404NotFound or StatusCodes.Status200OK))
        {
            await problems.WriteAsync(new ProblemDetailsContext { HttpContext = HttpContext, ProblemDetails = { Status = status } });
            return new EmptyResult();
        }

        return NotFoundPage();
    }
}
