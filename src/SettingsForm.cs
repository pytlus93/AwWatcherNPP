using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AwWatcherNPP
{
    public sealed class SettingsForm : Form
    {
        internal readonly CheckBox TrackingEnabled = new CheckBox { Text = "Enable tracking", AutoSize = true };
        internal readonly ComboBox PayloadMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
        internal readonly CheckBox IncludeFile = new CheckBox { Text = "File / title", AutoSize = true };
        internal readonly CheckBox IncludeProjectPath = new CheckBox { Text = "Project path", AutoSize = true };
        internal readonly CheckBox IncludeLanguage = new CheckBox { Text = "Language", AutoSize = true };
        internal readonly CheckBox IncludeFilename = new CheckBox { Text = "Filename", AutoSize = true };
        internal readonly CheckBox IncludeProjectName = new CheckBox { Text = "Project name", AutoSize = true };
        private readonly NumericUpDown port = Number(1, 65535);
        private readonly NumericUpDown frequency = Number(1, 60);
        private readonly NumericUpDown pulse = Number(1, 120);
        private readonly NumericUpDown idle = Number(1, 3600);
        private readonly NumericUpDown capacity = Number(1, 10000);
        private readonly CheckBox testing = new CheckBox { Text = "Use testing server (port 5666)", AutoSize = true };
        private readonly Label endpoint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        private readonly Label error = new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick, AutoEllipsis = true };
        private readonly ToolTip hints = new ToolTip();
        private readonly Action<Settings> save;
        internal DataGridView LanguageTable { get; } = new DataGridView();
        internal string ErrorMessage => error.Text;

        public SettingsForm(Settings current, Action<Settings> save, string warning = null)
        {
            this.save = save;
            Text = "ActivityWatch settings";
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(840, 740);
            MinimumSize = new Size(650, 630);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20),
                ColumnCount = 1, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Controls.Add(layout);

            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            heading.Controls.Add(new Label { Text = "ActivityWatch settings", AutoSize = true,
                Font = new Font(Font.FontFamily, 15F, FontStyle.Bold) }, 0, 0);
            heading.Controls.Add(new Label { Text = "Track locally. Changes apply when you save.",
                AutoSize = true, ForeColor = SystemColors.GrayText }, 0, 1);
            layout.Controls.Add(heading, 0, 0);

            var tracking = new GroupBox { Text = "Connection and tracking", Dock = DockStyle.Fill, Padding = new Padding(12) };
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            for (int i = 0; i < 4; i++) fields.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            AddField(fields, "Server port", port, 0, 0, "Local ActivityWatch HTTP port; normally 5600.");
            AddField(fields, "Heartbeat interval (s)", frequency, 2, 0, "Send a heartbeat every 1–60 seconds while focused.");
            AddField(fields, "Merge gap (s)", pulse, 0, 1, "Must be at least the heartbeat interval; maximum 120 seconds.");
            AddField(fields, "Pause after idle (s)", idle, 2, 1, "Pause after this many seconds without Windows input.");
            AddField(fields, "Queue capacity", capacity, 0, 2, "Maximum pending heartbeats. The queue is kept in memory.");
            fields.Controls.Add(testing, 0, 3);
            fields.SetColumnSpan(testing, 2);
            fields.Controls.Add(endpoint, 2, 3);
            fields.SetColumnSpan(endpoint, 2);
            tracking.Controls.Add(fields);
            layout.Controls.Add(tracking, 0, 1);

            TrackingEnabled.Checked = current.Enabled;
            PayloadMode.Items.AddRange(new object[] { "strict", "extended" });
            PayloadMode.SelectedItem = current.PayloadMode;
            IncludeFile.Checked = current.IncludeFile;
            IncludeProjectPath.Checked = current.IncludeProjectPath;
            IncludeLanguage.Checked = current.IncludeLanguage;
            IncludeFilename.Checked = current.IncludeFilename;
            IncludeProjectName.Checked = current.IncludeProjectName;
            var payload = new FlowLayoutPanel { Dock = DockStyle.Fill };
            payload.Controls.Add(new Label { Text = "Payload (strict includes workspace title)", AutoSize = true });
            payload.Controls.Add(PayloadMode);
            payload.Controls.Add(TrackingEnabled);
            payload.Controls.Add(IncludeFile);
            payload.Controls.Add(IncludeProjectPath);
            payload.Controls.Add(IncludeLanguage);
            payload.Controls.Add(IncludeFilename);
            payload.Controls.Add(IncludeProjectName);
            Action updatePayload = () => {
                IncludeFilename.Enabled = PayloadMode.Text == "extended" && IncludeFile.Checked;
                IncludeProjectName.Enabled = PayloadMode.Text == "extended";
            };
            PayloadMode.SelectedIndexChanged += (sender, args) => updatePayload();
            IncludeFile.CheckedChanged += (sender, args) => updatePayload();
            updatePayload();
            layout.Controls.Add(payload, 0, 2);

            var languages = new GroupBox { Text = "Language overrides", Dock = DockStyle.Fill, Padding = new Padding(12) };
            LanguageTable.Dock = DockStyle.Fill;
            LanguageTable.BackgroundColor = SystemColors.Window;
            LanguageTable.BorderStyle = BorderStyle.FixedSingle;
            LanguageTable.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            LanguageTable.AllowUserToAddRows = true;
            LanguageTable.AllowUserToDeleteRows = true;
            LanguageTable.AllowUserToResizeRows = false;
            LanguageTable.RowHeadersWidth = 28;
            LanguageTable.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LanguageTable.AutoGenerateColumns = false;
            LanguageTable.AccessibleName = "Extension to language mappings";
            LanguageTable.Columns.Add(new DataGridViewTextBoxColumn { Name = "Extension", HeaderText = "Extension",
                FillWeight = 35, SortMode = DataGridViewColumnSortMode.NotSortable });
            LanguageTable.Columns.Add(new DataGridViewTextBoxColumn { Name = "Language", HeaderText = "Language name",
                FillWeight = 65, SortMode = DataGridViewColumnSortMode.NotSortable });
            foreach (var mapping in current.Languages.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                LanguageTable.Rows.Add(mapping.Key, mapping.Value);
            languages.Controls.Add(LanguageTable);
            layout.Controls.Add(languages, 0, 3);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.Controls.Add(new Label { Text = "Examples: py → Python, tpl → Template. Empty table uses editor languages.",
                AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText }, 0, 0);
            var rowActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            var add = new Button { Text = "Add", AutoSize = true };
            add.Click += (sender, args) => {
                var row = LanguageTable.Rows.Add();
                LanguageTable.CurrentCell = LanguageTable.Rows[row].Cells[0];
                LanguageTable.BeginEdit(true);
            };
            var remove = new Button { Text = "Remove", AutoSize = true };
            remove.Click += (sender, args) => {
                foreach (var row in LanguageTable.SelectedRows.Cast<DataGridViewRow>().ToArray())
                    if (!row.IsNewRow) LanguageTable.Rows.Remove(row);
            };
            rowActions.Controls.Add(add);
            rowActions.Controls.Add(remove);
            actions.Controls.Add(rowActions, 1, 0);
            layout.Controls.Add(actions, 0, 4);
            layout.Controls.Add(error, 0, 5);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Margin = Padding.Empty };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
            var apply = new Button { Text = "Save", Width = 90, Height = 30 };
            apply.Click += (sender, args) => { if (TrySave()) { DialogResult = DialogResult.OK; Close(); } };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(apply);
            layout.Controls.Add(buttons, 0, 6);
            AcceptButton = apply;
            CancelButton = cancel;

            port.Value = current.Port;
            frequency.Value = current.FrequencySeconds;
            pulse.Value = current.PulseTimeSeconds;
            idle.Value = current.IdleSeconds;
            capacity.Value = current.QueueCapacity;
            testing.Checked = current.Testing;
            port.ValueChanged += (sender, args) => UpdateEndpoint();
            testing.CheckedChanged += (sender, args) => UpdateEndpoint();
            UpdateEndpoint();
            error.Text = warning ?? "";
        }

        private static NumericUpDown Number(int minimum, int maximum)
        {
            return new NumericUpDown { Minimum = minimum, Maximum = maximum, Width = 92,
                Anchor = AnchorStyles.Left, ThousandsSeparator = true };
        }

        private void AddField(TableLayoutPanel panel, string label, NumericUpDown input, int column, int row, string hint)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, column, row);
            panel.Controls.Add(input, column + 1, row);
            hints.SetToolTip(input, hint);
            input.AccessibleName = label;
        }

        private void UpdateEndpoint()
        {
            var effectivePort = testing.Checked && port.Value == 5600 ? 5666 : (int)port.Value;
            endpoint.Text = "127.0.0.1:" + effectivePort;
        }

        internal Settings ReadSettings()
        {
            LanguageTable.EndEdit();
            var rows = LanguageTable.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow)
                .Select(row => new KeyValuePair<string, string>(Convert.ToString(row.Cells[0].Value),
                    Convert.ToString(row.Cells[1].Value)));
            var value = new Settings { Enabled = TrackingEnabled.Checked, PayloadMode = PayloadMode.Text,
                IncludeFile = IncludeFile.Checked, IncludeProjectPath = IncludeProjectPath.Checked,
                IncludeLanguage = IncludeLanguage.Checked, IncludeFilename = IncludeFilename.Checked,
                IncludeProjectName = IncludeProjectName.Checked, Port = (int)port.Value, Testing = testing.Checked,
                FrequencySeconds = (int)frequency.Value, PulseTimeSeconds = (int)pulse.Value,
                IdleSeconds = (int)idle.Value, QueueCapacity = (int)capacity.Value,
                Languages = Settings.BuildLanguages(rows) };
            value.Validate();
            return value;
        }

        internal bool TrySave()
        {
            try { save(ReadSettings()); error.Text = ""; return true; }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException ||
                ex is System.Security.SecurityException)
            {
                error.Text = ex is ArgumentException ? ex.Message : "Could not save settings. Check that the configuration file is writable.";
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) hints.Dispose();
            base.Dispose(disposing);
        }
    }
}
