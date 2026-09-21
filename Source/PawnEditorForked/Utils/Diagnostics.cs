using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Fronteras de error con contexto: ejecuta un trozo de trabajo con nombre y, si revienta, registra
/// UNA vez quién falló y sobre qué, en lugar de dejar caer la ventana entera.
///
/// POR QUÉ EXISTE
/// Un reporte de usuario útil dice dónde mirar. Uno inútil dice "se me rompió". La diferencia no la
/// pone el usuario, la ponemos nosotros: si cada sección se identifica al fallar, el log llega con el
/// culpable ya señalado.
///
/// Evidencia: cuando el editor de caras falló, la línea
///     at FacialAnimation.NL_SelectPartWindow.GetSelectedPawn
/// fue el diagnóstico completo. No hicieron falta trazas, solo que el método tuviera nombre honesto.
/// Esto extiende esa idea a nuestras costuras, y le agrega el dato que una traza no lleva: para qué
/// pawn, de qué mod, en qué sección.
///
/// DOS DECISIONES DELIBERADAS
///
/// No relanza. El objetivo es que el resto de la interfaz siga funcionando: el usuario ve una
/// sección vacía en vez de una ventana muerta, y nosotros recibimos el log igual.
///
/// Registra una sola vez por combinación de sección, sujeto y tipo de excepción. Esto vive en código
/// de modo inmediato: sin esa condición, un fallo escribiría sesenta líneas por segundo y enterraría
/// todo lo demás, que es exactamente el problema que hemos visto en logs ajenos.
///
/// COSTE
/// En el camino feliz solo cuesta la asignación del delegado. Por eso las fronteras van en las
/// costuras gruesas (una sección, una pestaña, un compat) y NUNCA por fila de una rejilla.
/// </summary>
public static class Diagnostics
{
    private static readonly HashSet<string> Reported = new();

    /// <summary>Ejecuta el trabajo; si falla, lo registra con contexto y continúa.</summary>
    /// <param name="section">Qué se estaba haciendo, en palabras. Aparece tal cual en el log.</param>
    /// <param name="subject">Pawn, Def o lo que dé contexto. Puede ser null.</param>
    public static void Run(string section, object subject, Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            Report(section, subject, ex);
        }
    }

    /// <summary>Igual que <see cref="Run(string, object, Action)"/>, sin sujeto que reportar.</summary>
    public static void Run(string section, Action work) => Run(section, null, work);

    /// <summary>
    /// Variante para trabajo que devuelve un valor. Ante un fallo entrega <paramref name="fallback"/>,
    /// de modo que el llamador siga teniendo algo con lo que dibujar.
    /// </summary>
    public static T Run<T>(string section, object subject, Func<T> work, T fallback = default)
    {
        try
        {
            return work();
        }
        catch (Exception ex)
        {
            Report(section, subject, ex);
            return fallback;
        }
    }

    private static void Report(string section, object subject, Exception ex)
    {
        var description = Describe(subject);
        var key = $"{section}|{description}|{ex.GetType().FullName}";
        if (!Reported.Add(key)) return;

        var target = description.NullOrEmpty() ? "" : $" for {description}";
        Log.Error($"[Pawn Editor] {section} failed{target}. The rest of the window keeps working; "
                  + $"this section is skipped.\n{ex}");
    }

    /// <summary>
    /// Describe el sujeto de la forma más útil para quien lee el log: un pawn por su nombre, un def
    /// por su defName y el mod que lo trajo, porque esa combinación es la que permite reproducirlo.
    /// </summary>
    private static string Describe(object subject) => subject switch
    {
        null => "",
        Pawn pawn => pawn.Name?.ToStringShort ?? pawn.LabelCap,
        Def def => def.modContentPack?.Name is { } mod ? $"'{def.defName}' (from {mod})" : $"'{def.defName}'",
        string text => text,
        _ => subject.ToString()
    };
}
