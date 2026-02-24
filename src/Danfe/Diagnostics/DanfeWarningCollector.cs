using System.Collections.Generic;

namespace Direction.NFSe.Danfe;

internal sealed class DanfeWarningCollector
{
    private readonly List<DanfeWarning> _warnings = new();

    public IReadOnlyList<DanfeWarning> Warnings => _warnings;

    public void FieldMissing(string fieldName, string? path = null, string fallback = "-")
    {
        _warnings.Add(new DanfeWarning(
            DanfeWarningCodes.FieldMissing,
            $"Campo {fieldName} ausente; usando '{fallback}'.",
            path
        ));
    }

    public void MunicipioNotFound(string? path = null)
    {
        _warnings.Add(new DanfeWarning(
            DanfeWarningCodes.MunicipioNotFound,
            "Município não encontrado; usando valores padrão.",
            path
        ));
    }

    public void TemplatePlaceholderEmpty(string placeholder)
    {
        _warnings.Add(new DanfeWarning(
            DanfeWarningCodes.TemplatePlaceholderEmpty,
            $"Placeholder {placeholder} resultou vazio após renderização.",
            placeholder
        ));
    }

    public void GenericWarning(string message)
    {
        _warnings.Add(new DanfeWarning(
            DanfeWarningCodes.GenericWarning,
            message,
            string.Empty
        ));
    }
}
