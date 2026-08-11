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
        new
        {
            question = "¿Qué es AnxietyWatch?",
            answer = "Una herramienta de acompañamiento para registrar cómo te sientes, revisar tus tendencias y decidir tus próximos pasos con más contexto."
        },
        new
        {
            question = "¿Cómo se calcula el nivel mostrado en mi resumen?",
            answer = "El resumen usa la intensidad de tus registros recientes. Es una guía personal, no un diagnóstico ni sustituye la atención profesional."
        },
        new
        {
            question = "¿Qué puedo registrar?",
            answer = "Puedes anotar la intensidad que percibes, síntomas o sensaciones y una nota breve. Así podrás observar patrones con el tiempo."
        },
        new
        {
            question = "¿Qué ocurre si necesito ayuda inmediata?",
            answer = "AnxietyWatch no es un servicio de emergencias. Si crees que estás en riesgo o necesitas ayuda urgente, contacta a los servicios de emergencia locales o a una persona de confianza."
        }
    });

    [HttpGet("testimonials")]
    public IActionResult Testimonials() => Ok(new[]
    {
        new
        {
            name = "Ejemplo de uso · 7 días",
            role = "Registro personal de prueba",
            quote = "Al registrar cómo me sentía al final del día pude identificar qué momentos necesitaba pausar y respirar."
        },
        new
        {
            name = "Ejemplo de uso · Tendencia",
            role = "Seguimiento de prueba",
            quote = "Ver mis registros juntos me ayudó a notar una semana más estable y a mantener hábitos que me hacen bien."
        },
        new
        {
            name = "Ejemplo de uso · Privacidad",
            role = "Cuenta de demostración",
            quote = "Me gusta que el registro sea breve y que yo decida cuándo compartir información con alguien de confianza."
        }
    });
}
