using Oxide.Core;
using System;
using System.IO;
using System.Reflection;

namespace Oxide.Plugins
{
    [Info("DigitalClockDebug", "networkluki", "1.0.3")]
    [Description("Dumps DigitalClock members to a file instead of console")]
    public class DigitalClockDebug : RustPlugin
    {
        void OnServerInitialized()
        {
            // Writes to: oxide/logs/DigitalClockDump.txt
            string path = Path.Combine(Interface.Oxide.LogDirectory, "DigitalClockDump.txt");

            var flags = BindingFlags.Instance
                      | BindingFlags.Static
                      | BindingFlags.Public
                      | BindingFlags.NonPublic
                      | BindingFlags.DeclaredOnly;

            using (var writer = new StreamWriter(path, false))
            {
                Type t = typeof(DigitalClock);
                while (t != null && t.Name != "MonoBehaviour" && t.Name != "Object")
                {
                    writer.WriteLine($"=== CLASS: {t.FullName} ===");

                    foreach (MethodInfo m in t.GetMethods(flags))
                    {
                        var ps = m.GetParameters();
                        string p = ps.Length == 0 ? ""
                            : string.Join(", ", Array.ConvertAll(ps, x => x.ParameterType.Name + " " + x.Name));
                        writer.WriteLine($"  METHOD  {m.ReturnType.Name} {m.Name}({p})");
                    }

                    foreach (FieldInfo f in t.GetFields(flags))
                        writer.WriteLine($"  FIELD   {f.FieldType.Name} {f.Name}");

                    foreach (PropertyInfo p in t.GetProperties(flags))
                        writer.WriteLine($"  PROP    {p.PropertyType.Name} {p.Name}");

                    t = t.BaseType;
                }

                writer.WriteLine("=== DONE ===");
            }

            Puts($"[DigitalClockDebug] Dump written to: {path}");
        }
    }
}
