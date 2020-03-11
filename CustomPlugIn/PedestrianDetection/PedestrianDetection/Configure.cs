using System;
using System.Windows.Forms;

using OpenCvSharp;



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
            lblColor.BackColor = _owner.MyColor;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            _owner.MyColor = lblColor.BackColor;
            _owner._scalar = Scalar.FromRgb(_owner.MyColor.R, _owner.MyColor.G, _owner.MyColor.B);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void lblColor_Click(object sender, EventArgs e)
        {
           DialogResult dr= colorDialog1.ShowDialog();
            if(dr==DialogResult.OK)
            {
                lblColor.BackColor = colorDialog1.Color;
            }
        }
    }
}
