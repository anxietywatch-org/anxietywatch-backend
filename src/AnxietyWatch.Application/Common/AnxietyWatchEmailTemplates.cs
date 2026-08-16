using System.Net;

namespace AnxietyWatch.Application.Common;

public static class AnxietyWatchEmailTemplates
{
    public static string Verification(string verificationLink, string fullName) => Layout(
        "Verifica tu correo",
        "Confirma tu cuenta de AnxietyWatch",
        $"Hola, {NormalizeName(fullName)}. Confirma tu correo para proteger tu cuenta y completar tu registro.",
        "Verificar mi correo",
        verificationLink,
        "Este enlace es de un solo uso y vence en 24 horas.");

    public static string PasswordRecovery(string resetLink, string fullName) => Layout(
        "Recupera tu contraseña",
        "Solicitaste restablecer tu acceso",
        $"Hola, {NormalizeName(fullName)}. Usa el botón para crear una nueva contraseña de forma segura.",
        "Crear nueva contraseña",
        resetLink,
        "Este enlace vence en 30 minutos. Si no hiciste la solicitud, ignora este correo.");

    public static string PasswordChanged(string fullName) => Layout(
        "Contraseña actualizada",
        "Tu cuenta está protegida",
        $"Hola, {NormalizeName(fullName)}. La contraseña de tu cuenta se cambió correctamente.",
        null,
        null,
        "Si no reconoces este cambio, recupera tu contraseña inmediatamente desde AnxietyWatch.");

    public static string TokenInvitation(string code) => Layout(
        "Invitación de vinculación",
        "Conecta un dispositivo con AnxietyWatch",
        "Recibiste un token para vincular un dispositivo o wearable a una cuenta de AnxietyWatch.",
        null,
        null,
        $"Código de vinculación: {code}");

    private static string Layout(
        string title,
        string eyebrow,
        string message,
        string? actionText,
        string? actionUrl,
        string footerNote)
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedEyebrow = WebUtility.HtmlEncode(eyebrow);
        var encodedMessage = WebUtility.HtmlEncode(message);
        var encodedFooter = WebUtility.HtmlEncode(footerNote);
        var action = string.IsNullOrWhiteSpace(actionText) || string.IsNullOrWhiteSpace(actionUrl)
            ? string.Empty
            : $"""
               <tr><td style="padding:0 40px 28px">
                 <a href="{WebUtility.HtmlEncode(actionUrl)}" style="display:inline-block;padding:14px 24px;border-radius:10px;background:#315f50;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700">{WebUtility.HtmlEncode(actionText)}</a>
               </td></tr>
               <tr><td style="padding:0 40px 26px;color:#73827c;font-size:12px;line-height:1.55">
                 Si el botón no funciona, copia este enlace:<br>
                 <a href="{WebUtility.HtmlEncode(actionUrl)}" style="color:#315f50;word-break:break-all">{WebUtility.HtmlEncode(actionUrl)}</a>
               </td></tr>
               """;

        return $$"""
            <!doctype html>
            <html lang="es">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;background:#f3f0e9;font-family:Arial,Helvetica,sans-serif;color:#243c34">
              <div style="display:none;max-height:0;overflow:hidden">{{encodedEyebrow}}</div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f3f0e9;padding:34px 14px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;overflow:hidden;border:1px solid #dce4df;border-radius:18px;background:#ffffff;box-shadow:0 14px 40px rgba(37,66,56,.09)">
                    <tr><td style="padding:26px 40px;background:#183d32;color:#ffffff">
                      <div style="font-family:Georgia,'Times New Roman',serif;font-size:22px;letter-spacing:.02em">AnxietyWatch</div>
                      <div style="margin-top:5px;color:#bfd4cb;font-size:11px;letter-spacing:.14em;text-transform:uppercase">Tu bienestar, más claro cada día</div>
                    </td></tr>
                    <tr><td style="padding:36px 40px 14px">
                      <div style="margin-bottom:10px;color:#6f8c80;font-size:11px;font-weight:700;letter-spacing:.13em;text-transform:uppercase">{{encodedEyebrow}}</div>
                      <h1 style="margin:0;font-family:Georgia,'Times New Roman',serif;color:#243c34;font-size:30px;font-weight:500;line-height:1.2">{{encodedTitle}}</h1>
                    </td></tr>
                    <tr><td style="padding:0 40px 26px;color:#596b64;font-size:15px;line-height:1.7">{{encodedMessage}}</td></tr>
                    {{action}}
                    <tr><td style="padding:20px 40px;border-top:1px solid #e6ece8;background:#f8faf8;color:#74827c;font-size:12px;line-height:1.55">{{encodedFooter}}</td></tr>
                  </table>
                  <div style="padding:18px;color:#87938e;font-size:11px">© {{DateTime.UtcNow.Year}} AnxietyWatch · Mensaje automático</div>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string NormalizeName(string fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? "usuario" : fullName.Trim();
}
