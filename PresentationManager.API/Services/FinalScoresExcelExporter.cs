using ClosedXML.Excel;
using PresentationManager.Application.Services;
using PresentationManager.Domain.Entities;

namespace PresentationManager.API.Services;

/// <summary>Server-side equivalent of PresentationManager.UI's <c>ExcelExportHelper</c> - that one reads
/// straight off a WinForms <c>DataGridView</c>, which doesn't exist here, so this builds the same kind of
/// workbook directly from the same <see cref="PresentationScoreSummary"/>/<see cref="EvaluationCriterion"/>
/// data <see cref="ScoreService.GetFinalScoresAsync"/> already returns for the desktop grid. Used by both the
/// web Admin and SuperAdmin panels' "Excel'ga eksport" action.</summary>
public static class FinalScoresExcelExporter
{
    public static byte[] Export(string projectName, IReadOnlyList<EvaluationCriterion> criteria, IReadOnlyList<PresentationScoreSummary> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SanitizeSheetName(projectName));

        var col = 1;
        sheet.Cell(1, col++).Value = "Taqdimotchi";
        sheet.Cell(1, col++).Value = "Sarlavha";
        foreach (var criterion in criteria)
        {
            sheet.Cell(1, col++).Value = criterion.Name;
        }
        sheet.Cell(1, col).Value = "Jami";
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromArgb(244, 246, 249);

        var row = 2;
        foreach (var summary in rows)
        {
            col = 1;
            sheet.Cell(row, col++).Value = summary.PresenterFullName;
            sheet.Cell(row, col++).Value = summary.Title;
            foreach (var criterion in criteria)
            {
                sheet.Cell(row, col++).Value = summary.AverageByCriterionId.TryGetValue(criterion.Id, out var avg) ? avg : 0;
            }
            sheet.Cell(row, col).Value = summary.Total;
            row++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Excel worksheet names can't contain <c>: \ / ? * [ ]</c> and are capped at 31 characters -
    /// a project name is free-text and could contain any of these.</summary>
    private static string SanitizeSheetName(string name)
    {
        var cleaned = new string(name.Where(c => !"\\/?*[]:".Contains(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "Yakuniy baholar";
        }

        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
