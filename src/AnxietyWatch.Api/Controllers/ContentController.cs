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
            answer = "Una herramienta de acompañamiento para registrar episodios de ansiedad, revisar patrones personales y tomar decisiones con más contexto. No sustituye la atención profesional."
        },
        new
        {
            question = "¿Qué muestra el dashboard?",
            answer = "Muestra el nivel del registro más reciente, su tendencia frente al anterior, los registros de la semana, la racha de uso y el historial personal de episodios."
        },
        new
        {
            question = "¿Qué puedo registrar?",
            answer = "Puedes registrar una intensidad de 0 a 100, síntomas o sensaciones y una nota de contexto de hasta 500 caracteres para consultar tu evolución."
        },
        new
        {
            question = "¿Qué incluye el plan Gratuito?",
            answer = "Incluye dashboard, registro de ansiedad, un token de vinculación y hasta cinco registros semanales. Los demás planes amplían funciones y capacidad de vinculación."
        },
        new
        {
            question = "¿Para qué sirven los tokens de vinculación?",
            answer = "Permiten vincular la aplicación móvil de forma controlada. Los límites actuales son 1 token en Gratuito e Individual, 5 en Familiar y 20 en Profesional."
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
            name = "Registro personal",
            role = "Escenario de uso del producto",
            quote = "Registra la intensidad, síntomas y contexto de un episodio para consultar su evolución con el tiempo."
        },
        new
        {
            name = "Seguimiento de tendencias",
            role = "Escenario de uso del producto",
            quote = "El dashboard reúne los registros recientes para mostrar una tendencia, la actividad semanal y una racha de uso."
        },
        new
        {
            name = "Vinculación por plan",
            role = "Escenario de uso del producto",
            quote = "Los tokens de vinculación permiten conectar la app móvil respetando la capacidad de cada plan."
        }
    });
}
