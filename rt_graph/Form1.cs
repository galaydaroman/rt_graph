using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace rt_graph
{
    public partial class Form1 : Form
    {
        private BufferedGraphicsContext context;
        private BufferedGraphics bufer;
        private Graphics panel_canvas;
        private Graphics canvas;

        private Dictionary<DateTime, List<Temp>> ranges = new Dictionary<DateTime, List<Temp>>();
        private DateTime selectedRangeKey = DateTime.MinValue;
        private Temp[] temps;
        private bool resizeNeed = true;
        private bool reloadNeed = false;
        private string curFileName = string.Empty;
        private Font font = new Font("Consolas", 8.0f);

        private int max_height = 108;
        private int minutePx = 16;
        private int secondsStrip = 90;
        private int secondsStripRectWidth = 10;
        private int minuteAxisStep = 30;
        private int detailMinuteAxisStep = 10;

        private string registry_path = "SOFTWARE\\Microsoft\\RealTempGraph";
        private string registry_recent_key = "recentFile";

        private Temp[] p_temps;
        private Thread thread;
        private object locker = new object();
        private object locker2 = new object();

        private bool first_load = true;

        private Thread main_thread;

        public Form1()
        {
            InitializeComponent();
            this.MouseWheel += new MouseEventHandler(panel1_MouseWheel);
            main_thread = Thread.CurrentThread;
        }

        void panel1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (hScrollBar1.Visible && e.Delta != 0)
            {
                int new_value = (int)Math.Round(hScrollBar1.Value - (e.Delta / (double)10));
                hScrollBar1.Value = Math.Min(hScrollBar1.Maximum, Math.Max(hScrollBar1.Minimum, new_value));
                hScrollBar1_Scroll(sender, null);
            }
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            context = BufferedGraphicsManager.Current;
            panel_canvas = panel1.CreateGraphics();
            bufer = context.Allocate(panel_canvas, panel1.ClientRectangle);
            canvas = bufer.Graphics;

            realtimeUpdateToolStripMenuItem.Checked = timer1.Enabled = false;
        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {
            ResizeContext();
            DrawTemp();
            bufer.Render();
        }

        private void graph_paint()
        {
            panel1_Paint(null, new PaintEventArgs(panel_canvas, panel1.ClientRectangle));
        }

        private IList<Temp> getSource()
        {
            IList<Temp> res;
            lock (locker)
            {
                res = selectedRangeKey != DateTime.MinValue ? (IList<Temp>)ranges[selectedRangeKey] : temps;
            }
            return res;
        }

       

        private List<Point> cpu_max = new List<Point>();
        private List<Point> cpu_ava = new List<Point>();
        private IList<Point>[] cpu;
        private List<Point> gpu = new List<Point>();
        private List<Point> load = new List<Point>();
        private List<Rectangle> rects = new List<Rectangle>();
        private int canvas_max_width = 0;
        private int cpu_count = 1;

        private void DrawTemp()
        {
            IList<Temp> source = getSource();
            int height = panel1.ClientSize.Height;
            int width = panel1.ClientSize.Width;
            Func<int, int> get_y = y => { return (int)Math.Round((double)(height * y / max_height)); };

            //initial draw
            canvas.Clear(Color.White);
            Pen p = new Pen(Color.DarkGray, 2);
            p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
            canvas.DrawLine(p, 0, get_y(max_height - 100), width, get_y(max_height - 100));
            p = new Pen(Color.Gray, 1);
            p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            for (int j = 1; j < 10; j++)
            {
                if (j % 2 == 0)
                {
                    canvas.DrawString(string.Format("{0}°", j * 10), font, Brushes.Black, 2, height - get_y(j * 10) - 13);
                } else if (j == 9)
                {
                    canvas.DrawString(string.Format("{0}°", 100), font, Brushes.Black, 2, height - get_y(100) - 13);
                }
                p.Color = j % 2 != 0 ? Color.Gray : Color.Black;
                p.DashStyle = j % 2 != 0 ? System.Drawing.Drawing2D.DashStyle.Dot : System.Drawing.Drawing2D.DashStyle.Dash;
                canvas.DrawLine(p, 0, height - get_y(j * 10), width, height - get_y(j * 10));
            }
            canvas.DrawRectangle(Pens.Black, 0, 0, width - 1, height - 1);

            List<int> cpus = new List<int>();
            for (int q = 0; q < cpu_count; q++) if (isCPUChecked(q)) cpus.Add(q);

            if (reloadNeed)
            {
                reloadNeed = false;

                cpu_max.Clear();
                cpu_ava.Clear();
                gpu.Clear();
                load.Clear();
                rects.Clear();
                foreach (var l in cpu) l.Clear();

                int x = 1;
                if (source != null && source.Count() > 0)
                {
                    for (int i = 0; i < source.Count(); i++)
                    {
                        if (i > 0)
                        {
                            var dis = (source[i].time - source[i - 1].time).TotalSeconds;
                            if (dis > secondsStrip)
                            {
                                rects.Add(new Rectangle
                                {
                                    X = x,
                                    Y = 0,
                                    Height = max_height,
                                    Width = secondsStripRectWidth
                                });
                                x += secondsStripRectWidth;
                            }
                            else
                            {
                                float step = (float)(dis / 60) * minutePx;
                                int stepInt = (int)Math.Round(step);
                                if (stepInt > 0) x += stepInt;
                                else continue;
                            }
                        }
                        if (mAXToolStripMenuItem.Checked)
                            cpu_max.Add(new Point
                            {
                                X = x,
                                Y = source[i].cpu_max
                            });
                        if (avarageToolStripMenuItem.Checked)
                            cpu_ava.Add(new Point
                            {
                                X = x,
                                Y = (int)Math.Round(source[i].cpu_avar)
                            });
                        //if (gPUToolStripMenuItem.Checked)
                            gpu.Add(new Point
                            {
                                X = x,
                                Y = (int)(source[i].gpu ?? 0)
                            });
                        if (lOADToolStripMenuItem.Checked)
                            load.Add(new Point
                            {
                                X = x,
                                Y = (int)Math.Round(source[i].load)
                            });

                        
                        foreach (var c in cpus)
                        {
                            cpu[c].Add(new Point
                            {
                                X = x,
                                Y = source[i].cpu[c]
                            });
                        }
                    }
                }
                canvas_max_width = x;

                //scrollbar
                if (panel1.Width < x)
                {
                    hScrollBar1.Minimum = 0;
                    hScrollBar1.Maximum = (int)(x / 10) + 100;
                    hScrollBar1.Show();
                }
                else
                {
                    hScrollBar1.Hide();
                }
            }

            int start = hScrollBar1.Visible ? hScrollBar1.Value * 10 : 0;
            int canvas_width = panel1.Width;
            Pen[] cpu_pen = new Pen[] { Pens.Khaki, Pens.Maroon, Pens.Pink, Pens.RosyBrown, Pens.DarkCyan, Pens.DarkMagenta, Pens.DarkTurquoise };
            Action<IList<Point>, Pen, int> draw_line = (list, pen, pos) =>
                {
                    canvas.DrawLine(pen, 
                        list[pos].X - start, height - get_y(list[pos].Y), 
                        list[pos + 1].X - start, height - get_y(list[pos + 1].Y));
                };
            for (int i = 2; i < gpu.Count; i++)
            {
                int cur_x = gpu[i - 1].X;
                if (cur_x >= (start + canvas_width))
                {
                    break;
                }
                else if (cur_x >= start)
                {
                    DrawMinuteScale(source, i, start, cur_x);
                    if (mAXToolStripMenuItem.Checked) draw_line(cpu_max, Pens.Red, i - 1);
                    if (gPUToolStripMenuItem.Checked) draw_line(gpu, Pens.Green, i - 1);
                    if (lOADToolStripMenuItem.Checked) draw_line(load, Pens.Blue, i - 1);
                    if (avarageToolStripMenuItem.Checked) draw_line(cpu_ava, Pens.Black, i - 1);
                    foreach (var c in cpus) draw_line(
                        cpu[c],  
                        c < cpu_pen.Length ? cpu_pen[c] : cpu_pen.Last(), 
                        i - 1
                    );
                }
            }
            foreach (var r in rects.Where(x => x.X >= start && x.X < (start + canvas_width)))
            {
                canvas.FillRectangle(SystemBrushes.ButtonFace, 
                    r.X - start, 1, r.Width, get_y(r.Height) - 2);
            }
        }

        private void DrawMinuteScale(IList<Temp> source, int cur_index, int start_x, int cur_x)
        {
            if (source[cur_index].time.Minute != source[cur_index - 1].time.Minute)
            {
                bool detailScale = true;
                if (source[cur_index].time.Minute % minuteAxisStep == 0) detailScale = false;
                else if (source[cur_index].time.Minute % detailMinuteAxisStep == 0) detailScale = true;
                else return;

                int height = panel1.ClientSize.Height - 2;
                int x = (int)Math.Round((double)(source[cur_index].time.Second / 60) * minutePx);
                x = cur_x - x - start_x;

                Pen linePen = (Pen)Pens.LightGray.Clone();
                if (detailScale) linePen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                canvas.DrawLine(linePen, x, 1, x, height);
                if (!detailScale)
                {
                    string text = source[cur_index].time.ToString("HH:mm");
                    canvas.DrawString(text, font, Brushes.Black, x + 2, 5);
                }
            }
        }

        private void ResizeContext()
        {
            if (resizeNeed) 
            {
                bufer = context.Allocate(panel_canvas, panel1.ClientRectangle);
                canvas = bufer.Graphics;
                resizeNeed = false;
            }
        }

        private void panel1_Resize(object sender, EventArgs e)
        {
            resizeNeed = true;
            graph_paint();
        }

        private void updateTimeRanges()
        {
            lock (locker)
            {
                ranges.Clear();
                foreach (var t in temps)
                {
                    var date = t.time.Date;
                    if (!ranges.ContainsKey(date))
                    {
                        ranges.Add(date, new List<Temp>());
                    }
                    ranges[date].Add(t);
                }
                for (int i = timeRangeToolStripMenuItem.DropDownItems.Count - 1; i > 0; i--)
                {
                    var m = timeRangeToolStripMenuItem.DropDownItems[i];
                    if (m.Tag != null) timeRangeToolStripMenuItem.DropDownItems.Remove(m);
                }
                foreach (var d in ranges.Keys)
                {
                    var item = timeRangeToolStripMenuItem.DropDownItems.Add(d.ToShortDateString(), null, timerangeitem_click);
                    item.Tag = d;
                }
                allToolStripMenuItem.Checked = true;
            }
        }

        private bool isCPUChecked(int number)
        {
            var q = cPUsToolStripMenuItem.DropDownItems.OfType<ToolStripMenuItem>()
                .Where(x => x.Tag != null && (int)x.Tag == number).FirstOrDefault();
            return q != null ? q.Checked : false;
        }

        private void updateCpusMenu()
        {
            var converter = new KeysConverter();
            lock (locker)
            {
                var count = cPUsToolStripMenuItem.DropDownItems.Count;
                for (int i = count - 1; i > 2; i--)
                    cPUsToolStripMenuItem.DropDownItems.RemoveAt(i);
                count = temps.FirstOrDefault().cpu.Length;
                cpu_count = count;
                cpu = new IList<Point>[cpu_count];

                for (int i = 0; i < count; i++)
                {
                    cpu[i] = new List<Point>();
                    var item = cPUsToolStripMenuItem.DropDownItems.Add("CPU" + i.ToString(), null, cpumenustrip_click);
                    item.Tag = i;
                    (item as ToolStripMenuItem).ShortcutKeys = (Keys)converter.ConvertFromString("Ctrl+" + (i + 5).ToString());
                }
                if (cPUsToolStripMenuItem.DropDownItems.OfType<ToolStripMenuItem>().Count(x => x.Checked == true) == 0)
                    mAXToolStripMenuItem.Checked = true;
            }
        }

        private void cpumenustrip_click(object sender, EventArgs e)
        {
            toolStripMenuItem_Click(sender, e);
        }

        private void timerangeitem_click(object sender, EventArgs e)
        {
            reloadNeed = true;
            selectedRangeKey = (DateTime)(sender as ToolStripItem).Tag;
            this.Invalidate();
            foreach (var i in timeRangeToolStripMenuItem.DropDownItems)
            {
                if (i is ToolStripMenuItem) (i as ToolStripMenuItem).Checked = false;
            }
            (sender as ToolStripMenuItem).Checked = true;
        }

        private void load_data()
        {
            p_temps = temp_parser.ParseFile(curFileName);
            p_temps = temp_parser.SmoothTime(p_temps);
        }

        private void openFileLog(string file_name)
        {
            lock (locker2)
            {
                curFileName = file_name;
                reloadNeed = true;
                thread = new Thread(load_data);
                thread.Start();
                long start_time = DateTime.Now.Ticks;
                while (true)
                {
                    Thread.Sleep(50);
                    bool timeout = TimeSpan.FromTicks(DateTime.Now.Ticks - start_time).TotalSeconds > 15;
                    if (timeout)
                    {
                        thread.Abort();
                        break;
                    }
                    else if (thread.ThreadState == ThreadState.Stopped)
                    {
                        lock (locker)
                        {
                            temps = p_temps.ToList().ToArray();
                        }
                        break;
                    }
                    Application.DoEvents();
                }
                //temps = temp_parser.ParseFile(curFileName);
                //temps = temp_parser.SmoothTime(temps);
                updateTimeRanges();
                updateCpusMenu();
                graph_paint();

                try
                {
                    //registry save
                    RegistryKey key = Registry.CurrentUser.CreateSubKey(registry_path);
                    key.SetValue(registry_recent_key, curFileName);
                    key.Close();
                }
                catch { }

                scrollToEndPoint();

                // enable truncate menu option
                truncateToolStripMenuItem.Enabled = true;
            }
        }

        private delegate void MenuClick();
        private void scrollToEndPoint()
        {
            if (first_load)
            {
                try
                {
                    first_load = false;
                    var items = timeRangeToolStripMenuItem.DropDownItems.OfType<ToolStripMenuItem>();
                    if (items.Count() > 1)
                    {
                        var item = items.Last();
                        IAsyncResult async_result = this.BeginInvoke(new MenuClick(item.PerformClick));
                        this.EndInvoke(async_result);
                        Application.DoEvents();

                        if (hScrollBar1.Visible)
                        {
                            int new_value = Math.Max(0, hScrollBar1.Maximum - 150);
                            if (new_value > 250)
                            {
                                int count_iteration = 70;
                                hScrollBar1.Value = 0;
                                for (var i = 0; i <= count_iteration; i++)
                                {
                                    int val = i == count_iteration ? new_value : (i > (int)Math.Round(count_iteration / 2d) ? new_value / 2 : new_value / 4);
                                    new_value -= val;
                                    hScrollBar1.Value += val;
                                    //Thread.Sleep(2);
                                    graph_paint();
                                    Application.DoEvents();
                                    //hScrollBar1_Scroll(null, null);
                                }
                            }
                            else
                            {
                                hScrollBar1.Value = new_value;
                                hScrollBar1_Scroll(null, null);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void openFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
            {
                openFileLog(openFileDialog1.FileName);
            }
        }

        private void allToolStripMenuItem_Click(object sender, EventArgs e)
        {
            reloadNeed = true;
            selectedRangeKey = DateTime.MinValue;
            graph_paint();
        }

        private void toolStripMenuItem_Click(object sender, EventArgs e)
        {
            (sender as ToolStripMenuItem).Checked = !(sender as ToolStripMenuItem).Checked;
            reloadNeed = true;
            graph_paint();
        }

        private void hScrollBar1_Scroll(object sender, ScrollEventArgs e)
        {
            graph_paint();
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(curFileName))
            {
                openFileLog(curFileName);
            }
            else
            {
                recentToolStripMenuItem_Click(sender, new EventArgs());
            }
        }

        private void recentToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.CreateSubKey(registry_path);
                string recent_name = (string)key.GetValue(registry_recent_key, string.Empty);
                key.Close();
                if (!string.IsNullOrEmpty(recent_name)) openFileLog(recent_name);
            }
            catch (Exception ex) { MessageBox.Show("Couldn't be open recent file. \n\nError: \n" + ex.Message); }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            try
            {
                refreshToolStripMenuItem_Click(sender, new EventArgs());
            }
            catch { }
        }

        private void realtimeUpdateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            realtimeUpdateToolStripMenuItem.Checked = timer1.Enabled = !timer1.Enabled;
        }

        private void truncateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (temps.Length == 0) return;

            Temp lastRecord = temps.Last();
            string dateString = lastRecord.time.ToString("MM/dd/yy");
            string[] lines = File.ReadAllLines(curFileName);
            lines = lines.Where((line, index) => index == 0 || line.StartsWith(dateString)).ToArray();
            File.WriteAllLines(curFileName, lines);

            openFileLog(curFileName);
        }
    }
}
    