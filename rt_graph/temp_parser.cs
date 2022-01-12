using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace rt_graph
{
    public struct Temp
    {
        public DateTime time;
        public float freq;
        public int[] cpu;
        public float cpu_avar;
        public int cpu_max;
        public int cpu_min;
        public int? gpu;
        public float load;
    }

    public class temp_parser
    {
        public static Temp[] ParseFile(string FileName)
        {
            string[] text = File.ReadAllLines(FileName);
            List<Temp> temps = new List<Temp>(text.Length);
            
            string line;
            string[] values;
            int cpu_count;
            int load_index;
            int gpu_index;
            System.Globalization.CultureInfo provider = new System.Globalization.CultureInfo("en-US");

            if (text.Length > 0)
            {
                line = text[0].Trim();
                values = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                cpu_count = values.Count(label => label.StartsWith("CPU"));
                load_index = cpu_count + 3;
                gpu_index = load_index + 1;

                for (int i = 1; i < text.Length; i++)
                {
                    line = text[i];
                    values = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int[] cpu = new int[cpu_count];

                    for (int index = 0; index < cpu_count; index++)
                    {
                        cpu[index] = int.Parse(values[3 + index]);
                    }

                    Temp temp = new Temp
                    {
                        time = DateTime.ParseExact(values[0] + " " + values[1], "MM/dd/yy HH:mm:ss", provider),
                        freq = float.Parse(values[2], provider),
                        cpu = cpu,
                        cpu_max = cpu.Max(),
                        cpu_min = cpu.Min(),
                        cpu_avar = (float)cpu.Average(),
                        load = float.Parse(values[load_index], provider),
                        gpu = values.Length > gpu_index ? (int?)int.Parse(values[gpu_index]) : null
                    };
                    temps.Add(temp);
                }
            }

            return temps.ToArray();
        }

        public static Temp[] SmoothTime(Temp[] temps)
        {
            if (temps != null && temps.Length > 0)
            {
                Action<int, int> smooth = (start, end) =>
                {
                    int _count = end - start;
                    int step = 5;
                    if (temps.Length - end > 1)
                    {
                        TimeSpan ts = temps[end + 1].time - temps[start].time;
                        step = (int)Math.Round(ts.TotalSeconds / (_count + 1));
                    }
                    DateTime startTime = temps[start].time;
                    for (int j = start + 1; j <= end; j++)
                    {
                        startTime = startTime.AddSeconds(step);
                        temps[j].time = startTime;
                    }
                };
                var count = 0;
                for (int i = 1; i < temps.Length; i++)
                {
                    if (temps[i - 1].time.Ticks == temps[i].time.Ticks)
                    {
                        count++;
                    }
                    else if (count > 0)
                    {
                        smooth(i - count - 1, i - 1);
                        count = 0;
                    }
                }
                if (count > 0) smooth(temps.Length - count - 1, temps.Length - 1);
            }
            return temps;
        }

        
    }
}
