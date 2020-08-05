using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Runtime.InteropServices;
using System.IO;
using System.Drawing.Imaging;

namespace FITSDominator
{
    public partial class Form1 : Form
    {
#if !__MonoCS__
        [DllImport("kernel32")]
        static extern bool AllocConsole();
#endif

        public Form1()
        {
            InitializeComponent();

#if DEBUG && (!__MonoCS__)
            AllocConsole();
#endif
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void loadFITSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if ( openFileDialogFits.ShowDialog() == DialogResult.OK)
            {
                byte[] fitsData = File.ReadAllBytes(openFileDialogFits.FileName);
                FitsDataModel fits = new FitsDataModel(fitsData);

                fits.Dump();
                DebugPlotImage(fits);
            }
        }

        private void DebugPlotImage (FitsDataModel fits)
        {
            FitsEntry primary = fits.GetPrimary();

            int width = primary.GetParam<int>("NAXIS1");
            int height = primary.GetParam<int>("NAXIS2");
            int bpp = primary.GetParam<int>("BITPIX") / 8;

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            for (int y=0; y<height; y++)
            {
                for (int x=0; x<width; x++)
                {
                    byte b1 = primary.data[(y * width  + x) * bpp + 0];
                    byte b2 = primary.data[(y * width  + x) * bpp + 1];
                    int val = ((int)b1 << 8) | b2;
                    int rgbVal = 255 - b2;

                    bitmap.SetPixel(x, y, Color.FromArgb(rgbVal, rgbVal, rgbVal));
                }
            }

            pictureBox2.Image = bitmap;
            pictureBox2.Invalidate();
        }

        private Point pntStart = new Point();

        private void pictureBox2_MouseDown(object sender, MouseEventArgs e)
        {
            pntStart = new Point(e.X, e.Y);
        }

        private void pictureBox2_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                int X = (pntStart.X - e.X);
                int Y = (pntStart.Y - e.Y);

                splitContainer2.Panel2.AutoScrollPosition =
                   new Point((X - splitContainer2.Panel2.AutoScrollPosition.X),
                   (Y - splitContainer2.Panel2.AutoScrollPosition.Y));
            }
        }
    }
}
