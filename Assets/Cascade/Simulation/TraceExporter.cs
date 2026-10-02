using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cascade.Simulation
{
    /// <summary>Writes decision traces as JSON Lines for offline analysis (one decision per line).</summary>
    public static class TraceExporter
    {
        public static int Export(SimulationWorld world, string path)
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (var a in world.Agents)
            {
                if (a.IsPlayer) continue;
                foreach (var t in a.Brain.Log.Traces)
                {
                    sb.Append(t.ToJson(a.Brain.Name)).Append('\n');
                    count++;
                }
            }
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString());
            return count;
        }
    }
}
