using System;
using System.Windows.Forms;

namespace Plugins
{
    public partial class Configure : Form
    {
        public string Configuration;
        private readonly Main _owner;

        public Configure(Main owner)
        {
            InitializeComponent();
            _owner = owner;
        }

        private void Configure_Load(object sender, EventArgs e)
        {
            numWidth.Value = _owner.LineWidth;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            _owner.LineWidth = (int) numWidth.Value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
