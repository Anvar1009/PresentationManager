namespace PresentationManager.UI.Theme;

/// <summary>Builds a themed, read-only <see cref="DataGridView"/> — used by the Admin/SuperAdmin panels, the
/// first place in this app tabular DB data is shown directly rather than through a custom-drawn list.</summary>
public static class DataGridViewTheme
{
    /// <summary>The app's usual dark palette.</summary>
    public static DataGridView CreateReadOnlyGrid() => CreateReadOnlyGrid(
        background: AppColors.PanelAlt,
        alternatingBackground: AppColors.Panel,
        headerBackground: AppColors.Panel,
        headerForeground: AppColors.TextSecondary,
        textColor: AppColors.TextPrimary,
        accent: AppColors.Accent,
        selectionForeground: AppColors.Background,
        gridLines: AppColors.Border);

    /// <summary>Same grid, themed for a light-background screen (SuperAdmin panel) instead of the app's
    /// usual dark one.</summary>
    public static DataGridView CreateReadOnlyGrid(
        Color background, Color alternatingBackground, Color headerBackground, Color headerForeground,
        Color textColor, Color accent, Color selectionForeground, Color gridLines)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = background,
            GridColor = gridLines,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            Font = new Font("Segoe UI", 9.5f)
        };

        grid.DefaultCellStyle.BackColor = background;
        grid.DefaultCellStyle.ForeColor = textColor;
        grid.DefaultCellStyle.SelectionBackColor = accent;
        grid.DefaultCellStyle.SelectionForeColor = selectionForeground;
        grid.DefaultCellStyle.Padding = new Padding(10, 0, 10, 0);

        grid.ColumnHeadersDefaultCellStyle.BackColor = headerBackground;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = headerForeground;
        // Clicking any cell in a column marks that column's header as the "current" one, which WinForms then
        // paints using these Selection* colors - left at their defaults that meant a blue header background
        // with whatever near-invisible foreground it inherited. Explicit white keeps the label readable
        // instead of vanishing into the highlight.
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = accent;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 8, 10, 8);
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.EnableHeadersVisualStyles = false;

        grid.AlternatingRowsDefaultCellStyle.BackColor = alternatingBackground;
        grid.RowTemplate.Height = 34;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;

        // WinForms auto-selects row 0 the moment data is bound, which - combined with FullRowSelect - paints
        // that entire first row (most noticeably its first cell) with SelectionBackColor even though nothing
        // was actually clicked. These are read-only reference grids, not something a "current row" concept
        // is useful for, so every rebind clears it back to plain, uncolored cells like all the others.
        grid.DataBindingComplete += (_, _) =>
        {
            grid.ClearSelection();
            grid.CurrentCell = null;
        };

        return grid;
    }
}
