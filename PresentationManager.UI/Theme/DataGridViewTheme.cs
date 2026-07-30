namespace PresentationManager.UI.Theme;

/// <summary>Applies the app's dark palette to a <see cref="DataGridView"/> and locks it read-only — used by
/// the Admin/SuperAdmin panels, the first place in this app tabular DB data is shown directly rather than
/// through a custom-drawn list.</summary>
public static class DataGridViewTheme
{
    public static DataGridView CreateReadOnlyGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = AppColors.PanelAlt,
            GridColor = AppColors.Border,
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

        grid.DefaultCellStyle.BackColor = AppColors.PanelAlt;
        grid.DefaultCellStyle.ForeColor = AppColors.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = AppColors.Accent;
        grid.DefaultCellStyle.SelectionForeColor = AppColors.Background;

        grid.ColumnHeadersDefaultCellStyle.BackColor = AppColors.Panel;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = AppColors.TextSecondary;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.EnableHeadersVisualStyles = false;

        grid.AlternatingRowsDefaultCellStyle.BackColor = AppColors.Panel;

        return grid;
    }
}
