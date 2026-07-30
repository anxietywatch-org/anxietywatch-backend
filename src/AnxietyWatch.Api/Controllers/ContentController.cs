using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/content")]
public sealed class ContentController : ControllerBase
{
    [HttpGet("faq")]
    public IActionResult Faq() => Ok(new[]
    {
        new { question = "What is AnxietyWatch?", answer = "A tool for recording and reviewing anxiety trends." }
    });

    [HttpGet("testimonials")]
    public IActionResult Testimonials() => Ok(Array.Empty<object>());
}
