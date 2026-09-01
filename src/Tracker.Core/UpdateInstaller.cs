namespace Tracker.Core;

/// <summary>
/// Nasazení stažené verze. Windows umí přejmenovat i běžící <c>.exe</c>, takže se stará verze
/// odsune stranou a nová se přesune na její místo. Žádný pomocný instalátor není potřeba.
/// </summary>
public static class UpdateInstaller
{
    /// <summary>Cesta, kam se ukládá stažené vydání, dokud se nenainstaluje.</summary>
    public static string StagedPath(string executablePath) => executablePath + ".new";

    private static string RetiredPath(string executablePath) => executablePath + ".old";

    public static bool HasStagedUpdate(string executablePath) => File.Exists(StagedPath(executablePath));

    /// <summary>
    /// Uklidí zbytek po minulé aktualizaci a případnou staženou verzi nasadí. Vrací <c>true</c>,
    /// pokud se verze vyměnila; ta se projeví až při dalším spuštění, protože běžící proces
    /// pořád používá původní obraz.
    /// </summary>
    public static bool Apply(string executablePath)
    {
        var retired = RetiredPath(executablePath);
        TryDelete(retired);

        var staged = StagedPath(executablePath);
        if (!File.Exists(staged))
        {
            return false;
        }

        try
        {
            File.Move(executablePath, retired, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        try
        {
            File.Move(staged, executablePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Výměna se nepovedla, takže se původní program vrátí zpět na své místo.
            TryMoveBack(retired, executablePath);
            return false;
        }
    }

    private static void TryMoveBack(string retired, string executablePath)
    {
        try
        {
            File.Move(retired, executablePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Tady už se nedá nic dělat; uživatel si program stáhne znovu ručně.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Odloženou starou verzi drží ještě běžící proces; smaže se při příštím startu.
        }
    }
}
