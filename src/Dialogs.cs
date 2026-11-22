using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XiaoYaOpt
{
    /// <summary>輸入名稱的小對話框。</summary>
    internal sealed class PromptDialog : Form
    {
        private readonly TextBox _txt = new TextBox();
        private readonly Func<string, string> _validate;

        public string Value { get { return _txt.Text.Trim(); } }

        public PromptDialog(string title, string label, string initial, Func<string, string> validate)
        {
            _validate = validate;
            Text = title;
            Font = new Font("Microsoft JhengHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 110);

            Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(12, 14) });
            _txt.SetBounds(12, 38, 316, 23);
            _txt.Text = initial ?? "";
            Controls.Add(_txt);
            var ok = new Button { Text = "確定", DialogResult = DialogResult.None };
            ok.SetBounds(172, 72, 75, 28);
            ok.Click += OnOk;
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(253, 72, 75, 28);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void OnOk(object sender, EventArgs e)
        {
            string err = _validate != null ? _validate(Value) : null;
            if (err != null)
            {
                MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
