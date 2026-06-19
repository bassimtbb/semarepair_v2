using System.Text.RegularExpressions;
using SearchService.Models;

namespace SearchService.Services;

// TooVague / RedirectToFaultCode / Valid. See docs/SemaRepair_Architecture.md section 5.10 Rule 12 / section 9.
public class ValidationService
{
    private static readonly string[] Stopwords =
        ["problema", "errore", "guasto", "non", "funziona", "rotto"];

    public ValidationResult ValidateSymptom(string symptom)
    {
        if (string.IsNullOrWhiteSpace(symptom))
            return ValidationResult.TooVague("Sintomo vuoto");

        var words = symptom.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 2)
            return ValidationResult.TooVague("Sintomo troppo corto");

        // Gemini accidentally passed a fault code as symptom
        if (Regex.IsMatch(symptom.Trim(), @"^[PCBU]\d{4}$"))
            return ValidationResult.RedirectToFaultCode(symptom.Trim());

        // Only generic words, no technical content
        if (words.All(w => Stopwords.Contains(w.ToLower())))
            return ValidationResult.TooVague("Solo parole generiche");

        return ValidationResult.Valid();
    }
}
