using ClosedXML.Excel;

namespace PresentationManager.UI.Forms;

/// <summary>Writes a themed, read-only <see cref="DataGridView"/>'s currently-bound rows/columns out as a
/// real .xlsx file (via ClosedXML - not <c>Microsoft.Office.Interop.Excel</c>, unlike the PowerPoint preview
/// elsewhere in this app, since export shouldn't require Excel itself to be installed on the admin's
/// machine).</summary>
internal static class ExcelExportHelper
{
    public static void ExportGrid(DataGridView grid, string filePath, string sheetName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);

        for (var col = 0; col < grid.Columns.Count; col++)
        {
            var headerCell = sheet.Cell(1, col + 1);
            headerCell.Value = grid.Columns[col].HeaderText;
            headerCell.Style.Font.Bold = true;
        }

        for (var row = 0; row < grid.Rows.Count; row++)
        {
            for (var col = 0; col < grid.Columns.Count; col++)
            {
                var cell = sheet.Cell(row + 2, col + 1);
                var text = grid.Rows[row].Cells[col].Value?.ToString() ?? string.Empty;

                // Score/total columns are formatted numeric strings (e.g. "8.5") in the grid - stored as
                // real numbers here so Excel can sum/sort/chart them, rather than as plain text.
                if (double.TryParse(text, out var number))
                {
                    cell.Value = number;
                }
                else
                {
                    cell.Value = text;
                }
            }
        }

        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromArgb(244, 246, 249);
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }
}
