using SearchService.Models;

namespace SearchService.Services;

// TooVague / RedirectToFaultCode / Valid. See docs/SemaRepair_Architecture.md section 5.10 Rule 12 / section 9.
public class ValidationService
{
    public ValidationResult ValidateSymptom(string symptom) =>
        throw new NotImplementedException();
}
